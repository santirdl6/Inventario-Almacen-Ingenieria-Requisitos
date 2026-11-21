using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// Integración del núcleo de stock con los controles de Article de A (HU-08): cobertura de
// referencias, primera referencia (IsDefinitionLocked) y borrado con historial.
public sealed class StockArticleIntegrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static async Task<(Guid Id, string ETag)> CreateArticleAsync(MasterDataApi api, string prefix, bool lot = false)
    {
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles",
            MasterDataArticleTests.Body(family, prefix + "-" + Guid.NewGuid().ToString("N"), lot: lot));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        return (id, created.Headers.ETag!.ToString());
    }

    private async Task<Guid> LocationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var location = new Inventario.Api.Domain.Locations.Location("L-" + suffix, "A" + suffix[..8], "E01", "N1",
            Inventario.Api.Domain.Locations.LocationKind.Storage);
        await using var database = fixture.ApplicationDatabase();
        database.Add(location);
        await database.SaveChangesAsync();
        return location.Id;
    }

    private static StockCommand Receive(Guid article, Guid location, decimal quantity) =>
        StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article, location, null, quantity)]);

    [Fact]
    public void Stock_and_purchasing_contributors_cover_every_article_foreign_key()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(new HashSet<Type> { typeof(Lot), typeof(StockBalance), typeof(StockEntry), typeof(PurchaseOrderLine) },
            ArticleUsageCoverageValidator.RequiredTypes(database.Model));
        Assert.Equal(new HashSet<Type> { typeof(Lot), typeof(StockBalance), typeof(StockEntry) },
            scope.ServiceProvider.GetServices<IArticleUsageContributor>().OfType<StockArticleUsageContributor>()
                .Single().ReferencingEntityTypes.ToHashSet());
        // Ni la cobertura de uso ni la de primera referencia quedan como 503 DependencyUnavailable.
        scope.ServiceProvider.GetRequiredService<ArticleUsageCoverageValidator>().EnsureCovered();
        scope.ServiceProvider.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
    }

    [Fact]
    public async Task An_article_with_stock_cannot_be_deleted_and_reports_history()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (id, _) = await CreateArticleAsync(api, "STOCKED");
        var location = await LocationAsync();
        await stock.RunAsync(principal, Receive(id, location, 2m));

        using (var scope = factory.Services.CreateScope())
            Assert.Equal(new ArticleUsage(true, true),
                await scope.ServiceProvider.GetRequiredService<IArticleUsageReader>().ReadAsync(id));

        var path = $"/api/v1/articles/{id:D}";
        var current = await api.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var context = (await api.Client.GetFromJsonAsync<JsonElement>(path + "/context")).GetProperty("capabilities");
        Assert.False(context.GetProperty("canDelete").GetBoolean());
        Assert.Equal("History", context.GetProperty("deleteBlockReason").GetString());
        Assert.False(context.GetProperty("canEditDefinition").GetBoolean());
        var deleted = await api.WriteAsync(HttpMethod.Delete, path, new { }, version: current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("HistoryPreventsDeletion", (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using var check = fixture.ApplicationDatabase();
        Assert.True(await check.Articles.AnyAsync(value => value.Id == id));
        Assert.Equal(2m, await check.Set<StockBalance>().Where(value => value.ArticleId == id).SumAsync(value => value.Physical));
    }

    [Fact]
    public async Task Deleting_an_unreferenced_article_still_works_with_balances_protected_from_delete()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();

        // Otro artículo con saldo: la tabla tiene filas y el DENY DELETE está en vigor.
        var (stocked, _) = await CreateArticleAsync(api, "NEIGHBOUR");
        await stock.RunAsync(principal, Receive(stocked, await LocationAsync(), 1m));
        await using (var database = fixture.ApplicationDatabase())
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM StockBalances WHERE ArticleId = {stocked}"));
            Assert.Equal(229, denied.Number);
        }

        var (id, etag) = await CreateArticleAsync(api, "UNUSED");
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(new ArticleUsage(false, false),
                await scope.ServiceProvider.GetRequiredService<IArticleUsageReader>().ReadAsync(id));
        var path = $"/api/v1/articles/{id:D}";
        var deleted = await api.WriteAsync(HttpMethod.Delete, path, new { }, version: etag);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task The_first_movement_locks_the_article_definition_under_the_same_lock_and_transaction()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (id, _) = await CreateArticleAsync(api, "FIRSTREF");
        var path = $"/api/v1/articles/{id:D}";
        var initial = await api.Client.GetAsync(path);
        var before = await initial.Content.ReadFromJsonAsync<JsonElement>();
        var staleETag = initial.Headers.ETag!.ToString();
        Assert.False(before.GetProperty("isDefinitionLocked").GetBoolean());

        await stock.RunAsync(principal, Receive(id, await LocationAsync(), 1m));

        var current = await api.Client.GetAsync(path);
        var after = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(after.GetProperty("isDefinitionLocked").GetBoolean());
        var currentETag = current.Headers.ETag!.ToString();
        Assert.NotEqual(staleETag, currentETag);
        var family = after.GetProperty("familyId").GetGuid();
        var body = MasterDataArticleTests.Body(family, after.GetProperty("sku").GetString()!, unit: "KG", lot: false);

        // La primera referencia cambió la versión: el ETag leído antes de recibir está obsoleto.
        var stale = await api.WriteAsync(HttpMethod.Put, path, body, Guid.NewGuid(), staleETag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("VersionConflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Con el ETag vigente, el cambio de definición se rechaza por la primera referencia.
        var changed = await api.WriteAsync(HttpMethod.Put, path, body, Guid.NewGuid(), currentETag);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("ArticleDefinitionLocked", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task History_for_an_inactive_article_filters_and_pages_the_committed_journal()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        using var reader = await MasterDataApi.LoginAsync(fixture, factory, "Operario");
        using var administrator = await MasterDataApi.LoginAsync(fixture, factory, "Administrador");
        var stock = new StockTestHarness(fixture, factory.Services);
        var (actorId, principal) = await stock.ActorAsync();
        var (article, firstLocation) = await stock.SeedAsync(allowsFractional: true, tracksLot: true);
        var secondLocation = await LocationAsync();
        var path = $"/api/v1/articles/{article.Id:D}";
        var url = path + "/movements";

        var receipt = await stock.RunAsync(principal, StockCore.Prepare(StockOperation.Receive,
            StockTestHarness.Context("receipt") with { SupportReference = "ALB-123", CounterpartyReference = "Proveedor" },
            [new(article.Id, firstLocation.Id, "lote-1", 2.125m), new(article.Id, secondLocation, "lote-2", 3m)]));
        var shipment = await stock.RunAsync(principal, StockCore.PrepareShipment(StockTestHarness.Context("shipment"),
            [new(article.Id, firstLocation.Id, "lote-1", 1m, 0m)]));
        var current = await api.Client.GetAsync(path);
        var disable = MasterDataArticleTests.Body(article.FamilyId, article.Sku,
            unit: article.BaseUnit, fractions: true, lot: true, active: false);
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path, disable,
            version: current.Headers.ETag!.ToString())).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.Client.GetAsync(url)).StatusCode);
        var all = (await reader.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>(url))!;
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(shipment.MovementId, all.Items[0].MovementId);
        Assert.Equal(firstLocation.Id, all.Items[0].Origin!.Id);
        Assert.Null(all.Items[0].Destination);

        var start = Uri.EscapeDataString(receipt.RecordedAt.ToString("O"));
        var end = Uri.EscapeDataString(shipment.RecordedAt.ToString("O"));
        var range = $"{url}?from={start}&to={end}&actorId={actorId:D}&pageSize=1";
        var first = (await reader.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>(range))!;
        var second = (await reader.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>(range + "&page=2"))!;
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(all.Items.Skip(1).Select(item => item.Id), first.Items.Concat(second.Items).Select(item => item.Id));
        Assert.All(first.Items.Concat(second.Items), item => Assert.Equal(receipt.MovementId, item.MovementId));

        var lot = all.Items.Single(item => item.Quantity == "2.125").Lot!;
        var page = (await reader.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>(
            $"{range}&locationId={firstLocation.Id:D}&lotId={lot.Id:D}"))!;
        Assert.Equal(1, page.TotalCount);
        var movement = Assert.Single(page.Items);
        Assert.Equal("Receipt", movement.Kind);
        Assert.Equal("2.125", movement.Quantity);
        Assert.Equal("UD", movement.BaseUnit);
        Assert.Equal(actorId, movement.Actor.Id);
        Assert.Equal(receipt.SourceDocumentId, movement.Document.Id);
        Assert.Equal("ALB-123", movement.SupportReference);
        Assert.Equal("Proveedor", movement.CounterpartyReference);
        Assert.Null(movement.Origin);
        Assert.Equal(firstLocation.Code, movement.Destination!.Code);
        Assert.Equal("LOTE-1", movement.Lot!.Code);

        // Las opciones proceden del diario de este artículo, también inactivo, no del catálogo de usuarios.
        var optionsUrl = url + "/filters";
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.Client.GetAsync(optionsUrl + "?field=actor")).StatusCode);
        var actors = await reader.Client.GetFromJsonAsync<JsonElement>(optionsUrl + "?field=actor");
        Assert.Equal(actorId, Assert.Single(actors.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var locations = await reader.Client.GetFromJsonAsync<JsonElement>(optionsUrl + "?field=location&pageSize=1");
        Assert.Equal(2, locations.GetProperty("totalCount").GetInt32());
        Assert.Single(locations.GetProperty("items").EnumerateArray());
        var lots = await reader.Client.GetFromJsonAsync<JsonElement>(optionsUrl + "?field=lot&q=lote-1");
        Assert.Equal("LOTE-1", Assert.Single(lots.GetProperty("items").EnumerateArray()).GetProperty("label").GetString());
        var literal = await reader.Client.GetFromJsonAsync<JsonElement>(optionsUrl + "?field=lot&q=%25");
        Assert.Empty(literal.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await reader.Client.GetAsync(optionsUrl + "?field=other")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.Client.GetAsync($"/api/v1/articles/{Guid.NewGuid():D}/movements/filters?field=actor")).StatusCode);
    }

    [Fact]
    public async Task Inactive_article_rejects_new_stock_but_can_release_and_ship_existing_reservation()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, actor) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        StockCommand Command(StockOperation operation, string type, decimal quantity) =>
            StockCore.Prepare(operation, StockTestHarness.Context(type), [new(article.Id, location.Id, null, quantity)]);
        await stock.RunAsync(actor, Command(StockOperation.Receive, "receipt", 5m));
        await stock.RunAsync(actor, Command(StockOperation.Reserve, "reservation", 3m));

        var path = $"/api/v1/articles/{article.Id:D}";
        using var current = await api.Client.GetAsync(path);
        var disable = MasterDataArticleTests.Body(article.FamilyId, article.Sku, lot: false, active: false);
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path, disable,
            version: current.Headers.ETag!.ToString())).StatusCode);

        var receipt = await Assert.ThrowsAsync<InventoryProblemException>(() =>
            stock.RunAsync(actor, Command(StockOperation.Receive, "receipt", 1m)));
        Assert.Equal("ArticleInactive", receipt.Code);
        var reservation = await Assert.ThrowsAsync<InventoryProblemException>(() =>
            stock.RunAsync(actor, Command(StockOperation.Reserve, "reservation", 1m)));
        Assert.Equal("ArticleInactive", reservation.Code);
        await stock.RunAsync(actor, Command(StockOperation.Release, "release", 1m));
        await stock.RunAsync(actor, StockCore.PrepareShipment(StockTestHarness.Context("shipment"),
            [new(article.Id, location.Id, null, 2m, 2m)]));

        var balance = (await stock.BalanceAsync(article.Id, location.Id))!;
        Assert.Equal(3m, balance.Physical);
        Assert.Equal(0m, balance.Reserved);
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
        var history = (await api.Client.GetFromJsonAsync<PageResult<ArticleMovementDto>>(path + "/movements"))!;
        Assert.Equal(4, history.TotalCount);
        Assert.All(history.Items, movement => Assert.Null(movement.Lot));
    }
}
