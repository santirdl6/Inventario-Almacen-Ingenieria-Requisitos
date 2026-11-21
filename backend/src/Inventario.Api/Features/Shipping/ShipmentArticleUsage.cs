using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Shipping;

// HU-03 (research D-09, INT-HU03-01): aporta a los controles de Article de A la única entidad de
// HU-03 con FK a Article. Una línea de salida, también en borrador, es una referencia real: bloquea
// el borrado del artículo y su definición. No es historial por sí misma; la salida confirmada genera
// historial en el diario de stock. La marca IsDefinitionLocked la ponen los comandos con
// ArticleReferenceTracker bajo el bloqueo del artículo. Se registra en AddStockCore (lección 4).
internal sealed class ShipmentArticleUsageContributor : IArticleUsageContributor, IArticleFirstReferenceContributor
{
    private bool? schemaApplied;

    public IReadOnlyCollection<Type> ReferencingEntityTypes { get; } = [typeof(ShipmentLine)];

    public async Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken)
    {
        // Como los de HU-05: «sin uso» solo si la migración de HU-03 no está aplicada.
        schemaApplied ??= (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(ShipmentSchema.Migration);
        if (schemaApplied != true)
            return new ArticleUsage(false, false);

        return new ArticleUsage(
            await database.Set<ShipmentLine>().AnyAsync(line => line.ArticleId == articleId, cancellationToken), false);
    }
}

public static class ShipmentSchema
{
    // Migración que crea Shipments, ShipmentLines y ShipmentReservationConsumptions (research D-10).
    public const string Migration = "20261121114907_HU03_Shipments";
}
