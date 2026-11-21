using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T010 (US2, US5.1; FR-009–FR-012) e INT-HU05-01/02 en lo que corresponde al alta y la
// lectura del pedido. Alta y lectura por HTTP con sesión real (contracts/stock-api.md).
//
// Quedan para otras tareas, porque necesitan reservar o liberar (T011/T013), que aún no existen:
// - T012: el ETag cambia tras una reserva real; carreras de la reserva frente a desactivación, PUT y
//   DELETE; S-18 completo (lector con asignaciones).
// - T014: el ETag cambia tras una liberación real; la prueba de la ubicación desactivada (exige una
//   reserva liberada por completo y la ubicación vaciada); DELETE sigue rechazado tras liberarlo todo.
public sealed class CustomerOrderTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string Orders = "/api/v1/customer-orders";

    [Fact]
    public async Task S08_creates_an_open_order_replays_it_and_reads_it_with_its_etag()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var reader = await support.SessionAsync("GestorMaestros");
        var art01 = await ArticleAsync();
        var art02 = await ArticleAsync();
        var reference = "PED-" + StockApiSupport.Unique(8);
        var body = Order("  " + reference + " ", " Cliente Norte ", (art01.Id, "7"), (art02.Id, "3"));
        var key = Guid.NewGuid();

        using var created = await operario.WriteAsync(HttpMethod.Post, Orders, body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdText = await created.Content.ReadAsStringAsync();
        var mutation = JsonDocument.Parse(createdText).RootElement;
        Assert.Equal(key, mutation.GetProperty("operationId").GetGuid());
        var resource = mutation.GetProperty("resource");
        var id = resource.GetProperty("id").GetGuid();
        Assert.Equal($"{Orders}/{id:D}", created.Headers.Location!.ToString());
        Assert.NotNull(created.Headers.ETag);
        Assert.Equal(reference, resource.GetProperty("externalReference").GetString());
        Assert.Equal("Cliente Norte", resource.GetProperty("customerReference").GetString());
        Assert.Equal("Open", resource.GetProperty("state").GetString());
        var lines = resource.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        // Orden estable por Id de línea, nunca por SKU (data-model, CustomerOrderLine).
        Assert.Equal(lines.Select(line => line.GetProperty("id").GetGuid()).Order(),
            lines.Select(line => line.GetProperty("id").GetGuid()));
        var first = lines.Single(line => line.GetProperty("articleId").GetGuid() == art01.Id);
        Assert.Equal("7.000", first.GetProperty("ordered").GetString());
        Assert.Equal("0.000", first.GetProperty("shipped").GetString());
        Assert.Equal("7.000", first.GetProperty("pending").GetString());
        Assert.Equal("0.000", first.GetProperty("reservedRemaining").GetString());
        Assert.Empty(first.GetProperty("reservations").EnumerateArray());
        // Solo datos propios: el detalle versionado no lleva SKU ni nombre del artículo.
        Assert.False(first.TryGetProperty("sku", out _));
        Assert.False(first.TryGetProperty("articleName", out _));

        // Replay con la misma clave: idéntico, incluidas Location y ETag.
        using var replay = await operario.WriteAsync(HttpMethod.Post, Orders, body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(createdText, await replay.Content.ReadAsStringAsync());
        Assert.Equal(created.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(created.Headers.Location, replay.Headers.Location);

        // GET con ETag: el mismo recurso que devolvió el alta (Inventory.Read; GestorMaestros sí lee).
        var (detail, etag) = await DetailAsync(reader.Client, id);
        Assert.Equal(created.Headers.ETag!.ToString(), etag);
        Assert.Equal(resource.GetRawText(), detail.GetRawText());

        // La primera referencia fija la definición de los dos artículos (R-05).
        foreach (var article in new[] { art01.Id, art02.Id })
        {
            var context = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{article:D}/context");
            Assert.False(context.GetProperty("capabilities").GetProperty("canEditDefinition").GetBoolean());
        }
        await using var db = fixture.ApplicationDatabase();
        Assert.True((await db.Articles.SingleAsync(value => value.Id == art01.Id)).IsDefinitionLocked);
        Assert.Equal(1, await db.Set<CustomerOrder>().CountAsync(order => order.ExternalReference == reference));
    }

    [Fact]
    public async Task S08_rejects_a_reference_already_used_ignoring_case_and_outer_spaces()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var article = await ArticleAsync();
        var reference = "PED-" + StockApiSupport.Unique(8);
        using var created = await operario.WriteAsync(HttpMethod.Post, Orders, Order(reference, "Cliente", (article.Id, "1")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicate = await operario.WriteAsync(HttpMethod.Post, Orders,
            Order("  " + reference.ToLowerInvariant() + "  ", "Otro cliente", (article.Id, "2")));
        var problem = await ProblemAsync(duplicate, HttpStatusCode.Conflict, "DuplicateCode");
        Assert.True(problem.GetProperty("errors").TryGetProperty("externalReference", out _));
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(1, await db.Set<CustomerOrder>().CountAsync(order => order.ExternalReference == reference));
    }

    [Fact]
    public async Task S09_rejects_invalid_lines_per_line_and_creates_nothing()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var whole = await ArticleAsync();
        var other = await ArticleAsync();
        var inactive = await ArticleAsync(active: false);
        var reference = "PED-" + StockApiSupport.Unique(8);

        // Forma: cantidad 0, artículo repetido (un artículo aparece una sola vez por pedido) y más
        // de tres decimales, todo en la misma respuesta.
        using var shape = await operario.WriteAsync(HttpMethod.Post, Orders,
            Order(reference, "Cliente", (whole.Id, "0"), (other.Id, "2"), (other.Id, "1"), (Guid.NewGuid(), "1.2345")));
        var errors = (await ProblemAsync(shape, HttpStatusCode.UnprocessableEntity, "ValidationFailed")).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lines[0].quantity", out _));
        Assert.True(errors.TryGetProperty("lines[2].articleId", out _));
        Assert.True(errors.TryGetProperty("lines[3].quantity", out _));
        Assert.False(errors.TryGetProperty("lines[1].articleId", out _));

        // Cabecera: referencias vacías y sin líneas.
        using var header = await operario.WriteAsync(HttpMethod.Post, Orders,
            new { externalReference = "   ", customerReference = "", lines = Array.Empty<object>() });
        errors = (await ProblemAsync(header, HttpStatusCode.UnprocessableEntity, "ValidationFailed")).GetProperty("errors");
        Assert.True(errors.TryGetProperty("externalReference", out _));
        Assert.True(errors.TryGetProperty("customerReference", out _));
        Assert.True(errors.TryGetProperty("lines", out _));

        // Bajo bloqueo: 1,5 en un artículo entero.
        using var fraction = await operario.WriteAsync(HttpMethod.Post, Orders,
            Order(reference, "Cliente", (whole.Id, "1"), (other.Id, "1.5")));
        errors = (await ProblemAsync(fraction, HttpStatusCode.UnprocessableEntity, "ValidationFailed")).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lines[1].quantity", out _));

        // Artículo inexistente.
        using var missing = await operario.WriteAsync(HttpMethod.Post, Orders,
            Order(reference, "Cliente", (whole.Id, "1"), (Guid.NewGuid(), "1")));
        errors = (await ProblemAsync(missing, HttpStatusCode.UnprocessableEntity, "ValidationFailed")).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lines[1].articleId", out _));

        // Artículo desactivado: 409 que nombra el SKU.
        using var deactivated = await operario.WriteAsync(HttpMethod.Post, Orders,
            Order(reference, "Cliente", (whole.Id, "1"), (inactive.Id, "1")));
        var problem = await ProblemAsync(deactivated, HttpStatusCode.Conflict, "ArticleInactive");
        Assert.Contains(inactive.Sku, problem.GetProperty("detail").GetString());

        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Set<CustomerOrder>().AnyAsync(order => order.ExternalReference == reference));
        Assert.False((await db.Articles.SingleAsync(value => value.Id == whole.Id)).IsDefinitionLocked);
    }

    [Fact]
    public async Task Permissions_create_needs_shipping_record_and_reads_need_inventory_read()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var article = await ArticleAsync();
        var reference = "PED-" + StockApiSupport.Unique(8);
        foreach (var role in new[] { "ResponsableAlmacen", "GestorMaestros" })
        {
            var session = await support.SessionAsync(role);
            using var forbidden = await session.WriteAsync(HttpMethod.Post, Orders, Order(reference, "Cliente", (article.Id, "1")));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        var id = await CreateAsync(await support.SessionAsync("Operario"), (article.Id, "1"));

        foreach (var role in new[] { "GestorMaestros", "ResponsableAlmacen", "Operario" })
        {
            var session = await support.SessionAsync(role);
            using var detail = await session.Client.GetAsync($"{Orders}/{id:D}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var context = await session.Client.GetAsync($"{Orders}/{id:D}/context");
            Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        }
        var admin = await support.SessionAsync("Administrador");
        using (var denied = await admin.Client.GetAsync($"{Orders}/{id:D}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
               { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Orders}/{id:D}")).StatusCode);

        var gestor = await support.SessionAsync("GestorMaestros");
        using (var notFound = await gestor.Client.GetAsync($"{Orders}/{Guid.NewGuid():D}"))
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        using (var notFound = await gestor.Client.GetAsync($"{Orders}/{Guid.NewGuid():D}/context"))
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Set<CustomerOrder>().AnyAsync(order => order.ExternalReference == reference));
    }

    // Convergence C3 (G2; FR-026, US2.1): el contexto da la operación del alta, que se lee del
    // OperationResult registrado (POST /customer-orders con SourceDocumentId = pedido). Así el
    // detalle la muestra también al recuperar el pedido, sin cambiar el esquema ni el ETag.
    [Fact]
    public async Task C3_context_returns_the_creation_operation_also_after_a_replay_and_never_another_order()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var article = await ArticleAsync();
        var key = Guid.NewGuid();
        var body = Order("PED-" + StockApiSupport.Unique(8), "Cliente", (article.Id, "2"));
        using var created = await operario.WriteAsync(HttpMethod.Post, Orders, body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var other = await CreateAsync(operario, (article.Id, "1"));
        var (_, etag) = await DetailAsync(gestor.Client, id);

        Assert.Equal(key, (await ContextAsync(gestor.Client, id)).GetProperty("creationOperationId").GetGuid());
        using (var replay = await operario.WriteAsync(HttpMethod.Post, Orders, body, key))
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(key, (await ContextAsync(gestor.Client, id)).GetProperty("creationOperationId").GetGuid());
        Assert.NotEqual(key, (await ContextAsync(gestor.Client, other)).GetProperty("creationOperationId").GetGuid());
        Assert.Equal(etag, (await DetailAsync(gestor.Client, id)).ETag);
    }

    [Fact]
    public async Task Etag_covers_only_own_data_renaming_the_article_changes_only_the_context()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var article = await ArticleAsync();
        var id = await CreateAsync(operario, (article.Id, "7"));
        var (before, etag) = await DetailAsync(gestor.Client, id);

        var context = await ContextAsync(gestor.Client, id);
        Assert.Equal(etag, context.GetProperty("orderVersion").GetString());
        Assert.True(context.TryGetProperty("asOf", out _));
        var described = context.GetProperty("articles").EnumerateArray().Single();
        Assert.Equal(article.Id, described.GetProperty("id").GetGuid());
        Assert.Equal(article.Sku, described.GetProperty("sku").GetString());
        Assert.Equal("Artículo de pedido", described.GetProperty("name").GetString());
        Assert.Equal("UD", described.GetProperty("baseUnit").GetString());
        Assert.True(described.GetProperty("isActive").GetBoolean());
        Assert.False(described.GetProperty("tracksLot").GetBoolean());
        Assert.False(described.GetProperty("allowsFractional").GetBoolean());
        Assert.Empty(context.GetProperty("locations").EnumerateArray());
        Assert.Empty(context.GetProperty("lots").EnumerateArray());

        // Renombrar el artículo (y desactivarlo, también dato de otro agregado) no cambia el detalle.
        var articlePath = $"/api/v1/articles/{article.Id:D}";
        using var current = await gestor.Client.GetAsync(articlePath);
        using var renamed = await gestor.WriteAsync(HttpMethod.Put, articlePath,
            MasterDataArticleTests.Body(article.FamilyId, article.Sku, name: "Nombre corregido", lot: false, active: false),
            version: current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var (after, sameEtag) = await DetailAsync(gestor.Client, id);
        Assert.Equal(etag, sameEtag);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        var refreshed = await ContextAsync(gestor.Client, id);
        var renamedArticle = refreshed.GetProperty("articles").EnumerateArray().Single();
        Assert.Equal("Nombre corregido", renamedArticle.GetProperty("name").GetString());
        Assert.False(renamedArticle.GetProperty("isActive").GetBoolean());
        Assert.Equal(etag, refreshed.GetProperty("orderVersion").GetString());
    }

    [Fact]
    public async Task Etag_changes_with_a_simulated_shipment_that_renews_the_header_and_the_context_follows_it()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var article = await ArticleAsync();
        var id = await CreateAsync(operario, (article.Id, "7"));
        var (_, etag) = await DetailAsync(operario.Client, id);

        // HU-03 aún no existe. Se simula su escritor como exige hu03-integration.md, regla 4: cambia
        // lo servido de la línea y renueva la versión de la cabecera en la misma transacción.
        await using (var db = fixture.ApplicationDatabase())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CustomerOrderLines SET ShippedQuantity = 2 WHERE CustomerOrderId = {id}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CustomerOrders SET State = State WHERE Id = {id}");
            await transaction.CommitAsync();
        }

        var (shipped, changed) = await DetailAsync(operario.Client, id);
        Assert.NotEqual(etag, changed);
        var line = shipped.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("2.000", line.GetProperty("shipped").GetString());
        Assert.Equal("5.000", line.GetProperty("pending").GetString());
        Assert.Equal(changed, (await ContextAsync(operario.Client, id)).GetProperty("orderVersion").GetString());
    }

    [Fact]
    public async Task Writers_contract_order_line_locks_the_definition_and_prevents_deleting_the_article()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var article = await ArticleAsync();
        await CreateAsync(operario, (article.Id, "1"));
        var path = $"/api/v1/articles/{article.Id:D}";

        using (var current = await gestor.Client.GetAsync(path))
        {
            using var definition = await gestor.WriteAsync(HttpMethod.Put, path,
                MasterDataArticleTests.Body(article.FamilyId, article.Sku, unit: "KG", fractions: true, lot: true),
                version: current.Headers.ETag!.ToString());
            await ProblemAsync(definition, HttpStatusCode.Conflict, "ArticleDefinitionLocked");
        }
        var context = await gestor.Client.GetFromJsonAsync<JsonElement>($"{path}/context");
        var capabilities = context.GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("canEditDefinition").GetBoolean());
        Assert.False(capabilities.GetProperty("canDelete").GetBoolean());
        Assert.Equal("References", capabilities.GetProperty("deleteBlockReason").GetString());

        // Solo una línea de pedido (sin movimientos ni saldo): rechazo por referencias, no 503.
        using (var current = await gestor.Client.GetAsync(path))
        {
            using var deleted = await gestor.WriteAsync(HttpMethod.Delete, path, new { }, version: current.Headers.ETag!.ToString());
            await ProblemAsync(deleted, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
        }
        await using var db = fixture.ApplicationDatabase();
        Assert.True(await db.Articles.AnyAsync(value => value.Id == article.Id));
        // «Bloqueo tras retirar referencias»: no aplica. HU-05 no edita ni cancela pedidos y las
        // cuatro tablas tienen DENY DELETE, así que la referencia de la línea nunca se retira.
    }

    public enum Writer { Deactivate, Definition, Delete }

    [Theory]
    [InlineData(Writer.Deactivate, true)]
    [InlineData(Writer.Deactivate, false)]
    [InlineData(Writer.Definition, true)]
    [InlineData(Writer.Definition, false)]
    [InlineData(Writer.Delete, true)]
    [InlineData(Writer.Delete, false)]
    public async Task Create_and_article_writers_serialize_in_both_orders(Writer writer, bool createFirst)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var factory = new ApiFactory(fixture.Settings, "Development", services =>
            services.ConfigureDbContext<InventoryDbContext>((_, options) => options.AddInterceptors(barrier)));
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var article = await ArticleAsync();
        // Desactivar: un pedido previo ya fijó la definición, así que la versión del artículo no
        // cambia con la nueva alta y la desactivación se decide solo por el bloqueo.
        if (writer == Writer.Deactivate) await CreateAsync(operario, (article.Id, "1"));
        var path = $"/api/v1/articles/{article.Id:D}";
        using var current = await gestor.Client.GetAsync(path);
        var etag = current.Headers.ETag!.ToString();
        var reference = "PED-" + StockApiSupport.Unique(8);
        // Si el PUT de la definición gana, el alta debe leer la definición nueva (fracciones).
        var quantity = writer == Writer.Definition && !createFirst ? "1.5" : "1";

        barrier.Predicate = createFirst
            ? entity => entity is CustomerOrder order && order.ExternalReference == reference
            // La auditoría sigue en el contexto tras guardar; un artículo borrado ya se ha separado.
            : entity => entity is MasterDataAudit audit && audit.EntityId == article.Id;
        Task<HttpResponseMessage> Create() =>
            operario.WriteAsync(HttpMethod.Post, Orders, Order(reference, "Cliente", (article.Id, quantity)));
        Task<HttpResponseMessage> Change() => writer switch
        {
            Writer.Delete => gestor.WriteAsync(HttpMethod.Delete, path, new { }, version: etag),
            Writer.Definition => gestor.WriteAsync(HttpMethod.Put, path,
                MasterDataArticleTests.Body(article.FamilyId, article.Sku, unit: "UD", fractions: true, lot: false), version: etag),
            _ => gestor.WriteAsync(HttpMethod.Put, path,
                MasterDataArticleTests.Body(article.FamilyId, article.Sku, lot: false, active: false), version: etag)
        };

        Task<HttpResponseMessage> creating;
        Task<HttpResponseMessage> changing;
        try
        {
            if (createFirst)
            {
                creating = Create();
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                changing = Change();
            }
            else
            {
                changing = Change();
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                creating = Create();
            }
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }
        using var created = await creating;
        using var changed = await changing;

        await using var db = fixture.ApplicationDatabase();
        var order = await db.Set<CustomerOrder>().AsNoTracking().Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.ExternalReference == reference);
        var persisted = await db.Articles.AsNoTracking().SingleOrDefaultAsync(value => value.Id == article.Id);
        if (createFirst)
        {
            // El alta gana: queda el compromiso y el escritor del artículo lo respeta.
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.NotNull(order);
            Assert.Equal(CustomerOrderState.Open, order.State);
            Assert.Equal(article.Id, Assert.Single(order.Lines).ArticleId);
            Assert.NotNull(persisted);
            Assert.True(persisted.IsDefinitionLocked);
            if (writer == Writer.Deactivate)
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.False(persisted.IsActive);
                return;
            }
            // La primera referencia renovó la versión del artículo: el If-Match leído antes es
            // obsoleto (412). Con la versión actual, la definición y el borrado se rechazan (409).
            Assert.Equal(HttpStatusCode.PreconditionFailed, changed.StatusCode);
            using var latest = await gestor.Client.GetAsync(path);
            using var retried = writer == Writer.Delete
                ? await gestor.WriteAsync(HttpMethod.Delete, path, new { }, version: latest.Headers.ETag!.ToString())
                : await gestor.WriteAsync(HttpMethod.Put, path,
                    MasterDataArticleTests.Body(article.FamilyId, article.Sku, unit: "UD", fractions: true, lot: false),
                    version: latest.Headers.ETag!.ToString());
            await ProblemAsync(retried, HttpStatusCode.Conflict,
                writer == Writer.Delete ? "HistoryPreventsDeletion" : "ArticleDefinitionLocked");
            Assert.False((await db.Articles.AsNoTracking().SingleAsync(value => value.Id == article.Id)).AllowsFractional);
            return;
        }

        switch (writer)
        {
            case Writer.Deactivate:
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                await ProblemAsync(created, HttpStatusCode.Conflict, "ArticleInactive");
                Assert.Null(order);
                break;
            case Writer.Definition:
                // El PUT gana: el alta lee la definición nueva bajo el bloqueo y admite 1,5.
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                Assert.Equal(1.5m, Assert.Single(order!.Lines).OrderedQuantity);
                Assert.True(persisted!.AllowsFractional);
                Assert.True(persisted.IsDefinitionLocked);
                break;
            default:
                Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
                var errors = (await ProblemAsync(created, HttpStatusCode.UnprocessableEntity, "ValidationFailed")).GetProperty("errors");
                Assert.True(errors.TryGetProperty("lines[0].articleId", out _));
                Assert.Null(order);
                Assert.Null(persisted);
                break;
        }
    }

    [Fact]
    public async Task Unique_index_race_on_the_external_reference_maps_to_duplicate_code()
    {
        // T009: defensa última del índice único (D-10). Se provoca la violación real del índice
        // UX_CustomerOrders_ExternalReference y se traduce con el mapper común.
        var article = await ArticleAsync();
        var reference = "PED-" + StockApiSupport.Unique(8);
        await using (var db = fixture.ApplicationDatabase())
        {
            db.Add(CustomerOrder.Create(Guid.NewGuid(), reference, "Cliente", [new(article.Id, 1m)]));
            await db.SaveChangesAsync();
        }
        await using var second = fixture.ApplicationDatabase();
        second.Add(CustomerOrder.Create(Guid.NewGuid(), reference.ToLowerInvariant(), "Cliente", [new(article.Id, 1m)]));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var problem = MasterDataProblemMapper.Map(error, "POST");
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("DuplicateCode", problem.Code);
        Assert.Equal(new[] { "externalReference" }, problem.Errors!.Keys);
    }

    [Fact]
    public async Task Reader_publishes_the_order_inside_the_executor_after_locking_it()
    {
        // Mínimo de S-18 sin reservas (T012 lo completa con asignaciones y remanentes por bucket).
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var article = await ArticleAsync();
        var id = await CreateAsync(operario, (article.Id, "7"));
        var (_, etag) = await DetailAsync(operario.Client, id);
        var (_, location) = await support.Stock.SeedAsync();
        var receiving = await support.ArticleAsync();
        var (_, principal) = await support.Stock.ActorAsync();

        CustomerOrderSnapshot? snapshot = null;
        CustomerOrderSnapshot? absent = new(Guid.Empty, "", "", [], []);
        await support.Stock.RunAsync(principal, Guid.NewGuid(),
            StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(receiving.Id, location.Id, null, 1m)]),
            async (context, token) =>
            {
                var missing = Guid.NewGuid();
                await context.Locks.DocumentsAsync([new DocumentLock(StockDocuments.CustomerOrder, id),
                    new DocumentLock(StockDocuments.CustomerOrder, missing)], token);
                var reader = context.Services.GetRequiredService<ICustomerOrderReader>();
                snapshot = await reader.ReadAsync(context, id, token);
                absent = await reader.ReadAsync(context, missing, token);
            });

        Assert.Null(absent);
        Assert.NotNull(snapshot);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal("Open", snapshot.State);
        Assert.Equal(etag, EntityTags.Format(snapshot.Version));
        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(article.Id, line.ArticleId);
        Assert.Equal(7m, line.Ordered);
        Assert.Equal(0m, line.Shipped);
        Assert.Equal(7m, line.Pending);
        Assert.Empty(line.ActiveAllocations);
    }

    private async Task<Article> ArticleAsync(bool fractions = false, bool active = true)
    {
        var sku = "S08-" + StockApiSupport.Unique(10);
        var article = await fixture.NewArticleAsync(sku, "Artículo de pedido", "UD", fractions, false);
        if (!active) article.Change(sku, article.Name, article.FamilyId, "UD", fractions, false, false);
        await using var db = fixture.ApplicationDatabase();
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        return article;
    }

    private static object Order(string externalReference, string customerReference, params (Guid ArticleId, string Quantity)[] lines) =>
        new { externalReference, customerReference, lines = lines.Select(line => new { articleId = line.ArticleId, quantity = line.Quantity }) };

    private static async Task<Guid> CreateAsync(MasterDataApi session, params (Guid ArticleId, string Quantity)[] lines)
    {
        using var created = await session.WriteAsync(HttpMethod.Post, Orders,
            Order("PED-" + StockApiSupport.Unique(8), "Cliente", lines));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
    }

    private static async Task<(JsonElement Body, string ETag)> DetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{Orders}/{id:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).Clone(), response.Headers.ETag!.ToString());
    }

    private static async Task<JsonElement> ContextAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{Orders}/{id:D}/context");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.ETag);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"Se esperaba {(int)status} {code} y llegó {(int)response.StatusCode}: {text}");
        var problem = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }
}
