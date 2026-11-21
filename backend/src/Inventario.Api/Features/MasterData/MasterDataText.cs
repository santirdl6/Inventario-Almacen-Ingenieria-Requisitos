using System.ComponentModel.DataAnnotations;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.MasterData;

public static class MasterDataText
{
    public static string Code(string? value, string field = "code") => Required(value, 64, field).ToUpperInvariant();
    public static string Name(string? value) => Required(value, 200, "name");
    public static string BaseUnit(string? value) => Required(value, 32, "baseUnit").ToUpperInvariant();

    public static string Required(string? value, int maximum, string field)
    {
        var normalized = Optional(value, maximum, field);
        return normalized ?? throw InventoryProblemException.Validation(field, "Este campo es obligatorio.");
    }

    public static string? Optional(string? value, int maximum, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum)
            throw InventoryProblemException.Validation(field, $"El campo admite hasta {maximum} caracteres.");
        return normalized;
    }

    public static string? Email(string? value)
    {
        var normalized = Optional(value, 256, "email");
        if (normalized is not null && !new EmailAddressAttribute().IsValid(normalized))
            throw InventoryProblemException.Validation("email", "Introduce un correo válido o deja el campo vacío.");
        return normalized;
    }
}
