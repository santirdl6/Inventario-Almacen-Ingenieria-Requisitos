using System.Security.Cryptography;
using System.Text.Json;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Features.Locations;

// El detalle incluye capacidades derivadas: su revisión fuerte cubre la representación
// completa además de la rowversion propia, sin modificar Location por un movimiento.
internal static class LocationEntityTags
{
    public static byte[] Token(LocationDetail detail, byte[] version) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { detail, version }, OperationResponse.JsonOptions));
}
