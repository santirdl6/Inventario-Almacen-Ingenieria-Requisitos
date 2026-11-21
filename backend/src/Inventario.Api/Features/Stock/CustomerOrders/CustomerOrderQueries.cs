using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.CustomerOrders;

/// <summary>
/// Lecturas del pedido (HU-05 T008; research D-11). El detalle lleva solo datos propios y su ETag;
/// el contexto, los datos actuales de artículos, ubicaciones y lotes sin ETag. Cada una se lee en
/// una transacción SNAPSHOT, así que solo incluye hechos confirmados y un único instante.
/// </summary>
public sealed class CustomerOrderQueries(InventoryDbContext database)
{
    private static readonly string CreationResource = RequestFingerprint.CanonicalResource(CustomerOrderCommands.Resource);

    /// <summary>GET /customer-orders/{id}: null si no existe.</summary>
    public Task<(CustomerOrderView View, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var loaded = await LoadAsync(database, id, cancellation);
            await transaction.CommitAsync(cancellation);
            return loaded is null ? ((CustomerOrderView, byte[])?)null
                : (CustomerOrderView.From(loaded.Value.Order, loaded.Value.Reservations), loaded.Value.Order.Version);
        });

    /// <summary>GET /customer-orders/{id}/context: null si el pedido no existe.</summary>
    public Task<CustomerOrderContext?> ContextAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var loaded = await LoadAsync(database, id, cancellation);
            if (loaded is null) return (CustomerOrderContext?)null;
            var (order, reservations) = loaded.Value;
            var asOf = DateTimeOffset.UtcNow;
            var allocations = reservations.SelectMany(reservation => reservation.Allocations).ToList();
            var articleIds = order.Lines.Select(line => line.ArticleId).Distinct().ToList();
            var locationIds = allocations.Select(allocation => allocation.LocationId).Distinct().ToList();
            var lotIds = allocations.Select(allocation => allocation.LotId).Distinct().ToList();

            var articles = await database.Set<Article>().AsNoTracking().Where(article => articleIds.Contains(article.Id))
                .OrderBy(article => article.Sku)
                .Select(article => new CustomerOrderContextArticle(article.Id, article.Sku, article.Name, article.BaseUnit,
                    article.IsActive, article.TracksLot, article.AllowsFractional))
                .ToListAsync(cancellation);
            var locations = await database.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
                .OrderBy(location => location.Code)
                .Select(location => new CustomerOrderContextLocation(location.Id, location.Code, location.Aisle, location.Rack,
                    location.Level, location.IsActive))
                .ToListAsync(cancellation);
            var lots = await database.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id))
                .OrderBy(lot => lot.Code)
                .Select(lot => new CustomerOrderContextLot(lot.Id, lot.ArticleId, lot.Code, lot.IsUntracked))
                .ToListAsync(cancellation);
            // El alta guarda su OperationResult con SourceDocumentId = pedido (Convergence C3).
            var creation = await database.OperationResults.AsNoTracking()
                .Where(result => result.SourceDocumentId == id && result.Method == "POST" && result.Resource == CreationResource)
                .Select(result => (Guid?)result.OperationId)
                .SingleOrDefaultAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            return new CustomerOrderContext(EntityTags.Format(order.Version), asOf, articles, locations, lots, creation);
        });

    /// <summary>
    /// HU-03 (D-11, FR-001a): búsqueda exacta por referencia externa con la regla de unicidad del alta
    /// (recorte y mayúsculas invariantes sobre la columna con intercalación CI). 422 si viene vacía o
    /// supera la longitud; null si no hay ningún pedido.
    /// </summary>
    public async Task<CustomerOrderLookup?> LookupAsync(string? externalReference, CancellationToken cancellation)
    {
        var text = externalReference?.Trim() ?? "";
        if (text.Length == 0)
            throw InventoryProblemException.Validation("externalReference", "Escribe la referencia del pedido.");
        if (text.Length > CustomerOrder.ExternalReferenceMaxLength)
            throw InventoryProblemException.Validation("externalReference",
                $"La referencia no puede superar {CustomerOrder.ExternalReferenceMaxLength} caracteres.");
        var normalized = StockDocuments.NormalizeReference(text);
        var found = await database.Set<CustomerOrder>().AsNoTracking()
            .Where(order => order.ExternalReference == normalized)
            .Select(order => new { order.Id, order.ExternalReference, order.State })
            .SingleOrDefaultAsync(cancellation);
        return found is null ? null : new CustomerOrderLookup(found.Id, found.ExternalReference, found.State.ToString());
    }

    /// <summary>Pedido con sus líneas, reservas y asignaciones, sin seguimiento.</summary>
    internal static async Task<(CustomerOrder Order, IReadOnlyList<Reservation> Reservations)?> LoadAsync(
        InventoryDbContext database, Guid id, CancellationToken cancellation)
    {
        var order = await database.Set<CustomerOrder>().AsNoTracking().Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == id, cancellation);
        if (order is null) return null;
        var lineIds = order.Lines.Select(line => line.Id).ToList();
        var reservations = await database.Set<Reservation>().AsNoTracking().Include(reservation => reservation.Allocations)
            .Where(reservation => lineIds.Contains(reservation.CustomerOrderLineId))
            .ToListAsync(cancellation);
        return (order, reservations);
    }
}
