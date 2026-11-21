using System.Text.Json;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Features.Stock.Reservations;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Features.Stock.Testing;

/// <summary>
/// Siembra de existencias para el E2E mientras HU-01 no ofrezca recepciones por HTTP (HU-05 T017,
/// research D-17). <c>POST /api/v1/testing/stock-receipts</c> solo existe si
/// <c>Testing:EnableStockSeeding</c> es true (por defecto false; variable de entorno
/// <c>Testing__EnableStockSeeding</c>) y nunca en el entorno Production. No escribe saldos: ejecuta
/// StockCore (Receive) con el ejecutor común y queda en el diario como cualquier recepción.
/// Se retira cuando HU-01 ofrezca la recepción.
/// </summary>
public static class StockSeedingEndpoints
{
    public const string Option = "Testing:EnableStockSeeding";
    public const string Route = "/testing/stock-receipts";
    public const string DocumentType = "test-receipt";
    public const string Reason = "Preparación de pruebas E2E";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        configuration.GetValue<bool>(Option) && !environment.IsProduction();

    public static RouteGroupBuilder MapStockSeedingEndpoint(this RouteGroupBuilder api)
    {
        api.MapPost(Route, (JsonElement body, HttpContext context, InventoryOperationExecutor executor, CancellationToken token) =>
                SeedAsync(executor, LocationEndpoints.Key(context), StockSeedWrite.Read(body), token))
            .RequireAuthorization("Receiving.Record").WithTags("Testing").ExcludeFromDescription();
        return api;
    }

    private static Task<OperationResponse> SeedAsync(InventoryOperationExecutor executor, Guid key, StockSeedWrite write,
        CancellationToken cancellation)
    {
        // Documento propio por intención, estable en los reintentos; la huella es el cuerpo
        // normalizado del cliente porque OccurredAt lo fija el servidor (como research D-05).
        var document = Guid.NewGuid();
        var request = new OperationRequest(key, "POST", "/api/v1" + Route, "Receiving.Record",
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!Guid.TryParse(write.ArticleId, out var article) || article == Guid.Empty) errors["articleId"] = ["Elige un artículo."];
            if (!Guid.TryParse(write.LocationId, out var location) || location == Guid.Empty) errors["locationId"] = ["Elige una ubicación."];
            var problem = JsonBody.QuantityProblem(write.Quantity, out var quantity);
            if (problem is not null) errors["quantity"] = [problem];
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa los datos de la preparación.", errors);

            // Sin documento de compra ni proveedor: ambos motivos «no aplica» (constitución IV, #7).
            var command = StockCore.Prepare(StockOperation.Receive, new StockCommandContext(
                    new StockDocumentRef(DocumentType, document), DateTimeOffset.UtcNow, Reason,
                    SupportNotApplicableReason: Reason, CounterpartyNotApplicableReason: Reason),
                [new StockAllocation(article, location, write.LotCode, quantity)]);
            return StockCore.Response(await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token));
        }, cancellation);
    }
}

/// <summary>Cuerpo normalizado de la siembra: ids en formato D, lote en mayúsculas y cantidad con tres decimales.</summary>
public sealed record StockSeedWrite(string? ArticleId, string? LocationId, string? LotCode, string? Quantity)
{
    public static StockSeedWrite Read(JsonElement body)
    {
        var properties = JsonBody.Object(body, "articleId", "locationId", "lotCode", "quantity");
        return new(JsonBody.Id(properties, "articleId"), JsonBody.Id(properties, "locationId"),
            JsonBody.Text(properties, "lotCode")?.ToUpperInvariant() is { Length: > 0 } lot ? lot : null,
            JsonBody.Quantity(properties, "quantity"));
    }
}
