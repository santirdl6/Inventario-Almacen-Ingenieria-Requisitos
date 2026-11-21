using System.Security.Cryptography;

namespace Inventario.Api.Infrastructure.Http;

public static class EntityTags
{
    public static string Format(byte[] version) => $"\"{Convert.ToBase64String(version)}\"";

    public static void RequireMatch(string? header, byte[] currentVersion)
    {
        if (string.IsNullOrWhiteSpace(header))
            throw new InventoryProblemException(428, "PreconditionRequired", "Consulta el registro antes de guardar su versión.");
        byte[] supplied;
        try
        {
            if (header.Length < 3 || header[0] != '"' || header[^1] != '"')
                throw new FormatException();
            supplied = Convert.FromBase64String(header[1..^1]);
            if (supplied.Length != currentVersion.Length || Format(supplied) != header)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InventoryProblemException(400, "InvalidRequest", "If-Match debe contener una única versión fuerte válida.");
        }
        if (!CryptographicOperations.FixedTimeEquals(supplied, currentVersion))
            throw new InventoryProblemException(412, "VersionConflict", "Otra persona cambió el registro. Consulta la versión actual y revisa tus cambios.");
    }
}
