using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving.Commands;

public sealed class ReceiptContingencyScopeCommands(InventoryOperationExecutor executor, InventoryDbContext db)
{
    private sealed record Input(Guid[]? PurchaseOrderIds, Guid[]? LocationIds, string[]? ExpectedVouchers,
        Difference[]? Differences, string? JournalEvidence, string? StockEvidence, string? PurchaseEvidence);
    private sealed record Difference(Guid Id, string? Description, bool Resolved, string? ResolutionReference);
    private sealed record ScopeData(Guid[] PurchaseOrderIds, Guid[] LocationIds, string[] ExpectedVouchers,
        Difference[] Differences, string? JournalEvidence, string? StockEvidence, string? PurchaseEvidence);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public Task<OperationResponse> SaveAsync(Guid key, Guid? id, JsonElement input, string? tag, CancellationToken token)
    {
        Input value;
        try { value = input.Deserialize<Input>(Json)!; }
        catch (JsonException) { throw ReceiptWrite.Invalid("scope", "Corrige los datos del alcance."); }
        if (value is null || value.PurchaseOrderIds is not { Length: > 0 } || value.LocationIds is not { Length: > 0 } ||
            value.ExpectedVouchers is not { Length: > 0 } || value.PurchaseOrderIds.Contains(Guid.Empty) || value.LocationIds.Contains(Guid.Empty))
            throw ReceiptWrite.Invalid("scope", "Indica compras, ubicaciones y comprobantes esperados.");
        var vouchers = value.ExpectedVouchers.Select(ReconcileReceiptVoucher.NormalizeVoucher).ToArray();
        var differences = value.Differences ?? [];
        if (vouchers.Distinct(StringComparer.Ordinal).Count() != vouchers.Length || differences.Any(item => item is null || item.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 500 ||
                item.ResolutionReference?.Length > 500 || item.Resolved && string.IsNullOrWhiteSpace(item.ResolutionReference)) ||
            differences.Select(item => item.Id).Distinct().Count() != differences.Length)
            throw ReceiptWrite.Invalid("scope", "Comprueba los comprobantes y las diferencias declaradas.");
        static string? Evidence(string? value)
        {
            value = value?.Trim();
            if (value?.Length > 500 || value?.Any(char.IsControl) == true) throw ReceiptWrite.Invalid("evidence", "La evidencia no es válida.");
            return string.IsNullOrEmpty(value) ? null : value;
        }
        var normalized = new ScopeData(value.PurchaseOrderIds.Distinct().Order().ToArray(), value.LocationIds.Distinct().Order().ToArray(),
            vouchers.Order(StringComparer.Ordinal).ToArray(), differences.OrderBy(item => item.Id).ToArray(),
            Evidence(value.JournalEvidence), Evidence(value.StockEvidence), Evidence(value.PurchaseEvidence));
        var scopeId = id ?? Guid.NewGuid(); var resource = $"/api/v1/contingencies/receiving-scopes/{scopeId:D}";
        return executor.ExecuteAsync(new(key, id is null ? "POST" : "PUT", id is null ? "/api/v1/contingencies/receiving-scopes" : resource,
            "Inventory.Adjust", JsonSerializer.SerializeToElement(normalized, OperationResponse.JsonOptions)), async (context, ct) =>
        {
            await context.Locks.DocumentsAsync([new("contingency-scope", scopeId)], ct);
            var scope = id is null ? null : await context.Database.Set<ReceiptContingencyScope>().SingleOrDefaultAsync(value => value.Id == scopeId, ct);
            if (id is not null && scope is null) throw new InventoryProblemException(404, "NotFound", "El alcance no existe.");
            if (scope is not null)
            {
                EntityTags.RequireMatch(tag, scope.RevisionStamp.ToByteArray());
                if (scope.State != ReceiptContingencyScopeState.Suspended) throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La reapertura ya fue autorizada.");
            }
            var created = scope is null;
            scope ??= new ReceiptContingencyScope(scopeId, Serialize(normalized.PurchaseOrderIds), Serialize(normalized.LocationIds), Serialize(normalized.ExpectedVouchers), context.ActorId);
            scope.Revise(Serialize(normalized.PurchaseOrderIds), Serialize(normalized.LocationIds), Serialize(normalized.ExpectedVouchers), Serialize(normalized.Differences),
                normalized.JournalEvidence, normalized.StockEvidence, normalized.PurchaseEvidence);
            if (created) context.Database.Add(scope);
            var headers = new Dictionary<string, string> { ["ETag"] = Tag(scope) };
            if (created) headers["Location"] = resource;
            return OperationResponse.Json(created ? 201 : 200, new { operationId = key, resource = Resource(scope) }, scope.Id, headers);
        }, token);
    }

