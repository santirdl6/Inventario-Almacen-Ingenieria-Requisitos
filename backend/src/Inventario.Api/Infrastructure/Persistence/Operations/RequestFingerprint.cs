using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Infrastructure.Persistence.Operations;

public static class RequestFingerprint
{
    public static string CanonicalResource(string resource)
    {
        if (resource.Length > 512 || !resource.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase)
            || resource.Contains('?') || resource.Contains('#') || resource.Contains("//") || resource.Contains(".."))
            throw new InventoryProblemException(400, "InvalidRequest", "La ruta de la operación no es válida.");
        return resource.TrimEnd('/').ToLowerInvariant();
    }

    public static string Create(string method, string resource, JsonElement normalizedBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, normalizedBody);
        var prefix = Encoding.UTF8.GetBytes(method.ToUpperInvariant() + "\n" + CanonicalResource(resource) + "\n");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix);
        hash.AppendData(stream.ToArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else if (element.ValueKind == JsonValueKind.Undefined)
            writer.WriteNullValue();
        else element.WriteTo(writer);
    }
}
