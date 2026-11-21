using System.Text.Json;
using Inventario.Api.Features.Receiving.Commands;
using Inventario.Api.Features.Receiving.Queries;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Receiving;

public static class ReceivingEndpoints
{
    public static IServiceCollection AddReceiving(this IServiceCollection services)
    {
        services.AddScoped<SaveReceiptDraft>();
        services.AddScoped<ConfirmReceipt>();
        services.AddScoped<CorrectReceipt>();
        services.AddScoped<ReconcileReceiptVoucher>();
        services.AddScoped<ReceiptContingencyScopeCommands>();
        services.AddScoped<ReceiptQueries>();
        return services;
    }

    public static void MapReceivingEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/contingencies/reconcile", (JsonElement body, HttpContext http, ReconcileReceiptVoucher writer, CancellationToken token) =>
            writer.ExecuteAsync(Key(http), body, token)).RequireAuthorization(Inventario.Api.Infrastructure.Identity.PermissionCatalog.ReceivingReconcilePolicy);
        var scopes = api.MapGroup("/contingencies/receiving-scopes").RequireAuthorization("Inventory.Adjust");
        scopes.MapPost("", (JsonElement body, HttpContext http, ReceiptContingencyScopeCommands writer, CancellationToken token) =>
            writer.SaveAsync(Key(http), null, body, null, token));
        scopes.MapPut("/{id:guid}", (Guid id, JsonElement body, HttpContext http, ReceiptContingencyScopeCommands writer, CancellationToken token) =>
            writer.SaveAsync(Key(http), id, body, http.Request.Headers.IfMatch.ToString(), token));
        scopes.MapGet("/{id:guid}", async (Guid id, HttpContext http, ReceiptContingencyScopeCommands writer, CancellationToken token) =>
        { var result = await writer.DetailAsync(id, token); http.Response.Headers.ETag = result.Tag; return Results.Ok(result.Resource); });
        scopes.MapPost("/{id:guid}/reopen", (Guid id, JsonElement body, HttpContext http, ReceiptContingencyScopeCommands writer, CancellationToken token) =>
        {
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any()) throw ReceiptWrite.Invalid("scope", "La reapertura solo admite un objeto vacío.");
            return writer.ReopenAsync(Key(http), id, http.Request.Headers.IfMatch.ToString(), token);
        });
        api.MapPost("/movements/{id:guid}/corrections", (Guid id, JsonElement body, HttpContext http, CorrectReceipt writer, CancellationToken token) =>
            writer.ExecuteAsync(Key(http), id, body, http.Request.Headers.IfMatch.ToString(), token)).RequireAuthorization("Inventory.Adjust");
        var group = api.MapGroup("/receipts").WithTags("Receiving");
        group.MapGet("", (Guid? purchaseOrderId, Guid? articleId, Guid? actorId, string? from, string? to, string? state,
            int? page, int? pageSize, ReceiptQueries queries, CancellationToken token) =>
            queries.ListAsync(purchaseOrderId, articleId, actorId, from, to, state, page ?? 1, pageSize ?? 25, token)).RequireAuthorization("Inventory.Read");
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, ReceiptQueries queries, CancellationToken token) =>
        {
            var (resource, tag) = await queries.DetailAsync(id, token);
            http.Response.Headers.ETag = tag;
            return Results.Ok(resource);
        }).RequireAuthorization("Inventory.Read");
        group.MapGet("/{id:guid}/context", (Guid id, ReceiptQueries queries, CancellationToken token) =>
            queries.ContextAsync(id, token)).RequireAuthorization("Inventory.Read");
        group.MapPost("", (JsonElement body, HttpContext http, SaveReceiptDraft writer, CancellationToken token) =>
            writer.ExecuteAsync(Key(http), null, ReceiptWrite.Read(body), null, token)).RequireAuthorization("Receiving.Record");
        group.MapPut("/{id:guid}", (Guid id, JsonElement body, HttpContext http, SaveReceiptDraft writer, CancellationToken token) =>
            writer.ExecuteAsync(Key(http), id, ReceiptWrite.Read(body), http.Request.Headers.IfMatch.ToString(), token)).RequireAuthorization("Receiving.Record");
        group.MapPost("/{id:guid}/confirm", (Guid id, JsonElement body, HttpContext http, ConfirmReceipt writer, CancellationToken token) =>
        {
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any())
                throw ReceiptWrite.Invalid("receipt", "La confirmación solo admite un objeto vacío.");
            return writer.ExecuteAsync(Key(http), id, http.Request.Headers.IfMatch.ToString(), token);
        }).RequireAuthorization("Receiving.Record");
    }

    private static Guid Key(HttpContext http) => Guid.TryParse(http.Request.Headers["Idempotency-Key"].ToString(), out var id) && id != Guid.Empty
        ? id : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
