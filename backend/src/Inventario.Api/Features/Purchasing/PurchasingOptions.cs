using System.Globalization;

namespace Inventario.Api.Features.Purchasing;

public sealed class PurchasingOptions
{
    public string Currency { get; set; } = "EUR";

    internal static bool IsValid(PurchasingOptions options) => options.Currency.Length == 3
        && options.Currency.All(value => value is >= 'A' and <= 'Z')
        && CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name).ISOCurrencySymbol)
            .Contains(options.Currency, StringComparer.Ordinal);
}
