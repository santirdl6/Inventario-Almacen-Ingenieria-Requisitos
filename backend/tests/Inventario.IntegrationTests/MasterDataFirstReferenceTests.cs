using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataFirstReferenceTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public void Current_h1_has_no_document_consumers_and_the_gate_is_not_a_permissive_stub()
    {
        using var factory = fixture.Factory(); using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(ArticleUsageCoverageValidator.RequiredTypes(db.Model));
    }

    [Fact]
    public void The_domain_marker_never_unlocks_and_same_definition_can_change_descriptive_fields()
    {
        var article = Article.Create(Guid.NewGuid(), "FIRST-01", "Original", "UD", false, true);
        article.LockDefinition(); article.LockDefinition();
        article.Change("FIRST-02", "Cambio descriptivo", article.FamilyId, "UD", false, true, false);
        Assert.True(article.IsDefinitionLocked);
        Assert.Throws<InvalidOperationException>(() => article.Change(article.Sku, article.Name, article.FamilyId, "KG", true, false, false));
    }

    [Fact]
    public void A_consumer_with_a_reader_but_without_first_reference_link_is_not_ready()
    {
        // Control de metadatos de integración; no sustituye las pruebas del futuro escritor real.
        var builder = new ModelBuilder();
        builder.Entity<Article>().HasKey(value => value.Id);
        builder.Entity<Consumer>().HasKey(value => value.Id);
        builder.Entity<Consumer>().HasOne<Article>().WithMany().HasForeignKey(value => value.ArticleId);
        var model = builder.FinalizeModel();
        ArticleUsageCoverageValidator.Validate(model, [new MetadataReader()]);
        var error = Assert.Throws<InventoryProblemException>(() => ArticleDefinitionReadiness.Validate(model, []));
        Assert.Equal(503, error.Status); Assert.Equal("DependencyUnavailable", error.Code);
    }

    private sealed class Consumer { public Guid Id { get; set; } public Guid ArticleId { get; set; } }
    private sealed class MetadataReader : IArticleUsageContributor
    {
        public IReadOnlyCollection<Type> ReferencingEntityTypes => [typeof(Consumer)];
        public Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Solo valida metadatos, no simula lectura de referencias.");
    }
}
