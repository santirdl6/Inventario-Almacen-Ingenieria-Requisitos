using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Features.MasterData.Commands;

public interface IArticleStateEffects
{
    Task ApplyAsync(OperationContext context, Article article, CancellationToken cancellation);
}

public sealed class ArticleLifecycleCoordinator
{
    public async Task ApplyAsync(OperationContext context, Article article, CancellationToken cancellation)
    {
        var effects = context.Services.GetServices<IArticleStateEffects>().ToArray();
        var alertsPresent = context.Database.Model.GetEntityTypes().Any(type => type.ClrType.Name == "StockAlert");
        if (alertsPresent && effects.Length == 0)
            throw new InventoryProblemException(503, "DependencyUnavailable", "Falta el evaluador de alertas integrado para cambiar el estado.");
        foreach (var effect in effects) await effect.ApplyAsync(context, article, cancellation);
        // H1 sin StockAlert no calcula disponibilidad ni simula evaluación de otra feature.
    }
}
