using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataUsageTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task The_real_h0_model_has_no_document_consumers_and_does_not_use_a_fake_reader()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IArticleUsageReader>();
        Assert.IsType<ArticleUsageReader>(reader);
        Assert.Equal(new ArticleUsage(false, false), await reader.ReadAsync(Guid.NewGuid()));
    }

    [Fact]
    public void An_uncovered_foreign_key_is_not_interpreted_as_no_references()
    {
        // Prueba de metadatos, no evidencia de concurrencia ni de integración de stock.
        var builder = new ModelBuilder();
        builder.Entity<Article>().HasKey(value => value.Id);
        builder.Entity<Consumer>().HasKey(value => value.Id);
        builder.Entity<Consumer>().HasOne<Article>().WithMany().HasForeignKey(value => value.ArticleId);
        var model = builder.FinalizeModel();
        var error = Assert.Throws<InventoryProblemException>(() => ArticleUsageCoverageValidator.Validate(model, []));
        Assert.Equal("DependencyUnavailable", error.Code);
    }

    private sealed class Consumer
    {
        public Guid Id { get; set; }
        public Guid ArticleId { get; set; }
    }
}
