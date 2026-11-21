using System.Net;
using System.Net.Http.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T011 (C-17, C-18; FR-026–FR-028, SC-004; US6). Replay idéntico de las nueve escrituras sin un
// segundo ajuste; misma clave con otro cuerpo → 409 IdempotencyConflict; If-Match ausente u obsoleto →
// 428/412; fallo inducido tras guardar en la confirmación → nada aplicado y la misma clave confirma una
// vez, coherente con GET /operations/{id}; matriz 401/403/400 por ruta (Operario: Counts.Record;
// ResponsableAlmacen: Counts.Record + Inventory.Adjust).
public sealed class CycleCountReliabilityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task C17_every_write_replays_identically_and_rejects_another_body_or_a_missing_or_stale_version()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var u2 = await support.LocationAsync("U2-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        var (c, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 10m);

        // 1. Crear.
        var key = Guid.NewGuid();
        var (created, createdText) = await SendAsync(() => PostAsync(operario, key, u1, u2), HttpStatusCode.Created);
        var id = System.Text.Json.JsonDocument.Parse(createdText).RootElement.GetProperty("count").GetProperty("id").GetGuid();
        await ReplayAsync(() => PostAsync(operario, key, u2, u1), createdText);
        await ConflictAsync(() => PostAsync(operario, key, u1));
        var etag = created;

        // 2. Editar el alcance.
        key = Guid.NewGuid();
        await PreconditionsAsync(() => PutAsync(operario, id, null, u1), stale: null);
        var (edited, editedText) = await SendAsync(() => PutKeyedAsync(operario, id, etag, key, u1), HttpStatusCode.OK);
        await ReplayAsync(() => PutKeyedAsync(operario, id, etag, key, u1), editedText);
        await ConflictAsync(() => PutKeyedAsync(operario, id, edited, key, u1, u2));
        var stale = etag;
        etag = edited;

        // 3. Iniciar.
        key = Guid.NewGuid();
        await PreconditionsAsync(() => StartAsync(operario, id, null), () => StartAsync(operario, id, stale));
        var (started, startedText) = await SendAsync(() => StartAsync(operario, id, etag, key), HttpStatusCode.OK);
        await ReplayAsync(() => StartAsync(operario, id, etag, key), startedText);
        stale = etag;
        etag = started;
        var (detail, _) = await DetailAsync(operario.Client, id);
        var lineA = Line(detail, a, u1).GetProperty("id").GetGuid();

        // 4. Contar y hallazgo.
        key = Guid.NewGuid();
        object Lines(string counted) => new
        {
            counted = new[] { new { lineId = lineA, countedPhysical = counted } },
            found = new[] { new { articleId = c, locationId = u1, lotCode = (string?)null, countedPhysical = "3" } }
        };
        await PreconditionsAsync(() => LinesAsync(operario, id, null, Lines("8")), () => LinesAsync(operario, id, stale, Lines("8")));
        var (counted, countedText) = await SendAsync(() => LinesAsync(operario, id, etag, Lines("8.0"), key), HttpStatusCode.OK);
        await ReplayAsync(() => LinesAsync(operario, id, etag, Lines("8"), key), countedText);
        await ConflictAsync(() => LinesAsync(operario, id, counted, Lines("7"), key));
        stale = etag;
        etag = counted;

        // 5. Retirar el hallazgo.
        var foundC = Line((await DetailAsync(operario.Client, id)).Body, c, u1).GetProperty("id").GetGuid();
        key = Guid.NewGuid();
        await PreconditionsAsync(() => RemoveLineAsync(operario, id, foundC, null), () => RemoveLineAsync(operario, id, foundC, stale));
        string removed;
        using (var response = await RemoveLineAsync(operario, id, foundC, etag, key))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            removed = response.Headers.ETag!.ToString();
        }
        using (var replay = await RemoveLineAsync(operario, id, foundC, etag, key))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.Equal(removed, replay.Headers.ETag!.ToString());
        }
        stale = etag;
        etag = removed;

        // 6. Enviar.
        key = Guid.NewGuid();
        await PreconditionsAsync(() => SubmitAsync(operario, id, null), () => SubmitAsync(operario, id, stale));
        var (submitted, submittedText) = await SendAsync(() => SubmitAsync(operario, id, etag, key), HttpStatusCode.OK);
        await ReplayAsync(() => SubmitAsync(operario, id, etag, key), submittedText);
        stale = etag;
        etag = submitted;

        // 7. Revisar.
        key = Guid.NewGuid();
        Task<HttpResponseMessage> Review(string? version, string reason) => responsable.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}/review",
            new { lines = new[] { new { lineId = lineA, reason } } }, key, version);
        await PreconditionsAsync(() => ReviewAsync(responsable, id, null, (lineA, "Rotura")), () => ReviewAsync(responsable, id, stale, (lineA, "Rotura")));
        var (reviewed, reviewedText) = await SendAsync(() => Review(etag, "Rotura"), HttpStatusCode.OK);
        await ReplayAsync(() => Review(etag, "Rotura"), reviewedText);
        await ConflictAsync(() => Review(reviewed, "Otra causa"));
        stale = etag;
        etag = reviewed;

        // 8. Confirmar: el replay no aplica un segundo ajuste.
        key = Guid.NewGuid();
        await PreconditionsAsync(() => ConfirmAsync(responsable, id, null), () => ConfirmAsync(responsable, id, stale));
        var (_, confirmedText) = await SendAsync(() => ConfirmAsync(responsable, id, etag, key), HttpStatusCode.OK);
        await ReplayAsync(() => ConfirmAsync(responsable, id, etag, key), confirmedText);
        await using (var database = fixture.ApplicationDatabase())
            Assert.Equal(1, await database.Set<StockMovement>().CountAsync(value => value.SourceDocumentType == "cycle-count" && value.SourceDocumentId == id));
        Assert.Equal(8m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
        await AssertConfirmedInvariantsAsync(fixture, support, id);

        // 9. Cancelar (otro conteo).
        var (other, otherTag) = await CreateAsync(operario, u2);
        key = Guid.NewGuid();
        await PreconditionsAsync(() => CancelAsync(responsable, other, null, "Repetido"), stale: null);
        var (_, cancelledText) = await SendAsync(() => CancelAsync(responsable, other, otherTag, "Repetido", key), HttpStatusCode.OK);
        await ReplayAsync(() => CancelAsync(responsable, other, otherTag, "Repetido", key), cancelledText);
        await ConflictAsync(() => CancelAsync(responsable, other, otherTag, "Otro motivo", key));
    }

    [Fact]
    public async Task C18_a_failure_after_saving_the_confirmation_applies_nothing_and_the_same_key_confirms_once()
    {
        var failure = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, failure);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 10m);
        var (id, detail, etag) = await AwaitingAsync(operario, u1, (a, "8"));
        etag = await OkAsync(ReviewAsync(responsable, id, etag, (Line(detail, a, u1).GetProperty("id").GetGuid(), "Rotura")));

        var key = Guid.NewGuid();
        failure.Arm();
        using (var failed = await ConfirmAsync(responsable, id, etag, key))
            Assert.False(failed.IsSuccessStatusCode);
        // Nada aplicado: sin resultado, sin movimiento, sigue pendiente y congelado, físico intacto.
        await using (var database = fixture.ApplicationDatabase())
        {
            Assert.False(await database.Set<OperationResult>().AnyAsync(value => value.OperationId == key));
            Assert.False(await database.Set<StockMovement>().AnyAsync(value => value.SourceDocumentId == id));
        }
        using (var operation = await responsable.Client.GetAsync($"/api/v1/operations/{key:D}"))
            Assert.Equal(HttpStatusCode.NotFound, operation.StatusCode);
        var (pending, pendingTag) = await DetailAsync(operario.Client, id);
        Assert.Equal(etag, pendingTag);
        Assert.Equal("AwaitingApproval", pending.GetProperty("state").GetString());
        Assert.All(pending.GetProperty("locations").EnumerateArray(), location => Assert.True(location.GetProperty("isFrozen").GetBoolean()));
        Assert.Equal(10m, (await support.Stock.BalanceAsync(a, u1))!.Physical);

        // La misma clave confirma una sola vez y la operación queda comprobable.
        var (_, text) = await SendAsync(() => ConfirmAsync(responsable, id, etag, key), HttpStatusCode.OK);
        await ReplayAsync(() => ConfirmAsync(responsable, id, etag, key), text);
        using (var operation = await responsable.Client.GetAsync($"/api/v1/operations/{key:D}"))
            Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
        await using (var database = fixture.ApplicationDatabase())
            Assert.Equal(1, await database.Set<StockMovement>().CountAsync(value => value.SourceDocumentId == id));
        Assert.Equal(8m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
        await AssertConfirmedInvariantsAsync(fixture, support, id);
    }

    public static TheoryData<string, string, string> Writes() => new()
    {
        { "POST", "", "Counts.Record" },
        { "PUT", "/{id}", "Counts.Record" },
        { "POST", "/{id}/start", "Counts.Record" },
        { "PUT", "/{id}/lines", "Counts.Record" },
        { "DELETE", "/{id}/lines/{line}", "Counts.Record" },
        { "POST", "/{id}/submit", "Counts.Record" },
        { "PUT", "/{id}/review", "Inventory.Adjust" },
        { "POST", "/{id}/confirm", "Inventory.Adjust" },
        { "POST", "/{id}/cancel", "Inventory.Adjust" }
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Each_write_needs_a_session_antiforgery_an_idempotency_key_and_its_permission(string method, string route, string permission)
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var path = Counts + route.Replace("{id}", Guid.NewGuid().ToString("D")).Replace("{line}", Guid.NewGuid().ToString("D"));
        var verb = new HttpMethod(method);
        // Cuerpo inválido a propósito: 403 antes de 422.
        var body = new { locationIds = Array.Empty<Guid>() };

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using (var request = new HttpRequestMessage(verb, path) { Content = JsonContent.Create(body) })
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(request)).StatusCode);
        }

        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var authorized = permission == "Counts.Record" ? operario : responsable;
        using (var request = new HttpRequestMessage(verb, path) { Content = JsonContent.Create(body) })
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var withoutToken = await authorized.Client.SendAsync(request);
            await StockApiSupport.ProblemAsync(withoutToken, HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        }
        using (var request = new HttpRequestMessage(verb, path) { Content = JsonContent.Create(body) })
        {
            request.Headers.Add("X-XSRF-TOKEN", authorized.Token);
            Assert.Equal(HttpStatusCode.BadRequest, (await authorized.Client.SendAsync(request)).StatusCode);
        }

        // Sin el permiso de la fila: GestorMaestros no tiene Counts.Record; Operario no tiene Inventory.Adjust.
        var forbidden = permission == "Counts.Record" ? await support.SessionAsync("GestorMaestros") : operario;
        using (var response = await forbidden.WriteAsync(verb, path, body))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Con el permiso, la petición pasa la autorización (422 por el cuerpo o 404 por el conteo inexistente).
        using (var response = await authorized.WriteAsync(verb, path, body, version: "\"AAAAAAAAAAA=\""))
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound });
    }

    [Fact]
    public async Task Reads_need_a_session_and_inventory_read_and_responsable_records_counts()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var id = Guid.NewGuid();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var administrador = await support.SessionAsync("Administrador");
        foreach (var path in new[] { Counts, $"{Counts}/{id:D}", $"{Counts}/{id:D}/context" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await administrador.Client.GetAsync(path)).StatusCode);
        }
        // ResponsableAlmacen también tiene Counts.Record: crea e inicia.
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        await StartedAsync(responsable, u1);
    }

    private static async Task<(string ETag, string Text)> SendAsync(Func<Task<HttpResponseMessage>> send, HttpStatusCode expected)
    {
        using var response = await send();
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, text);
        return (response.Headers.ETag?.ToString() ?? "", text);
    }

    private static async Task ReplayAsync(Func<Task<HttpResponseMessage>> send, string expected)
    {
        using var response = await send();
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    private static async Task ConflictAsync(Func<Task<HttpResponseMessage>> send)
    {
        using var response = await send();
        await StockApiSupport.ProblemAsync(response, HttpStatusCode.Conflict, "IdempotencyConflict");
    }

    private static async Task PreconditionsAsync(Func<Task<HttpResponseMessage>> missing, Func<Task<HttpResponseMessage>>? stale)
    {
        using (var response = await missing())
            Assert.Equal((HttpStatusCode)428, response.StatusCode);
        if (stale is null) return;
        using (var response = await stale())
            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PutKeyedAsync(MasterDataApi session, Guid id, string? etag, Guid key, params Guid[] locations) =>
        session.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}", new { locationIds = locations }, key, etag);
}
