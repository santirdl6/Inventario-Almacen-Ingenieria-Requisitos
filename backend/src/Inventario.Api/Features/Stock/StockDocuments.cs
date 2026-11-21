using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Inventario.Api.Features.Stock;

/// <summary>
/// Tipos canónicos de documento de HU-05 para InventoryLocks y StockCore (research D-04). Orden
/// ordinal de sus recursos: customer-order-reference &lt; customer-order &lt; reservation-release &lt;
/// reservation, porque «-» precede a «:».
/// </summary>
public static class StockDocuments
{
    public const string CustomerOrderReference = "customer-order-reference";
    public const string CustomerOrder = "customer-order";
    public const string ReservationRelease = "reservation-release";
    public const string Reservation = "reservation";

    // Espacio de nombres fijo de HU-05 para el UUID v5 de la referencia externa. No debe cambiar:
    // dos altas con la misma referencia normalizada deben pedir el mismo bloqueo.
    private static readonly Guid ReferenceNamespace = new("5d0a1f4e-7c2b-4e8a-9b61-0c3e5a7d9f12");

    /// <summary>Referencia externa normalizada: recorte y mayúsculas invariantes (comparación CI).</summary>
    public static string NormalizeReference(string reference) => reference.Trim().ToUpperInvariant();

    /// <summary>UUID v5 (RFC 9562, SHA-1) determinista de la referencia normalizada.</summary>
    public static Guid ReferenceLockId(string externalReference)
    {
        Span<byte> name = stackalloc byte[16];
        WriteBigEndian(ReferenceNamespace, name);
        var text = Encoding.UTF8.GetBytes(NormalizeReference(externalReference));
        var input = new byte[16 + text.Length];
        name.CopyTo(input);
        text.CopyTo(input, 16);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);
        Span<byte> uuid = hash[..16];
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50); // versión 5
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80); // variante RFC
        return new Guid(uuid, bigEndian: true);
    }

    private static void WriteBigEndian(Guid value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out _))
            throw new InvalidOperationException("No se pudo serializar el espacio de nombres.");
    }
}
