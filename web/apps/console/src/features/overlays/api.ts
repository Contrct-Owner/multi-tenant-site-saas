import { api, type components } from '@premise/api';

export type OverlayLayer = components['schemas']['OverlayLayerSummary'];

/** The Spatial module's overlays (ADR 50 §3): the org's own shapes, served as tiles. */
export const overlaysApi = {
  list: (deleted?: boolean, signal?: AbortSignal) =>
    api.get('/api/overlays', { query: { deleted }, signal }),
  /** The tile URL template the map draws a layer from (scope is the server's). */
  tiles: (id: string) => `${window.location.origin}/api/tiles/overlays/${id}/{z}/{x}/{y}`,
};
