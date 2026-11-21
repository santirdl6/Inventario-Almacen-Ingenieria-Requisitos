using System.Text.Json;
using Inventario.Api.Features.Purchasing.Commands;
using Inventario.Api.Features.Purchasing.Queries;
using Inventario.Api.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Inventario.Api.Features.Purchasing;

public static class PurchasingEndpoints
{
    public static RouteGroupBuilder MapPurchasingEndpoints(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/purchase-orders").WithTags("Purchasing");
        orders.MapGet("", (string? q, Guid? supplierId, string? state, string? from, string? to, int? page, int? pageSize,
            PurchaseOrderQueries queries, CancellationToken token) => queries.ListAsync(PurchaseOrderListOptions.Parse(q, supplierId, state, from, to, page, pageSize), token))
            .RequireAuthorization("Inventory.Read").Produces<PurchaseOrderPage>();
        orders.MapGet("/settings", (IOptions<PurchasingOptions> options) => Results.Ok(new { currency = options.Value.Currency }))
            .RequireAuthorization("Inventory.Read");
        orders.MapPost("", (JsonElement body, HttpContext http, PurchaseOrderDraftCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(http), null, PurchaseOrderWrite.Read(body), null, token))
            .RequireAuthorization("Purchasing.Manage").Accepts<PurchaseOrderWrite>("application/json").Produces<PurchaseOrderMutation>(201);
        orders.MapPut("/{id:guid}", (Guid id, JsonElement body, HttpContext http, PurchaseOrderDraftCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(http), id, PurchaseOrderWrite.Read(body), http.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization("Purchasing.Manage").Accepts<PurchaseOrderWrite>("application/json").Produces<PurchaseOrderMutation>();
        orders.MapGet("/{id:guid}", async (Guid id, HttpContext http, PurchaseOrderQueries queries, CancellationToken token) =>
        {
            var result = await queries.DetailAsync(id, token); http.Response.Headers.ETag = result.ETag;
            return Results.Ok(result.Resource);
        }).RequireAuthorization("Inventory.Read").Produces<PurchaseOrderDetail>();
        orders.MapGet("/{id:guid}/context", (Guid id, PurchaseOrderQueries queries, CancellationToken token) => queries.ContextAsync(id, token))
            .RequireAuthorization("Inventory.Read");
        orders.MapGet("/{id:guid}/send-preview", (Guid id, PurchaseOrderSendPreviewQuery query, CancellationToken token) => query.ReadAsync(id, token))
            .RequireAuthorization("Purchasing.Send").Produces<PurchaseSendPreview>();
        orders.MapPost("/{id:guid}/send", (Guid id, JsonElement body, HttpContext http, RequestPurchaseOrderSend command, RetryPurchaseOrderDelivery retry, CancellationToken token) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "retry"
                ? retry.ExecuteAsync(Key(http), id, body, http.Request.Headers.IfMatch.ToString(), token)
                : command.ExecuteAsync(Key(http), id, body, http.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization("Purchasing.Send").Produces<PurchaseSendMutation>(202);
        orders.MapGet("/{id:guid}/delivery", (Guid id, HttpContext http, PurchaseOrderDeliveryQueries query, CancellationToken token) =>
            query.ReadAsync(id, http.User.HasClaim("permission", "Purchasing.Send"), token))
            .RequireAuthorization("Inventory.Read").Produces<PurchaseDeliveryStatus>();
        orders.MapGet("/{id:guid}/delivery/document", async (Guid id, HttpContext http, PurchaseOrderDeliveryQueries query, CancellationToken token) =>
        {
            var document = await query.DocumentAsync(id, token);
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(document.Bytes, document.MediaType, document.Name);
        }).RequireAuthorization("Inventory.Read");
        return api;
    }

    internal static Guid Key(HttpContext http) => Guid.TryParse(http.Request.Headers["Idempotency-Key"].ToString(), out var id)
        && id != Guid.Empty ? id : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
