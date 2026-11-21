using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T007 (núcleo de H-04…H-07; D-02, D-03, D-12; FR-001–FR-005): crear, leer, editar y eliminar
// el borrador por HTTP con sesión real. T008 amplía este archivo con las carreras de INT-HU03-01, la
// retirada por edición, los permisos completos y el soporte común ShippingApiSupport.
public sealed class ShipmentDraftTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string Shipments = "/api/v1/shipments";

    [Fact]
    public async Task H04_creates_a_draft_without_touching_stock_order_or_journal_and_replays_it()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var before = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        var key = Guid.NewGuid();
        var body = Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1), (seed.OrderLine, seed.Location, seed.Lot, "6"));

        using var created = await operario.WriteAsync(HttpMethod.Post, Shipments, body, key);
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == created.StatusCode, text);
        var mutation = JsonDocument.Parse(text).RootElement;
        Assert.Equal(key, mutation.GetProperty("operationId").GetGuid());
        var shipment = mutation.GetProperty("shipment");
        var id = shipment.GetProperty("id").GetGuid();
        Assert.Equal($"{Shipments}/{id:D}", created.Headers.Location!.ToString());
        Assert.NotNull(created.Headers.ETag);
        Assert.Equal("Draft", shipment.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, shipment.GetProperty("operationId").ValueKind);
        var line = shipment.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("6.000", line.GetProperty("quantity").GetString());
        Assert.Equal("0.000", line.GetProperty("ownReservationConsumed").GetString());
        Assert.Empty(line.GetProperty("consumptions").EnumerateArray());

        // Un borrador no cambia existencias, servido ni diario (FR-004).
        var (_, stock) = await StockApiSupport.GetStockAsync(operario.Client, $"?articleId={seed.Article}");
        Assert.Equal("10.000", stock.GetProperty("totals").GetProperty("physical").GetString());
        var after = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Equal("0.000", after.Body.GetProperty("lines")[0].GetProperty("shipped").GetString());
        await using (var database = fixture.ApplicationDatabase())
            Assert.False(await database.Set<StockMovement>().AnyAsync(movement => movement.SourceDocumentId == id));

        using var replay = await operario.WriteAsync(HttpMethod.Post, Shipments, body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(text, await replay.Content.ReadAsStringAsync());
        await using var check = fixture.ApplicationDatabase();
        Assert.Equal(1, await check.Set<Shipment>().CountAsync(value => value.CustomerOrderId == seed.Order));
    }

    [Fact]
    public async Task H04_the_detail_has_only_own_data_lines_by_id_and_its_etag()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var other = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, other, 5m);
        var (id, etag) = await CreateAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            (seed.OrderLine, seed.Location, seed.Lot, "4"), (seed.OrderLine, other, seed.Lot, "2")));

        var reader = await support.SessionAsync("GestorMaestros");
        using var detail = await reader.Client.GetAsync($"{Shipments}/{id:D}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(etag, detail.Headers.ETag!.ToString());
        Assert.Contains("no-store", detail.Headers.CacheControl!.ToString());
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seed.Order, body.GetProperty("customerOrderId").GetGuid());
        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(lines.Select(line => line.GetProperty("id").GetGuid()).Order(), lines.Select(line => line.GetProperty("id").GetGuid()));
        Assert.All(lines, line => Assert.Equal(seed.Article, line.GetProperty("articleId").GetGuid()));
        // Solo datos propios: sin SKU, nombres ni códigos (los da el contexto, T012).
        Assert.False(lines[0].TryGetProperty("sku", out _));
        using (var missing = await reader.Client.GetAsync($"{Shipments}/{Guid.NewGuid():D}"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task H05_shape_and_state_errors_are_reported_per_line_and_nothing_is_created()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var empty = await support.LocationAsync("V01-" + StockApiSupport.Unique(6));
        var (otherArticle, _) = await support.ArticleAsync();
        var (_, otherLine, _) = await StockApiSupport.CreateOrderAsync(operario, otherArticle, "1");
        var past = DateTimeOffset.UtcNow.AddHours(-1);

        // Forma: cantidad 0, más de tres decimales, bucket repetido y fecha futura, a la vez.
        using (var shape = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(1),
                   (seed.OrderLine, seed.Location, seed.Lot, "0"), (seed.OrderLine, seed.Location, seed.Lot, "1.2345"))))
        {
            var errors = (await StockApiSupport.ProblemAsync(shape, HttpStatusCode.UnprocessableEntity, "ValidationFailed")).GetProperty("errors");
            Assert.True(errors.TryGetProperty("lines[0].quantity", out _));
            Assert.True(errors.TryGetProperty("lines[1].quantity", out _));
            Assert.True(errors.TryGetProperty("occurredAt", out _));
        }
        using (var repeated = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, past,
                   (seed.OrderLine, seed.Location, seed.Lot, "1"), (seed.OrderLine, seed.Location, seed.Lot, "2"))))
            Assert.True((await StockApiSupport.ProblemAsync(repeated, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("lines", out _));

        // Bajo bloqueo: fracción en artículo entero, Σ > pendiente, línea de otro pedido y lote ajeno.
        using (var rules = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, past,
                   (seed.OrderLine, seed.Location, seed.Lot, "6.5"))))
            Assert.True((await StockApiSupport.ProblemAsync(rules, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("lines[0].quantity", out _));
        using (var pending = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, past,
                   (seed.OrderLine, seed.Location, seed.Lot, "7"))))
            Assert.True((await StockApiSupport.ProblemAsync(pending, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("lines[0].quantity", out _));
        using (var foreign = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, past,
                   (otherLine, seed.Location, seed.Lot, "1"))))
            Assert.True((await StockApiSupport.ProblemAsync(foreign, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("lines[0].customerOrderLineId", out _));
        var foreignLot = await UntrackedLotAsync(otherArticle, support);
        using (var lot = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, past,
                   (seed.OrderLine, seed.Location, foreignLot, "1"))))
            Assert.True((await StockApiSupport.ProblemAsync(lot, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("lines[0].lotId", out _));

        // Bucket sin existencias del artículo y pedido inexistente.
        using (var noStock = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed.Order, past,
                   (seed.OrderLine, empty, seed.Lot, "1"))))
            await StockApiSupport.ProblemAsync(noStock, HttpStatusCode.Conflict, "InsufficientStock");
        using (var missing = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(Guid.NewGuid(), past,
                   (seed.OrderLine, seed.Location, seed.Lot, "1"))))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var database = fixture.ApplicationDatabase();
        Assert.False(await database.Set<Shipment>().AnyAsync(value => value.CustomerOrderId == seed.Order));
    }

    [Fact]
    public async Task H06_edits_a_draft_with_its_current_etag_only()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, etag) = await CreateAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            (seed.OrderLine, seed.Location, seed.Lot, "2")));
        var path = $"{Shipments}/{id:D}";
        var edited = Body(null, DateTimeOffset.UtcNow.AddHours(-2), (seed.OrderLine, seed.Location, seed.Lot, "5"));

        using (var missing = await operario.WriteAsync(HttpMethod.Put, path, edited))
            await StockApiSupport.ProblemAsync(missing, HttpStatusCode.PreconditionRequired, "PreconditionRequired");
        using var updated = await operario.WriteAsync(HttpMethod.Put, path, edited, version: etag);
        var text = await updated.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == updated.StatusCode, text);
        var newEtag = updated.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newEtag);
        Assert.Equal("5.000", JsonDocument.Parse(text).RootElement.GetProperty("shipment").GetProperty("lines")[0]
            .GetProperty("quantity").GetString());
        using (var stale = await operario.WriteAsync(HttpMethod.Put, path, edited, version: etag))
            await StockApiSupport.ProblemAsync(stale, HttpStatusCode.PreconditionFailed, "VersionConflict");
        // Otro pedido en el cuerpo de PUT: 422 (contrato); el mismo pedido se admite.
        using (var otherOrder = await operario.WriteAsync(HttpMethod.Put, path,
                   Body(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-2), (seed.OrderLine, seed.Location, seed.Lot, "5")), version: newEtag))
            Assert.True((await StockApiSupport.ProblemAsync(otherOrder, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("customerOrderId", out _));
        using (var sameOrder = await operario.WriteAsync(HttpMethod.Put, path,
                   Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-2), (seed.OrderLine, seed.Location, seed.Lot, "4")), version: newEtag))
            Assert.Equal(HttpStatusCode.OK, sameOrder.StatusCode);
        using (var notFound = await operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{Guid.NewGuid():D}", edited, version: etag))
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task H07_deletes_a_draft_with_its_etag_and_replays_the_204()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, etag) = await CreateAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            (seed.OrderLine, seed.Location, seed.Lot, "2")));
        var path = $"{Shipments}/{id:D}";
        var key = Guid.NewGuid();

        using (var missing = await operario.WriteAsync(HttpMethod.Delete, path, new { }))
            await StockApiSupport.ProblemAsync(missing, HttpStatusCode.PreconditionRequired, "PreconditionRequired");
        using (var deleted = await operario.WriteAsync(HttpMethod.Delete, path, new { }, key, etag))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Empty(await deleted.Content.ReadAsStringAsync());
        }
        await using (var database = fixture.ApplicationDatabase())
        {
            Assert.False(await database.Set<Shipment>().AnyAsync(value => value.Id == id));
            Assert.False(await database.Set<ShipmentLine>().AnyAsync(value => value.ShipmentId == id));
            Assert.False(await database.Set<StockMovement>().AnyAsync(movement => movement.SourceDocumentId == id));
        }
        using (var replay = await operario.WriteAsync(HttpMethod.Delete, path, new { }, key, etag))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using (var again = await operario.WriteAsync(HttpMethod.Delete, path, new { }, Guid.NewGuid(), etag))
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        using (var reading = await operario.Client.GetAsync(path))
            Assert.Equal(HttpStatusCode.NotFound, reading.StatusCode);
    }

    [Fact]
    public async Task Writes_require_shipping_record_and_reads_inventory_read()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var body = Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1), (seed.OrderLine, seed.Location, seed.Lot, "1"));
        var (id, _) = await CreateAsync(operario, body);

        foreach (var role in new[] { "GestorMaestros", "ResponsableAlmacen" })
        {
            var session = await support.SessionAsync(role);
            using (var forbidden = await session.WriteAsync(HttpMethod.Post, Shipments, body))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var allowed = await session.Client.GetAsync($"{Shipments}/{id:D}");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        var admin = await support.SessionAsync("Administrador");
        using (var denied = await admin.Client.GetAsync($"{Shipments}/{id:D}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    // --- Preparación (T008 la traslada a ShippingApiSupport) ---

    private sealed record Seed(Guid Article, Guid Location, Guid Lot, Guid Order, Guid OrderLine);

    private static async Task<Seed> SeedAsync(StockApiSupport support, MasterDataApi operario, decimal physical, string ordered)
    {
        var (article, _) = await support.ArticleAsync();
        var location = await support.LocationAsync("N01-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(article, location, physical);
        var (order, line, _) = await StockApiSupport.CreateOrderAsync(operario, article, ordered);
        return new Seed(article, location, await UntrackedLotAsync(article, support), order, line);
    }

    private static async Task<Guid> UntrackedLotAsync(Guid article, StockApiSupport support)
    {
        await using var database = support.Stock.Fixture.ApplicationDatabase();
        var existing = await database.Set<Lot>().Where(lot => lot.ArticleId == article && lot.IsUntracked)
            .Select(lot => (Guid?)lot.Id).SingleOrDefaultAsync();
        if (existing is { } id) return id;
        var created = Lot.Untracked(article);
        database.Add(created);
        await database.SaveChangesAsync();
        return created.Id;
    }

    private static object Body(Guid? order, DateTimeOffset occurredAt, params (Guid Line, Guid Location, Guid Lot, string Quantity)[] lines) =>
        order is null
            ? new { occurredAt, lines = Lines(lines) }
            : new { customerOrderId = order, occurredAt, lines = Lines(lines) };

    private static object[] Lines((Guid Line, Guid Location, Guid Lot, string Quantity)[] lines) =>
        lines.Select(value => (object)new
        {
            customerOrderLineId = value.Line, locationId = value.Location, lotId = value.Lot, quantity = value.Quantity
        }).ToArray();

    private static async Task<(Guid Id, string ETag)> CreateAsync(MasterDataApi session, object body)
    {
        using var created = await session.WriteAsync(HttpMethod.Post, Shipments, body);
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == created.StatusCode, text);
        return (JsonDocument.Parse(text).RootElement.GetProperty("shipment").GetProperty("id").GetGuid(),
            created.Headers.ETag!.ToString());
    }
}
