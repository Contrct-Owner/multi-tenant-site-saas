import type { FeatureCollection } from 'geojson';
import type * as MapLibre from 'maplibre-gl';
import { useEffect, useRef } from 'react';
import { cn } from '../lib/utils';

/**
 * The console map (ADR 50 §5): MapLibre GL JS behind the barrel. Pages hand
 * it points, a basemap, and a viewport callback; nothing outside this file
 * knows the library. The library and its stylesheet load on demand the first
 * time a map mounts, so pages that never show one pay nothing and the module
 * stays safe to import where there is no window.
 *
 * Accessibility: the canvas is presentational; the list beside the map is
 * the accessible equivalent (the page owns that). Basemaps are open and
 * keyless: OpenStreetMap's raster tiles, and OpenFreeMap's light and dark
 * vector styles - attribution is rendered by the map control.
 */
export type SiteMapPoint = {
  id: string;
  name: string;
  latitude: number;
  longitude: number;
  subtitle?: string;
  selected?: boolean;
};

export type SiteMapViewport = {
  west: number;
  south: number;
  east: number;
  north: number;
  zoom: number;
};

export type SiteMapBasemap = 'osm' | 'light' | 'dark';

const BASEMAPS: Record<SiteMapBasemap, string | MapLibre.StyleSpecification> = {
  osm: {
    version: 8,
    sources: {
      osm: {
        type: 'raster',
        tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
        tileSize: 256,
        maxzoom: 19,
        attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      },
    },
    layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
  },
  light: 'https://tiles.openfreemap.org/styles/positron',
  dark: 'https://tiles.openfreemap.org/styles/dark',
};

const SOURCE = 'premise-sites';
const LAYER_HALO = 'premise-sites-halo';
const LAYER_DOT = 'premise-sites-dot';
// MapLibre paints literal colors, not CSS tokens: the accent and neutral pin
// from tokens.css, in hex, for both schemes
const ACCENT = '#7c6cf0';
const NEUTRAL_DOT = '#5b5b66';
const STROKE = '#ffffff';

type Props = {
  points: SiteMapPoint[];
  basemap?: SiteMapBasemap;
  /** Bump this to fit the view to the current points (Fit to scope). */
  fitKey?: number;
  /** After the user pans or zooms (debounced): the box in view, and the zoom. */
  onViewportChange?: (viewport: SiteMapViewport) => void;
  onPointClick?: (id: string) => void;
  className?: string;
};

function toGeoJson(points: SiteMapPoint[]): FeatureCollection {
  return {
    type: 'FeatureCollection',
    features: points.map((p) => ({
      type: 'Feature',
      id: p.id,
      geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] },
      properties: {
        id: p.id,
        name: p.name,
        subtitle: p.subtitle ?? '',
        selected: p.selected ? 1 : 0,
      },
    })),
  };
}

function addSitesLayers(map: MapLibre.Map, data: FeatureCollection) {
  if (map.getSource(SOURCE)) return;
  map.addSource(SOURCE, { type: 'geojson', data });
  map.addLayer({
    id: LAYER_HALO,
    type: 'circle',
    source: SOURCE,
    filter: ['==', ['get', 'selected'], 1],
    paint: { 'circle-radius': 16, 'circle-color': ACCENT, 'circle-opacity': 0.18 },
  });
  map.addLayer({
    id: LAYER_DOT,
    type: 'circle',
    source: SOURCE,
    paint: {
      'circle-radius': ['case', ['==', ['get', 'selected'], 1], 8, 6],
      'circle-color': ['case', ['==', ['get', 'selected'], 1], ACCENT, NEUTRAL_DOT],
      'circle-stroke-color': STROKE,
      'circle-stroke-width': 2,
    },
  });
}

function bounds(points: SiteMapPoint[]): [[number, number], [number, number]] | null {
  if (points.length === 0) return null;
  let west = Infinity, south = Infinity, east = -Infinity, north = -Infinity;
  for (const p of points) {
    west = Math.min(west, p.longitude);
    east = Math.max(east, p.longitude);
    south = Math.min(south, p.latitude);
    north = Math.max(north, p.latitude);
  }
  return [[west, south], [east, north]];
}

