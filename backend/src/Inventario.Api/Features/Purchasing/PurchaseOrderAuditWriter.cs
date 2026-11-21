using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Features.Purchasing;

internal static class PurchaseOrderAuditWriter
{
    public static void Record(OperationContext context, Guid orderId, PurchaseAuditAction action, object? before, object? after)
    {
        static string? Json(object? value) => value is null ? null : JsonSerializer.Serialize(value, OperationResponse.JsonOptions);
        context.Database.Add(new PurchaseOrderAudit(orderId, action, context.ActorId, context.OperationId, Json(before), Json(after)));
        // No SaveChanges: the OperationResult principal must be inserted in the same commit.
    }
}
