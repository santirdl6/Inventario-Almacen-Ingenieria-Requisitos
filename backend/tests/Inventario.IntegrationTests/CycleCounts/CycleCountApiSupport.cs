using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
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

    /// <summary>Errores de validación del problema por campo.</summary>
    public static IReadOnlyCollection<string> ErrorFields(JsonElement problem) =>
        problem.TryGetProperty("errors", out var errors) ? errors.EnumerateObject().Select(field => field.Name).ToList() : [];
}
