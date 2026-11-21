using Inventario.Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Inventario.Api.Infrastructure.Persistence;

// Declaración de integración respaldada por las pruebas del escritor de cada feature.
public interface IArticleFirstReferenceContributor { IReadOnlyCollection<Type> ReferencingEntityTypes { get; } }

public sealed class ArticleDefinitionReadiness(InventoryDbContext database, ArticleUsageCoverageValidator coverage,
    IEnumerable<IArticleFirstReferenceContributor> links)
{
    public void EnsureReady()
    {
        coverage.EnsureCovered();
        Validate(database.Model, links);
    }

    internal static void Validate(IModel model, IEnumerable<IArticleFirstReferenceContributor> links)
    {
        var required = ArticleUsageCoverageValidator.RequiredTypes(model);
        var linked = links.SelectMany(link => link.ReferencingEntityTypes).ToHashSet();
        if (!required.SetEquals(linked))
            throw new InventoryProblemException(503, "DependencyUnavailable", "Falta integrar el seguimiento de primera referencia de un consumidor.");
    }
}