    public Task<OperationResponse> ReopenAsync(Guid key, Guid id, string? tag, CancellationToken token) =>
        executor.ExecuteAsync(new(key, "POST", $"/api/v1/contingencies/receiving-scopes/{id:D}/reopen", "Inventory.Adjust",
            JsonSerializer.SerializeToElement(new { })), async (context, ct) =>
        {
            await context.Locks.DocumentsAsync([new("contingency-scope", id)], ct);
            var scope = await context.Database.Set<ReceiptContingencyScope>().SingleOrDefaultAsync(value => value.Id == id, ct)
                ?? throw new InventoryProblemException(404, "NotFound", "El alcance no existe.");
            if (scope.State != ReceiptContingencyScopeState.Suspended) throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La reapertura ya fue autorizada.");
            EntityTags.RequireMatch(tag, scope.RevisionStamp.ToByteArray());
            var vouchers = Parse<string[]>(scope.ExpectedVouchers); var purchases = Parse<Guid[]>(scope.PurchaseOrders).ToHashSet();
            var locations = Parse<Guid[]>(scope.Locations).ToHashSet(); var differences = Parse<Difference[]>(scope.Differences);
            if (vouchers.Length == 0 || differences.Any(value => !value.Resolved || string.IsNullOrWhiteSpace(value.ResolutionReference)) ||
                string.IsNullOrWhiteSpace(scope.JournalEvidence) || string.IsNullOrWhiteSpace(scope.StockEvidence) || string.IsNullOrWhiteSpace(scope.PurchaseEvidence))
                throw NotReconciled();
            var references = await context.Database.Set<ContingencyReference>().Where(value => vouchers.Contains(value.VoucherId)).ToListAsync(ct);
            if (references.Count != vouchers.Length) throw NotReconciled();
            var receiptIds = references.Select(value => value.ReceiptId).ToArray();
            var receipts = await ReceiptDocument.Graph(context.Database).Where(value => receiptIds.Contains(value.Id)).ToListAsync(ct);
            if (receipts.Count != vouchers.Length || receipts.Any(value => value.State != ReceiptState.Confirmed ||
                    !purchases.Contains(value.PurchaseOrderId) || value.Lines.SelectMany(line => line.Allocations).Any(allocation => !locations.Contains(allocation.LocationId))))
                throw NotReconciled();
            scope.Authorize(context.ActorId, context.OperationId, DateTimeOffset.UtcNow);
            return OperationResponse.Json(200, new { operationId = key, resource = Resource(scope) }, scope.Id,
                new Dictionary<string, string> { ["ETag"] = Tag(scope) });
        }, token);

    public async Task<(object Resource, string Tag)> DetailAsync(Guid id, CancellationToken token)
    {
        var scope = await db.Set<ReceiptContingencyScope>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, token)
            ?? throw new InventoryProblemException(404, "NotFound", "El alcance no existe.");
        return (Resource(scope), Tag(scope));
    }
    private static InventoryProblemException NotReconciled() => new(409, "ContingencyScopeNotReconciled", "Faltan comprobantes, evidencia de conciliación o diferencias por resolver.");
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, OperationResponse.JsonOptions);
    private static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, OperationResponse.JsonOptions)!;
    private static string Tag(ReceiptContingencyScope value) => EntityTags.Format(value.RevisionStamp.ToByteArray());
    private static object Resource(ReceiptContingencyScope value) => new { value.Id, state = value.State.ToString(),
        purchaseOrderIds = Parse<Guid[]>(value.PurchaseOrders), locationIds = Parse<Guid[]>(value.Locations), expectedVouchers = Parse<string[]>(value.ExpectedVouchers),
        differences = Parse<Difference[]>(value.Differences), value.JournalEvidence, value.StockEvidence, value.PurchaseEvidence,
        value.CreatedBy, value.CreatedAt, value.AuthorizedBy, value.AuthorizedAt, value.AuthorizationOperationId };
}
