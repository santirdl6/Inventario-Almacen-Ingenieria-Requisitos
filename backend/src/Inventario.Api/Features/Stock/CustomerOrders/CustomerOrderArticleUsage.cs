using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.CustomerOrders;

// HU-05 (research D-13, INT-HU05-01): aporta a los controles de Article de A las dos entidades de
// HU-05 con FK a Article. Una línea de pedido o una asignación de reserva es una referencia real:
// bloquea el borrado del artículo (HistoryPreventsDeletion, motivo «References») y su definición.
// No es historial por sí misma; las reservas ya generan historial a través del diario de stock.
// La marca IsDefinitionLocked la ponen los comandos con ArticleReferenceTracker bajo el bloqueo del
// artículo (IArticleFirstReferenceContributor declara los tipos cubiertos).
internal sealed class CustomerOrderArticleUsageContributor : IArticleUsageContributor, IArticleFirstReferenceContributor
{
    private bool? schemaApplied;

    public IReadOnlyCollection<Type> ReferencingEntityTypes { get; } = [typeof(CustomerOrderLine), typeof(ReservationAllocation)];

    public async Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken)
    {
        // Igual que StockArticleUsageContributor: «sin uso» solo si la migración de HU-05 no está
        // aplicada (la cadena va por detrás del código); nunca por un error de consulta.
        schemaApplied ??= (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(CustomerOrderSchema.Migration);
        if (schemaApplied != true)
            return new ArticleUsage(false, false);

        var references = await database.Set<CustomerOrderLine>().AnyAsync(line => line.ArticleId == articleId, cancellationToken)
            || await database.Set<ReservationAllocation>().AnyAsync(allocation => allocation.ArticleId == articleId, cancellationToken);
        return new ArticleUsage(references, false);
    }
}

public static class CustomerOrderSchema
{
    // Migración que crea CustomerOrders, CustomerOrderLines, Reservations y ReservationAllocations.
    public const string Migration = "20261120111847_HU05_CustomerOrdersReservations";
}
