using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Inventario.Api.Infrastructure.Persistence;

public sealed class ArticleUsageCoverageValidator(InventoryDbContext database,
    IEnumerable<IArticleUsageContributor> contributors)
{
    public void EnsureCovered() => Validate(database.Model, contributors);

    internal static HashSet<Type> RequiredTypes(IModel model) => model.GetEntityTypes().SelectMany(type => type.GetForeignKeys())
        .Where(key => key.PrincipalEntityType.ClrType == typeof(Article))
        .Select(key => key.DeclaringEntityType.ClrType)
        .Where(type => type.FullName != "Inventario.Api.Domain.Stock.StockPolicy").ToHashSet();

    internal static void Validate(IModel model, IEnumerable<IArticleUsageContributor> contributors)
    {
        var required = RequiredTypes(model);
        var declared = contributors.SelectMany(contributor => contributor.ReferencingEntityTypes).ToHashSet();
        if (!required.SetEquals(declared))
            throw new InventoryProblemException(503, "DependencyUnavailable",
                "Las comprobaciones de referencias del artículo no están completamente integradas.");
    }
}
