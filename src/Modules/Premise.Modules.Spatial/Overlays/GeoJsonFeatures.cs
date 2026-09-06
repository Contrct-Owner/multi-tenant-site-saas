using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace Premise.Modules.Spatial.Overlays;

/// <summary>
/// GeoJSON in, NetTopologySuite out (ADR 50: spatial types in application
/// code are NTS types; nobody hand-writes WKT). One reader, one set of
/// rules: a FeatureCollection or a single Feature or a bare geometry is
/// accepted, every geometry must be valid and in WGS84, and the first
/// version takes polygons only - the ADR's scope for overlay editing.
/// </summary>
public static class GeoJsonFeatures
{
    public const int MaxFeatures = 5000;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new GeoJsonConverterFactory(new GeometryFactory(new PrecisionModel(), 4326)),
        },
    };

    public sealed record Parsed(Geometry Geometry, string? Properties);

    /// <summary>Parses the upload; the error, when there is one, is the reason a 400 carries.</summary>
    public static (IReadOnlyList<Parsed> Features, string? Error) Parse(JsonElement geoJson)
    {
        if (geoJson.ValueKind != JsonValueKind.Object)
            return ([], "a GeoJSON object is required");
        var type = geoJson.TryGetProperty("type", out var t) ? t.GetString() : null;
        try
        {
            var raw = geoJson.GetRawText();
            IEnumerable<IFeature> features = type switch
            {
                "FeatureCollection" => JsonSerializer.Deserialize<FeatureCollection>(raw, Options)
                    ?? [],
                "Feature" => [JsonSerializer.Deserialize<Feature>(raw, Options)!],
                null => [],
                _ => [new Feature(JsonSerializer.Deserialize<Geometry>(raw, Options), null)],
            };
            var parsed = new List<Parsed>();
            foreach (var feature in features)
            {
                if (parsed.Count >= MaxFeatures)
                    return ([], $"at most {MaxFeatures} features per layer");
                var geometry = feature.Geometry;
                if (geometry is null || geometry.IsEmpty)
                    return ([], "every feature needs a geometry");
                if (geometry is not (Polygon or MultiPolygon))
                    return ([], "overlays take polygons (Polygon or MultiPolygon) only");
                if (!geometry.IsValid)
                    return ([], "a polygon is not valid (self-intersecting or unclosed)");
                if (!InWgs84(geometry))
                    return ([], "coordinates must be longitude/latitude in WGS84");
                geometry.SRID = 4326;
                parsed.Add(new Parsed(geometry, PropertiesJson(feature.Attributes)));
            }
            return parsed.Count == 0 ? ([], "no features found") : (parsed, null);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
        {
            return ([], "malformed GeoJSON: " + e.Message);
        }
    }

    private static bool InWgs84(Geometry geometry)
    {
        var e = geometry.EnvelopeInternal;
        return e.MinX >= -180 && e.MaxX <= 180 && e.MinY >= -90 && e.MaxY <= 90;
    }

    private static string? PropertiesJson(IAttributesTable? attributes)
    {
        if (attributes is null || attributes.Count == 0)
            return null;
        return JsonSerializer.Serialize(attributes, Options);
    }
}
