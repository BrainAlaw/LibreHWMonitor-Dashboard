using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCMonitor.Telemetry;

/// <summary>
/// LibreHardwareMonitor-compatible tree node. The string fields keep the legacy shape;
/// the numeric fields (ValueNum/MinNum/MaxNum/Unit) let the UI skip string parsing.
/// </summary>
internal sealed class SensorNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
    public string? Min { get; set; }
    public string? Value { get; set; }
    public string? Max { get; set; }
    public double? MinNum { get; set; }
    public double? ValueNum { get; set; }
    public double? MaxNum { get; set; }
    public string? Unit { get; set; }
    public string? Type { get; set; }
    public string? SensorId { get; set; }
    public string ImageURL { get; set; } = string.Empty;
    public List<SensorNode> Children { get; set; } = [];
}

internal static class TelemetryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool LooksLikeLibreHardwarePayload(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Children", out JsonElement children) &&
                children.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }
}
