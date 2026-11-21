using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.CycleCounts;

/// <summary>
/// Soporte HTTP de las pruebas de HU-04 (T006). Sesiones reales por rol (StockApiSupport), existencias
/// solo con StockCore y conteos solo con los endpoints de /cycle-counts.
/// </summary>
internal static class CycleCountApiSupport
{
    public const string Counts = "/api/v1/cycle-counts";

    public static Task<HttpResponseMessage> PostAsync(MasterDataApi session, Guid? key, params Guid[] locations) =>
        session.WriteAsync(HttpMethod.Post, Counts, new { locationIds = locations }, key);

    /// <summary>Crea el borrador y devuelve su id y su ETag.</summary>
    public static async Task<(Guid Id, string ETag)> CreateAsync(MasterDataApi session, params Guid[] locations)
    {
        using var created = await PostAsync(session, null, locations);
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == created.StatusCode, text);
        return (JsonDocument.Parse(text).RootElement.GetProperty("count").GetProperty("id").GetGuid(), created.Headers.ETag!.ToString());
    }

    public static Task<HttpResponseMessage> PutAsync(MasterDataApi session, Guid id, string? etag, params Guid[] locations) =>
        session.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}", new { locationIds = locations }, version: etag);

    public static Task<HttpResponseMessage> StartAsync(MasterDataApi session, Guid id, string? etag, Guid? key = null) =>
        session.WriteAsync(HttpMethod.Post, $"{Counts}/{id:D}/start", new { }, key, etag);

    /// <summary>Crea e inicia un conteo; devuelve su id, el detalle y el ETag tras iniciar.</summary>
    public static async Task<(Guid Id, JsonElement Detail, string ETag)> StartedAsync(MasterDataApi session, params Guid[] locations)
    {
        var (id, etag) = await CreateAsync(session, locations);
        using var started = await StartAsync(session, id, etag);
        var text = await started.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == started.StatusCode, text);
        return (id, JsonDocument.Parse(text).RootElement.GetProperty("count").Clone(), started.Headers.ETag!.ToString());
    }

    public static Task<HttpResponseMessage> LinesAsync(MasterDataApi session, Guid id, string? etag, object body, Guid? key = null) =>
        session.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}/lines", body, key, etag);

    public static Task<HttpResponseMessage> RemoveLineAsync(MasterDataApi session, Guid id, Guid line, string? etag, Guid? key = null) =>
        session.WriteAsync(HttpMethod.Delete, $"{Counts}/{id:D}/lines/{line:D}", new { }, key, etag);

    public static Task<HttpResponseMessage> SubmitAsync(MasterDataApi session, Guid id, string? etag, Guid? key = null) =>
        session.WriteAsync(HttpMethod.Post, $"{Counts}/{id:D}/submit", new { }, key, etag);

    /// <summary>Contado de líneas existentes: (lineId, countedPhysical).</summary>
    public static object Counted(params (Guid Line, string Quantity)[] lines) => new
    {
        counted = lines.Select(line => new { lineId = line.Line, countedPhysical = line.Quantity }),
        found = Array.Empty<object>()
    };

    /// <summary>Escritura que debe responder 200: devuelve el nuevo ETag.</summary>
    public static async Task<string> OkAsync(Task<HttpResponseMessage> write)
    {
        using var response = await write;
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.ToString();
    }

    public static Task<HttpResponseMessage> ReviewAsync(MasterDataApi session, Guid id, string? etag,
        params (Guid Line, string? Reason)[] lines) =>
        session.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}/review",
            new { lines = lines.Select(line => new { lineId = line.Line, reason = line.Reason }) }, version: etag);

    public static Task<HttpResponseMessage> ConfirmAsync(MasterDataApi session, Guid id, string? etag, Guid? key = null) =>
        session.WriteAsync(HttpMethod.Post, $"{Counts}/{id:D}/confirm", new { }, key, etag);

    public static Task<HttpResponseMessage> CancelAsync(MasterDataApi session, Guid id, string? etag, string? reason, Guid? key = null) =>
        session.WriteAsync(HttpMethod.Post, $"{Counts}/{id:D}/cancel", new { reason }, key, etag);

    /// <summary>
    /// Conteo pendiente de aprobación: crea, inicia, registra el contado de cada artículo esperado en
    /// la primera ubicación y envía. Devuelve el id, el detalle y el ETag tras enviar.
    /// </summary>
    public static async Task<(Guid Id, JsonElement Detail, string ETag)> AwaitingAsync(MasterDataApi operario, Guid location,
        params (Guid Article, string Counted)[] counted)
    {
        var (id, detail, etag) = await StartedAsync(operario, location);
        etag = await OkAsync(LinesAsync(operario, id, etag, Counted(counted.Select(item =>
            (Line(detail, item.Article, location).GetProperty("id").GetGuid(), item.Counted)).ToArray())));
        etag = await OkAsync(SubmitAsync(operario, id, etag));
        var (current, _) = await DetailAsync(operario.Client, id);
        return (id, current, etag);
    }

    /// <summary>Línea del detalle por artículo y ubicación.</summary>
    public static JsonElement Line(JsonElement detail, Guid article, Guid location) =>
        detail.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("articleId").GetGuid() == article
            && line.GetProperty("locationId").GetGuid() == location);

    /// <summary>GET /cycle-counts/{id}: cuerpo y ETag.</summary>
    public static async Task<(JsonElement Body, string ETag)> DetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{Counts}/{id:D}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, text);
        return (JsonDocument.Parse(text).RootElement.Clone(), response.Headers.ETag!.ToString());
    }

    public static async Task<JsonElement> ContextAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"{Counts}/{id:D}/context")).Clone();

    /// <summary>
    /// C-14: invariantes 1–5 y 7 de data-model tras confirmar. Físico = contado por bucket; Σ entradas
    /// del ajuste = diferencia por bucket y sin diferencias sin movimiento; sin filas congeladas en el
    /// conteo cerrado y como mucho una por ubicación; reservado ≤ físico; diario = saldo.
    /// </summary>
    public static async Task AssertConfirmedInvariantsAsync(IdentityFixture fixture, Inventario.IntegrationTests.Stock.StockApiSupport support,
        Guid id)
    {
        await using var database = fixture.ApplicationDatabase();
        var count = await database.Set<CycleCount>().AsNoTracking().Include(value => value.Locations).Include(value => value.Lines)
            .SingleAsync(value => value.Id == id);
        Assert.Equal(CycleCountState.Confirmed, count.State);
        var movements = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
            .Where(value => value.SourceDocumentType == "cycle-count" && value.SourceDocumentId == id).ToListAsync();
        var withDifference = count.Lines.Where(line => line.CountedPhysical != line.TheoreticalPhysical).ToList();
        if (withDifference.Count == 0)
        {
            Assert.Empty(movements);
            Assert.Null(count.AdjustmentMovementId);
        }
        else
        {
            var movement = Assert.Single(movements);
            Assert.Equal(count.AdjustmentMovementId, movement.Id);
            Assert.Equal(MovementKind.Adjustment, movement.Kind);
            Assert.Equal(count.CountCode, movement.SupportReference);
            Assert.Null(movement.CounterpartyReference);
            Assert.False(string.IsNullOrWhiteSpace(movement.CounterpartyNotApplicableReason));
            Assert.Equal(withDifference.Count, movement.Entries.Count);
        }
        var lots = await database.Set<Lot>().AsNoTracking().Where(lot => count.Lines.Select(line => line.ArticleId).Contains(lot.ArticleId))
            .ToListAsync();
        foreach (var line in count.Lines)
        {
            var lot = lots.SingleOrDefault(value => value.ArticleId == line.ArticleId && value.Code == line.LotCode);
            var balance = lot is null ? null : await database.Set<StockBalance>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.ArticleId == line.ArticleId && value.LocationId == line.LocationId && value.LotId == lot.Id);
            Assert.Equal(line.CountedPhysical, balance?.Physical ?? 0m);
            Assert.True((balance?.Reserved ?? 0m) <= (balance?.Physical ?? 0m));
            var entries = movements.SelectMany(movement => movement.Entries)
                .Where(entry => entry.ArticleId == line.ArticleId && entry.LocationId == line.LocationId && entry.LotId == lot?.Id)
                .Sum(entry => entry.PhysicalDelta);
            Assert.Equal(line.CountedPhysical - line.TheoreticalPhysical, entries);
            await support.AssertCommitmentsMatchBalancesAsync(line.ArticleId, line.LocationId);
        }
        Assert.DoesNotContain(count.Locations, location => location.IsFrozen);
        var scope = count.Locations.Select(location => location.LocationId).ToList();
        Assert.All(await database.Set<CountLocation>().AsNoTracking().Where(value => scope.Contains(value.LocationId) && value.IsFrozen)
            .GroupBy(value => value.LocationId).Select(group => group.Count()).ToListAsync(), frozen => Assert.Equal(1, frozen));
    }

    /// <summary>Errores de validación del problema por campo.</summary>
    public static IReadOnlyCollection<string> ErrorFields(JsonElement problem) =>
        problem.TryGetProperty("errors", out var errors) ? errors.EnumerateObject().Select(field => field.Name).ToList() : [];
}
