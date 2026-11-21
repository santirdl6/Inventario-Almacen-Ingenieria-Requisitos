using Inventario.Api.Features.Locations;

namespace Inventario.Api.Features.Stock.Queries;

// Contrato de GET /stock (specs/004-consultar-existencias/contracts/stock-api.md § GET /stock).
// Las cantidades viajan como cadenas con tres decimales, igual que en HU-02.

public sealed record StockQueryResult(DateTimeOffset AsOf, StockArticleDto Article, StockTotalsDto Totals,
    IReadOnlyList<StockRowDto> Items, int Page, int PageSize, int TotalCount)
{
    public static StockQueryResult From(StockBuckets buckets) => new(buckets.AsOf,
        new StockArticleDto(buckets.Article.Id, buckets.Article.Sku, buckets.Article.Name, buckets.Article.BaseUnit,
            buckets.Article.IsActive, buckets.Article.TracksLot),
        new StockTotalsDto(Quantity(buckets.Totals.Physical), Quantity(buckets.Totals.Reserved), Quantity(buckets.Totals.Available)),
        buckets.Items.Select(item => new StockRowDto(
            new StockLocationDto(item.Location.Id, item.Location.Code, item.Location.Aisle, item.Location.Rack,
                item.Location.Level, item.Location.Kind.ToString(), item.Location.IsActive),
            new StockLotDto(item.Lot.Id, item.Lot.Code, item.Lot.IsUntracked),
            Quantity(item.Physical), Quantity(item.Reserved), Quantity(item.Available))).ToList(),
        buckets.Page, buckets.PageSize, buckets.TotalCount);

    private static string Quantity(decimal value) => LocationText.Quantity(value);
}

public sealed record StockArticleDto(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive, bool TracksLot);

public sealed record StockTotalsDto(string Physical, string Reserved, string Available);

public sealed record StockRowDto(StockLocationDto Location, StockLotDto Lot, string Physical, string Reserved, string Available);

public sealed record StockLocationDto(Guid Id, string Code, string Aisle, string Rack, string Level, string Kind, bool IsActive);

/// <summary>En artículos sin seguimiento, IsUntracked = true: la UI muestra «sin lote», nunca SIN-LOTE.</summary>
public sealed record StockLotDto(Guid Id, string Code, bool IsUntracked);
