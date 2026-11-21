using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Locations;
using Inventario.IntegrationTests.Stock;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T006 (C-03; FR-001, US1.1; research D-02, D-10–D-12). Borrador: crear y editar el alcance sin
// congelar; lecturas de detalle (ETag), contexto (countVersion) y lista; ubicación inactiva, repetida o
// inexistente → 422; sin Counts.Record → 403; If-Match ausente o distinto → 428/412; replay idéntico.
public sealed class CycleCountDraftTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_draft_is_created_read_and_edited_without_freezing_its_scope()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var l1 = await support.LocationAsync("C01-" + StockApiSupport.Unique(6));
        var l2 = await support.LocationAsync("C02-" + StockApiSupport.Unique(6));
        var l3 = await support.LocationAsync("C03-" + StockApiSupport.Unique(6));

        var key = Guid.NewGuid();
        using var created = await PostAsync(operario, key, l2, l1);
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == created.StatusCode, text);
        var body = JsonDocument.Parse(text).RootElement;
        var id = body.GetProperty("count").GetProperty("id").GetGuid();
        Assert.Equal($"{Counts}/{id:D}", created.Headers.Location!.ToString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("operationId").GetGuid());
        var etag = created.Headers.ETag!.ToString();

        var (detail, detailTag) = await DetailAsync(operario.Client, id);
        Assert.Equal(etag, detailTag);
        Assert.Equal("Draft", detail.GetProperty("state").GetString());
        Assert.Matches("^CC-[0-9A-F]{12}$", detail.GetProperty("countCode").GetString());
        Assert.Equal(id.ToString("N")[..12].ToUpperInvariant(), detail.GetProperty("countCode").GetString()![3..]);
        Assert.Equal(new[] { l1, l2 }.Order(), detail.GetProperty("locations").EnumerateArray()
            .Select(location => location.GetProperty("locationId").GetGuid()));
        Assert.All(detail.GetProperty("locations").EnumerateArray(), location => Assert.False(location.GetProperty("isFrozen").GetBoolean()));
        Assert.Empty(detail.GetProperty("lines").EnumerateArray());

        // Contexto sin ETag, con la versión del detalle y el código de cada ubicación (D-12).
        var context = await ContextAsync(operario.Client, id);
        Assert.Equal(etag, context.GetProperty("countVersion").GetString());
        Assert.Equal(2, context.GetProperty("locations").GetArrayLength());
        Assert.All(context.GetProperty("locations").EnumerateArray(), location => Assert.True(location.GetProperty("isActive").GetBoolean()));

        // Lista paginada filtrada por estado.
        var list = await operario.Client.GetFromJsonAsync<JsonElement>($"{Counts}?state=Draft&pageSize=100");
        var item = list.GetProperty("items").EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == id);
        Assert.Equal(2, item.GetProperty("locationIds").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("startedAt").ValueKind);

        // Replay con la misma clave y la misma intención (el orden de las ubicaciones no cambia la intención).
        using (var replay = await PostAsync(operario, key, l1, l2))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(text, await replay.Content.ReadAsStringAsync());
        }
        using (var conflict = await PostAsync(operario, key, l1))
            await StockApiSupport.ProblemAsync(conflict, HttpStatusCode.Conflict, "IdempotencyConflict");

        // Editar el alcance: If-Match obligatorio y vigente.
        using (var missing = await PutAsync(operario, id, null, l2, l3))
            Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using (var edited = await PutAsync(operario, id, etag, l2, l3))
            Assert.True(HttpStatusCode.OK == edited.StatusCode, await edited.Content.ReadAsStringAsync());
        using (var stale = await PutAsync(operario, id, etag, l3))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var (after, afterTag) = await DetailAsync(operario.Client, id);
        Assert.NotEqual(etag, afterTag);
        Assert.Equal(new[] { l2, l3 }.Order(), after.GetProperty("locations").EnumerateArray()
            .Select(location => location.GetProperty("locationId").GetGuid()));

        // Un borrador no congela (US3.5): la ubicación no está en un conteo abierto.
        var (location, _) = await LocationsTestSupport.GetAsync(operario, l2);
        Assert.False(location.GetProperty("inOpenCount").GetBoolean());
    }

    [Fact]
    public async Task Invalid_scopes_missing_counts_and_missing_permission_are_rejected()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var active = await support.LocationAsync("C04-" + StockApiSupport.Unique(6));
        var inactive = await support.LocationAsync("C05-" + StockApiSupport.Unique(6));
        await StockApiSupport.DeactivateLocationAsync(operario, inactive);

        using (var forbidden = await PostAsync(gestor, null, active))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var off = await PostAsync(operario, null, active, inactive))
            Assert.Contains("locationIds[1]", ErrorFields(await StockApiSupport.ProblemAsync(off, HttpStatusCode.UnprocessableEntity,
                "ValidationFailed")));
        using (var unknown = await PostAsync(operario, null, Guid.NewGuid()))
            Assert.Contains("locationIds[0]", ErrorFields(await StockApiSupport.ProblemAsync(unknown, HttpStatusCode.UnprocessableEntity,
                "ValidationFailed")));
        using (var repeated = await PostAsync(operario, null, active, active))
            Assert.Contains("locationIds", ErrorFields(await StockApiSupport.ProblemAsync(repeated, HttpStatusCode.UnprocessableEntity,
                "ValidationFailed")));
        using (var empty = await PostAsync(operario, null))
            await StockApiSupport.ProblemAsync(empty, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var tooMany = await PostAsync(operario, null, Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray()))
            await StockApiSupport.ProblemAsync(tooMany, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var extra = await operario.WriteAsync(HttpMethod.Post, Counts, new { locationIds = new[] { active }, name = "x" }))
            await StockApiSupport.ProblemAsync(extra, HttpStatusCode.UnprocessableEntity, "ValidationFailed");

        using (var missing = await PutAsync(operario, Guid.NewGuid(), "\"AAAAAAAAAAA=\"", active))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var detail = await operario.Client.GetAsync($"{Counts}/{Guid.NewGuid():D}"))
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using (var page = await operario.Client.GetAsync($"{Counts}?page=0"))
            await StockApiSupport.ProblemAsync(page, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var state = await operario.Client.GetAsync($"{Counts}?state=Open"))
            await StockApiSupport.ProblemAsync(state, HttpStatusCode.UnprocessableEntity, "ValidationFailed");

        // Nada creado con la ubicación activa por los rechazos anteriores.
        var list = await operario.Client.GetFromJsonAsync<JsonElement>($"{Counts}?pageSize=100");
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), value =>
            value.GetProperty("locationIds").EnumerateArray().Any(location => location.GetGuid() == active));
    }
}
