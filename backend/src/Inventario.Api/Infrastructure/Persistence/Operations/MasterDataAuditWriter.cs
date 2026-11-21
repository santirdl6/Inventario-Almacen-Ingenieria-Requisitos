using System.Text.Json;
using Inventario.Api.Domain.MasterData;

namespace Inventario.Api.Infrastructure.Persistence.Operations;

public static class MasterDataAuditWriter
{
    public static void Record(OperationContext context, MasterDataEntity entity, Guid id, MasterDataAction action,
        object? before, object? after)
    {
        if (action == MasterDataAction.Create && before is not null || action == MasterDataAction.Delete && after is not null)
            throw new InvalidOperationException("La evidencia de alta/borrado tiene un snapshot inexistente.");
        context.Database.Set<MasterDataAudit>().Add(new MasterDataAudit(entity, id, action,
            before is null ? null : JsonSerializer.Serialize(before, OperationResponse.JsonOptions),
            after is null ? null : JsonSerializer.Serialize(after, OperationResponse.JsonOptions),
            context.ActorId, context.OperationId));
    }
}
