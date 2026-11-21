using Inventario.Api.Features.Locations;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Stock.Queries;

/// <summary>
/// GET /stock (HU-05 T006, US1). Valida los parámetros como dice el contrato y delega en el
/// agregado único <see cref="StockBalanceReader"/>. Los identificadores llegan como texto para
/// responder 422 con el campo, en lugar del 400 genérico del enlace de parámetros.
/// </summary>
public sealed class StockQueries(StockBalanceReader reader)
{
    public async Task<IResult> StockAsync(string? articleId, string? locationId, string? lotId, string? includeEmpty,
        int? page, int? pageSize, CancellationToken cancellation)
    {
        if (!Guid.TryParse(articleId, out var article) || article == Guid.Empty)
            throw InventoryProblemException.Validation("articleId", "Elige un artículo para ver sus existencias.");
        var location = OptionalId(locationId, "locationId", "La ubicación indicada no es válida.");
        var lot = OptionalId(lotId, "lotId", "El lote indicado no es válido.");
        var empty = false;
        if (!string.IsNullOrEmpty(includeEmpty) && !bool.TryParse(includeEmpty, out empty))
            throw InventoryProblemException.Validation("includeEmpty", "Indica true o false.");
        var paging = Paging.From(page, pageSize);

        var buckets = await reader.ReadAsync(article, location, lot, empty, paging, cancellation);
        return buckets is null ? ApiProblems.Status(404) : Results.Ok(StockQueryResult.From(buckets));
    }

    private static Guid? OptionalId(string? value, string field, string message)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return Guid.TryParse(value, out var id) ? id : throw InventoryProblemException.Validation(field, message);
    }
}