export function SiteMap({
  points,
  basemap = 'light',
  fitKey = 0,
  onViewportChange,
  onPointClick,
  className,
}: Props) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibre.Map | null>(null);
  const libRef = useRef<typeof MapLibre | null>(null);
  const pointsRef = useRef(points);
  const viewportCb = useRef(onViewportChange);
  const clickCb = useRef(onPointClick);
  const lastFit = useRef(fitKey);
  // true once the view means something: fitted to points, or moved by the user
  const positioned = useRef(false);
  pointsRef.current = points;
  viewportCb.current = onViewportChange;
  clickCb.current = onPointClick;

  // mount once: load the library, create the map, wire events
  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const el = container.current;
    if (!el) return;
    void (async () => {
      const [lib, , worker] = await Promise.all([
        import('maplibre-gl'),
        import('maplibre-gl/dist/maplibre-gl.css'),
        // Bundled as a chunk, the library cannot find its own script to spawn
        // its worker from (workerUrl resolves to ""), and a worker at the page
        // URL boots nothing: every tile and GeoJSON request then waits forever.
        // Hand it the worker file as an asset the bundler emits.
        // `?worker&url`: the bundler builds the worker entry as its own
        // self-contained script and hands back its URL - a plain `?url` copy
        // still imports shared chunks that touch `window` and dies on boot
        import('maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'),
      ]);
      if (disposed) return;
      lib.setWorkerUrl(worker.default);
      libRef.current = lib;
      const initial = bounds(pointsRef.current);
      const map = new lib.Map({
        container: el,
        style: BASEMAPS[basemap],
        ...(initial
          ? { bounds: initial, fitBoundsOptions: { padding: 48, maxZoom: 13 } }
          : { center: [-98, 39], zoom: 3 }),
        attributionControl: { compact: false },
      });
      positioned.current = initial !== null;
      map.addControl(new lib.NavigationControl({ showCompass: false }), 'top-right');
      const emitViewport = () => {
        if (timer) clearTimeout(timer);
        timer = setTimeout(() => {
          const b = map.getBounds();
          viewportCb.current?.({
            west: b.getWest(),
            south: b.getSouth(),
            east: b.getEast(),
            north: b.getNorth(),
            zoom: map.getZoom(),
          });
        }, 250);
      };
      map.on('load', () => {
        addSitesLayers(map, toGeoJson(pointsRef.current));
        // the list follows the map from the first frame, not the first drag
        if (positioned.current) emitViewport();
      });
      map.on('style.load', () => addSitesLayers(map, toGeoJson(pointsRef.current)));
      map.on('movestart', (e) => {
        if ((e as { originalEvent?: unknown }).originalEvent) positioned.current = true;
      });
      map.on('moveend', emitViewport);
      map.on('click', LAYER_DOT, (e) => {
        const id = e.features?.[0]?.properties?.id as string | undefined;
        if (id) clickCb.current?.(id);
      });
      map.on('mouseenter', LAYER_DOT, () => { map.getCanvas().style.cursor = 'pointer'; });
      map.on('mouseleave', LAYER_DOT, () => { map.getCanvas().style.cursor = ''; });
      mapRef.current = map;
      // test hook: browser suites and a console can reach the instance
      // through the element; nothing in the app reads it
      (el as HTMLDivElement & { __map?: MapLibre.Map }).__map = map;
    })();
    return () => {
      disposed = true;
      if (timer) clearTimeout(timer);
      mapRef.current?.remove();
      mapRef.current = null;
    };
    // the basemap is applied through setStyle below; the map is created once
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // points changed: update the source in place; if the map was created before
  // its points existed, the first points position it
  useEffect(() => {
    const map = mapRef.current;
    const source = map?.getSource(SOURCE) as MapLibre.GeoJSONSource | undefined;
    source?.setData(toGeoJson(points));
    const b = bounds(points);
    if (map && b && !positioned.current) {
      positioned.current = true;
      map.fitBounds(b, { padding: 48, maxZoom: 13, duration: 0 });
    }
  }, [points]);

  // basemap changed: swap the style, layers are re-added on style.load
  useEffect(() => {
    const map = mapRef.current;
    if (map && map.isStyleLoaded()) map.setStyle(BASEMAPS[basemap]);
  }, [basemap]);

  // fit requested
  useEffect(() => {
    if (fitKey === lastFit.current) return;
    lastFit.current = fitKey;
    const map = mapRef.current;
    const b = bounds(points);
    if (map && b) {
      positioned.current = true;
      map.fitBounds(b, { padding: 48, maxZoom: 13, duration: 400 });
    }
  }, [fitKey, points]);

  return (
    <div
      ref={container}
      role="presentation"
      aria-hidden
      className={cn('h-[560px] w-full bg-muted [&_.maplibregl-ctrl-attrib]:text-[11px]', className)}
    />
  );
}
