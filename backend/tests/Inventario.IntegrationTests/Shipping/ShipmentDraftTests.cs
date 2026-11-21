using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Shipping.ShippingApiSupport;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T007/T008 (H-04…H-07, H-20 en lo que toca al borrador; D-02, D-03, D-09, D-12; FR-001–FR-005,
// FR-019, FR-021; INT-HU03-01): borrador por HTTP con sesión real, contrato de escritores de
// referencias de Article (crear, editar, retirar y borrar, con carreras reales en ambos órdenes) y
// permisos. Lo que exige una salida confirmada (PUT y DELETE → 409) está en ShipmentConfirmTests (T010).
public sealed class ShipmentDraftTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task H04_creates_a_draft_without_touching_stock_order_or_journal_and_replays_it()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var before = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        var key = Guid.NewGuid();
        var body = Body(seed, "6");

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
        var (id, etag) = await CreateDraftAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
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

        // Forma: cantidad 0, más de tres decimales y fecha futura, a la vez.
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
                   (seed.OrderLine, seed.Location, seed.Lot, "5.5"))))
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
        var foreignLot = await UntrackedLotAsync(fixture, otherArticle);
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
        var (id, etag) = await CreateDraftAsync(operario, Body(seed, "2"));
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
        Assert.Equal(newEtag, (await DetailAsync(operario.Client, id)).ETag);
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

    // analyze-1 M1 (FR-021): la misma intención con otro contenido se rechaza al crear, editar y eliminar.
    [Fact]
    public async Task Reusing_a_key_with_another_content_is_an_idempotency_conflict_when_creating_editing_and_deleting()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");

        var createKey = Guid.NewGuid();
        var (id, etag) = await CreateDraftAsync(operario, Body(seed, "2"), createKey);
        using (var conflict = await operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed, "3"), createKey))
            await StockApiSupport.ProblemAsync(conflict, HttpStatusCode.Conflict, "IdempotencyConflict");

        var path = $"{Shipments}/{id:D}";
        var editKey = Guid.NewGuid();
        using (var edited = await operario.WriteAsync(HttpMethod.Put, path, Body(seed, "4"), editKey, etag))
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using (var conflict = await operario.WriteAsync(HttpMethod.Put, path, Body(seed, "5"), editKey, etag))
            await StockApiSupport.ProblemAsync(conflict, HttpStatusCode.Conflict, "IdempotencyConflict");
        var (detail, current) = await DetailAsync(operario.Client, id);
        Assert.Equal("4.000", detail.GetProperty("lines")[0].GetProperty("quantity").GetString());

        // Eliminar: la huella incluye el recurso; la misma clave sobre otra salida es otra intención.
        var (other, otherTag) = await CreateDraftAsync(operario, Body(seed, "1"));
        var deleteKey = Guid.NewGuid();
        using (var deleted = await operario.WriteAsync(HttpMethod.Delete, path, new { }, deleteKey, current))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var conflict = await operario.WriteAsync(HttpMethod.Delete, $"{Shipments}/{other:D}", new { }, deleteKey, otherTag))
            await StockApiSupport.ProblemAsync(conflict, HttpStatusCode.Conflict, "IdempotencyConflict");

        await using var database = fixture.ApplicationDatabase();
        Assert.True(await database.Set<Shipment>().AnyAsync(value => value.Id == other));
        Assert.False(await database.Set<Shipment>().AnyAsync(value => value.Id == id));
        Assert.Equal(1, await database.Set<Shipment>().CountAsync(value => value.CustomerOrderId == seed.Order));
    }

    [Fact]
    public async Task H07_deletes_a_draft_retires_its_reference_and_the_order_line_still_protects_the_article()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, etag) = await CreateDraftAsync(operario, Body(seed, "2"));
        var path = $"{Shipments}/{id:D}";
        var key = Guid.NewGuid();
        Assert.True((await ShipmentUsageAsync(factory, seed.Article)).HasReferences);

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

        // INT-HU03-01: el contributor de HU-03 deja de ver la línea; la del pedido sigue fijando la
        // definición y rechazando el borrado del artículo.
        Assert.False((await ShipmentUsageAsync(factory, seed.Article)).HasReferences);
        var capabilities = await ArticleCapabilitiesAsync(gestor, seed.Article);
        Assert.False(capabilities.GetProperty("canEditDefinition").GetBoolean());
        Assert.False(capabilities.GetProperty("canDelete").GetBoolean());
        var remove = await StockApiSupport.StockWriterAsync(StockWriter.ArticleDelete, seed.Article, seed.Sku, Guid.Empty, gestor, operario);
        using var rejected = await remove();
        await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
    }

    // INT-HU03-01 (analyze-1 H4): retirar la referencia al quitar una línea con PUT.
    [Fact]
    public async Task H07_editing_a_draft_to_remove_the_only_line_of_an_article_retires_that_reference()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var (x, _) = await support.ArticleAsync();
        var (y, _) = await support.ArticleAsync();
        var location = await support.LocationAsync("N01-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(x, location, 5m);
        await support.ReceiveAsync(y, location, 5m);
        var (order, lines) = await ShippingApiSupport.CreateOrderAsync(operario, (x, "2"), (y, "2"));
        var lotX = await UntrackedLotAsync(fixture, x);
        var lotY = await UntrackedLotAsync(fixture, y);
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var (id, etag) = await CreateDraftAsync(operario, Body(order, past, (lines[x], location, lotX, "1"), (lines[y], location, lotY, "1")));
        Assert.True((await ShipmentUsageAsync(factory, x)).HasReferences);

        using (var edited = await operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{id:D}",
                   Body(null, past, (lines[y], location, lotY, "2")), version: etag))
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.False((await ShipmentUsageAsync(factory, x)).HasReferences);
        Assert.True((await ShipmentUsageAsync(factory, y)).HasReferences);
        // La marca histórica y la línea de pedido mantienen bloqueada la definición de X.
        Assert.False((await ArticleCapabilitiesAsync(gestor, x)).GetProperty("canEditDefinition").GetBoolean());
        await using var database = fixture.ApplicationDatabase();
        Assert.True(await database.Articles.Where(article => article.Id == x).Select(article => article.IsDefinitionLocked).SingleAsync());
    }

    public enum DraftWrite { Create, Edit, Delete }

    // INT-HU03-01: referencia/edición y referencia/borrado con conexiones independientes y LockBarrier,
    // en ambos órdenes. La línea de pedido ya fija la definición y el artículo tiene referencias, así
    // que el escritor de A se rechaza siempre y el del borrador se confirma.
    [Theory]
    [InlineData(DraftWrite.Create, StockWriter.ArticleDefinition, true)]
    [InlineData(DraftWrite.Create, StockWriter.ArticleDefinition, false)]
    [InlineData(DraftWrite.Create, StockWriter.ArticleDelete, true)]
    [InlineData(DraftWrite.Create, StockWriter.ArticleDelete, false)]
    [InlineData(DraftWrite.Edit, StockWriter.ArticleDefinition, true)]
    [InlineData(DraftWrite.Edit, StockWriter.ArticleDefinition, false)]
    [InlineData(DraftWrite.Edit, StockWriter.ArticleDelete, true)]
    [InlineData(DraftWrite.Edit, StockWriter.ArticleDelete, false)]
    [InlineData(DraftWrite.Delete, StockWriter.ArticleDelete, true)]
    [InlineData(DraftWrite.Delete, StockWriter.ArticleDelete, false)]
    public async Task Draft_writers_and_article_writers_serialize_in_both_orders(DraftWrite draft, StockWriter writer, bool draftFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var id = Guid.Empty;
        var etag = "";
        if (draft != DraftWrite.Create) (id, etag) = await CreateDraftAsync(operario, Body(seed, "2"));
        var change = await StockApiSupport.StockWriterAsync(writer, seed.Article, seed.Sku, Guid.Empty, gestor, operario);

        Task<HttpResponseMessage> Write() => draft switch
        {
            DraftWrite.Create => operario.WriteAsync(HttpMethod.Post, Shipments, Body(seed, "3")),
            DraftWrite.Edit => operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{id:D}", Body(seed, "3"), version: etag),
            _ => operario.WriteAsync(HttpMethod.Delete, $"{Shipments}/{id:D}", new { }, version: etag)
        };
        // Eliminar el borrador no lee Articles: si va primero se retiene en la lectura de la salida,
        // que hace ya con el bloqueo del artículo.
        if (draftFirst && draft == DraftWrite.Delete) barrier.Hold("Shipments", id);
        else barrier.Hold("Articles", seed.Article);
        var (written, changed) = draftFirst
            ? await barrier.RaceAsync(fixture, Write, change)
            : Swap(await barrier.RaceAsync(fixture, change, Write));
        using var _w = written;
        using var _c = changed;

        var expected = draft switch { DraftWrite.Create => HttpStatusCode.Created, DraftWrite.Edit => HttpStatusCode.OK, _ => HttpStatusCode.NoContent };
        Assert.True(expected == written.StatusCode, await written.Content.ReadAsStringAsync());
        await StockApiSupport.ProblemAsync(changed, HttpStatusCode.Conflict,
            writer == StockWriter.ArticleDefinition ? "ArticleDefinitionLocked" : "HistoryPreventsDeletion");
        await using var database = fixture.ApplicationDatabase();
        var article = await database.Articles.AsNoTracking().SingleAsync(value => value.Id == seed.Article);
        Assert.Equal("UD", article.BaseUnit);
        Assert.True(article.IsDefinitionLocked);
    }

    [Fact]
    public async Task H20_draft_routes_need_a_session_antiforgery_and_shipping_record_and_reads_inventory_read()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var body = Body(seed, "1");
        var (id, etag) = await CreateDraftAsync(operario, body);
        var path = $"{Shipments}/{id:D}";

        foreach (var role in new[] { "GestorMaestros", "Comprador", "ResponsableAlmacen" })
        {
            var session = await support.SessionAsync(role);
            using (var create = await session.WriteAsync(HttpMethod.Post, Shipments, body))
                Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
            using (var edit = await session.WriteAsync(HttpMethod.Put, path, body, version: etag))
                Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
            using (var delete = await session.WriteAsync(HttpMethod.Delete, path, new { }, version: etag))
                Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            using var allowed = await session.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        var admin = await support.SessionAsync("Administrador");
        using (var denied = await admin.Client.GetAsync(path))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, Shipments) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var noToken = await operario.Client.SendAsync(request);
        await StockApiSupport.ProblemAsync(noToken, HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        await using var database = fixture.ApplicationDatabase();
        Assert.Equal(1, await database.Set<Shipment>().CountAsync(value => value.CustomerOrderId == seed.Order));
    }

    private static (T1, T2) Swap<T2, T1>((T2 First, T1 Second) pair) => (pair.Second, pair.First);
}
