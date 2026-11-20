using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Core;

// Aportación de stock a los controles de A sobre Article (ArticleUsageReader y
// ArticleDefinitionReadiness): declara todas las entidades de stock con FK a Article y
// consulta su uso real en el mismo contexto/snapshot que recibe.
//
// - Lots, StockBalances o StockEntries de un artículo ⇒ HasHistory (y por tanto referencias):
//   un saldo o lote solo nace con una entrada confirmada del diario y nunca se borra.
// - StockMovement no tiene FK a Article; su relación con el artículo está en StockEntry.
// - La primera referencia la marca StockCore con ArticleReferenceTracker bajo el bloqueo
//   del artículo (IArticleFirstReferenceContributor).
internal sealed class StockArticleUsageContributor : IArticleUsageContributor, IArticleFirstReferenceContributor
{
    private bool? schemaApplied;

    public IReadOnlyCollection<Type> ReferencingEntityTypes { get; } = [typeof(Lot), typeof(StockBalance), typeof(StockEntry)];

    public async Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken)
    {
        // Durante la preparación HU-08 (esquema Expand de A) la cadena está por detrás del código
        // y las tablas de stock aún no existen: sin tablas no puede haber referencias de stock.
        // No se trata como «vacío» un error de consulta; solo la ausencia comprobada de la migración.
        schemaApplied ??= (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(StockSchema.Migration);
        if (schemaApplied != true)
            return new ArticleUsage(false, false);

        var history = await database.Set<StockEntry>().AnyAsync(entry => entry.ArticleId == articleId, cancellationToken)
            || await database.Set<StockBalance>().AnyAsync(balance => balance.ArticleId == articleId, cancellationToken)
            || await database.Set<Lot>().AnyAsync(lot => lot.ArticleId == articleId, cancellationToken);
        return new ArticleUsage(history, history);
    }
}

public static class StockSchema
{
    // Migración que crea Locations, Lots, StockBalances, StockMovements y StockEntries.
    public const string Migration = "20261118194940_H0_StockCore";
    public const string CounterpartyMigration = "20261119171332_HU08MovementCounterparty";
}

public static class StockCoreRegistration
{
    public static IServiceCollection AddStockCore(this IServiceCollection services)
    {
        services.AddScoped<StockCore>();
        services.AddScoped<StockArticleUsageContributor>();
        services.AddScoped<IArticleUsageContributor>(provider => provider.GetRequiredService<StockArticleUsageContributor>());
        services.AddScoped<IArticleFirstReferenceContributor>(provider => provider.GetRequiredService<StockArticleUsageContributor>());
        // HU-05 (T004): líneas de pedido y asignaciones de reserva también referencian Article. Se
        // registra aquí, con el modelo que las incluye, para que la cobertura no dependa de AddStock().
        services.AddScoped<CustomerOrders.CustomerOrderArticleUsageContributor>();
        services.AddScoped<IArticleUsageContributor>(provider => provider.GetRequiredService<CustomerOrders.CustomerOrderArticleUsageContributor>());
        services.AddScoped<IArticleFirstReferenceContributor>(provider => provider.GetRequiredService<CustomerOrders.CustomerOrderArticleUsageContributor>());
        services.AddScoped<IArticleMovementReader, StockArticleMovementReader>();
        return services;
    }
}
