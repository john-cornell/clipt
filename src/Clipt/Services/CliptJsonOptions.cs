using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipt.Services;

internal static class CliptJsonOptions
{
    internal static readonly JsonSerializerOptions Shared = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
