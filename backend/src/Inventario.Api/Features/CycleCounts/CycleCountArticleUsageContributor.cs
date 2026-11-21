using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.CycleCounts;

// HU-04 (research D-08, INT-HU04-01): aporta a los controles de Article de A la única entidad de HU-04
// con FK a Article. Una línea de conteo, también abierto, es una referencia real: bloquea el borrado
// del artículo y su definición. Es historial cuando su conteo está cerrado (evidencia conservada).
// La marca IsDefinitionLocked la ponen los comandos con ArticleReferenceTracker bajo el bloqueo del
// artículo; retirar un hallazgo retira su referencia. Se registra en AddStockCore (lección 4).
internal sealed class CycleCountArticleUsageContributor : IArticleUsageContributor, IArticleFirstReferenceContributor
{
    private bool? schemaApplied;

    public IReadOnlyCollection<Type> ReferencingEntityTypes { get; } = [typeof(CountLine)];

    public async Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken)
    {
        // Como los de HU-05 y HU-03: «sin uso» solo si la migración de HU-04 no está aplicada.
        schemaApplied ??= (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(CycleCountSchema.Migration);
        if (schemaApplied != true)
            return new ArticleUsage(false, false);

        var lines = database.Set<CountLine>().Where(line => line.ArticleId == articleId);
        if (!await lines.AnyAsync(cancellationToken))
            return new ArticleUsage(false, false);
        var history = await lines.Join(database.Set<CycleCount>(), line => line.CountId, count => count.Id, (_, count) => count.State)
            .AnyAsync(state => state == CycleCountState.Confirmed || state == CycleCountState.Cancelled, cancellationToken);
        return new ArticleUsage(true, history);
    }
}

public static class CycleCountSchema
{
    // Migración que crea CycleCounts, CountLocations y CountLines (research D-14).
    public const string Migration = "20261121152857_HU04_CycleCounts";
}
