using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Inventario.Api.Infrastructure.Persistence;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

public sealed class StockCounterpartyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Additive_migration_preserves_legacy_journal_and_fingerprints_while_enforcing_new_rows()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var old = StockCore.Prepare(StockOperation.Receive, Context(null, null), [new(article.Id, location.Id, null, 2m)]);
        // Simula el contrato anterior: huella sin el campo nuevo. El stock se genera
        // con el núcleo real; al bajar la migración se obtiene el esquema histórico.
        var initial = new StockCommand(old.Operation, old.SourceType, old.SourceId, old.OccurredAt, old.Reason,
            old.SupportReference, old.SupportNotApplicableReason, null, "Fixture de migración", old.Lines, old.NormalizedBody);
        var key = Guid.NewGuid();
        var outcome = await stock.RunAsync(principal, key, initial);
        await using (var before = fixture.ApplicationDatabase())
            Assert.Equal("Fixture de migración", (await before.Set<StockMovement>().SingleAsync(value => value.Id == outcome.MovementId)).CounterpartyNotApplicableReason);
        using (var scope = fixture.BootstrapServices.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var migrator = admin.GetService<IMigrator>();
            try
            {
                await migrator.MigrateAsync(StockSchema.Migration);
                using var unavailable = await api.Client.GetAsync($"/api/v1/articles/{article.Id:D}/movements");
                Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            }
            finally { await migrator.MigrateAsync(); }
        }
        await using var db = fixture.ApplicationDatabase();
        var legacy = await db.Set<StockMovement>().SingleAsync(value => value.Id == outcome.MovementId);
        Assert.Null(legacy.CounterpartyReference); Assert.Null(legacy.CounterpartyNotApplicableReason);
        Assert.Equal(outcome, await stock.RunAsync(principal, key, old));
        var history = await api.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>($"/api/v1/articles/{article.Id:D}/movements");
        Assert.Null(Assert.Single(history!.Items).CounterpartyNotApplicableReason);
        Assert.Equal("2.000", history.Items[0].Quantity);
        var rejected = await Assert.ThrowsAsync<InventoryProblemException>(() => stock.RunAsync(principal, old));
        Assert.Equal(422, rejected.Status);
        var invalid = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.StockMovements
                (Id, Kind, SourceDocumentType, SourceDocumentId, Reason, ActorId, OccurredAt, RecordedAt,
                 OperationId, SupportReference, SupportNotApplicableReason, CounterpartyReference, CounterpartyNotApplicableReason)
            SELECT NEWID(), Kind, SourceDocumentType, {Guid.NewGuid()}, Reason, ActorId, OccurredAt, RecordedAt,
                   OperationId, SupportReference, SupportNotApplicableReason, NULL, NULL
            FROM dbo.StockMovements WHERE Id = {outcome.MovementId}
            """));
        Assert.Equal(547, invalid.Number);
        var denied = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.StockMovements SET CounterpartyNotApplicableReason = N'No permitido' WHERE Id = {outcome.MovementId}"));
        Assert.Equal(229, denied.Number);
        Assert.Equal(1, await db.Set<StockEntry>().CountAsync(value => value.ArticleId == article.Id));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    private static StockCommandContext Context(string? reference, string? reason)
    {
        var json = JsonSerializer.SerializeToNode(StockTestHarness.Context("receipt"), OperationResponse.JsonOptions)!.AsObject();
        json["counterpartyReference"] = reference;
        json["counterpartyNotApplicableReason"] = reason;
        return json.Deserialize<StockCommandContext>(OperationResponse.JsonOptions)!;
    }

    [Fact]
    public async Task A_new_movement_requires_counterparty_or_explicit_reason_without_partial_effects()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var command = StockCore.Prepare(StockOperation.Receive, Context("  ", "  "), [new(article.Id, location.Id, null, 2m)]);
        var key = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<InventoryProblemException>(() => stock.RunAsync(principal, key, command));
        Assert.Equal((422, "ValidationFailed"), (error.Status, error.Code));
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Set<StockEntry>().AnyAsync(value => value.ArticleId == article.Id));
        Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
        Assert.False((await db.Articles.SingleAsync(value => value.Id == article.Id)).IsDefinitionLocked);
    }

    [Theory]
    [InlineData("  PROV-01  ", null)]
    [InlineData(null, "  Operación interna sin contraparte externa  ")]
    public async Task Counterparty_evidence_is_persisted_replayed_and_visible_for_inactive_articles(string? reference, string? reason)
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var context = Context(reference, reason);
        StockCommand Command(StockCommandContext value) => StockCore.Prepare(StockOperation.Receive, value, [new(article.Id, location.Id, null, 2m)]);
        var key = Guid.NewGuid();
        var first = await stock.RunAsync(principal, key, Command(context));
        Assert.Equal(first, await stock.RunAsync(principal, key, Command(context)));
        var changed = JsonSerializer.SerializeToNode(context, OperationResponse.JsonOptions)!.AsObject();
        changed["counterpartyNotApplicableReason"] = "Otra justificación";
        var conflict = await Assert.ThrowsAsync<InventoryProblemException>(() =>
            stock.RunAsync(principal, key, Command(changed.Deserialize<StockCommandContext>(OperationResponse.JsonOptions)!)));
        Assert.Equal("IdempotencyConflict", conflict.Code);
        using var current = await api.Client.GetAsync($"/api/v1/articles/{article.Id:D}");
        using var inactive = await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{article.Id:D}",
            MasterDataArticleTests.Body(article.FamilyId, article.Sku, lot: false, active: false), version: current.Headers.ETag!.ToString());
        inactive.EnsureSuccessStatusCode();
        var history = await api.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>($"/api/v1/articles/{article.Id:D}/movements");
        var movement = Assert.Single(history!.Items);
        Assert.Equal(reference?.Trim(), movement.CounterpartyReference);
        Assert.Equal(reason?.Trim(), movement.CounterpartyNotApplicableReason);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(1, await db.Set<StockMovement>().CountAsync(value => value.Id == first.MovementId));
        Assert.Equal(1, await db.OperationResults.CountAsync(value => value.OperationId == key));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }
}
