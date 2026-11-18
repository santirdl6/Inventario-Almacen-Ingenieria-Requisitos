using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Commands;

public sealed record FamilyDto(Guid Id, string Code, string Name, bool IsActive)
{
    public static FamilyDto From(Family family) => new(family.Id, family.Code, family.Name, family.IsActive);
}

public sealed record FamilyMutation(Guid OperationId, FamilyDto Resource);

public sealed record FamilyWrite([property: Required, StringLength(64)] string? Code,
    [property: Required, StringLength(200)] string? Name, bool? IsActive)
{
    public static FamilyWrite Read(JsonElement body, bool create)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        foreach (var property in body.EnumerateObject())
            if (property.Name is not ("code" or "name" or "isActive"))
                throw InventoryProblemException.Validation(property.Name, "Este campo no es editable en datos maestros.");
        FamilyWrite value;
        try { value = body.Deserialize<FamilyWrite>(OperationResponse.JsonOptions)!; }
        catch (JsonException) { throw new InventoryProblemException(400, "InvalidRequest", "Los tipos de los campos JSON no son válidos."); }
        if (!create && value.IsActive is null || body.TryGetProperty("isActive", out var state) && state.ValueKind == JsonValueKind.Null)
            throw InventoryProblemException.Validation("isActive", "El estado activo debe estar definido.");
        // Normalización sin validar reglas cambiantes antes de resolver un replay.
        return new(value.Code?.Trim().ToUpperInvariant(), value.Name?.Trim(), value.IsActive ?? true);
    }
}

public sealed class FamilyCommands(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> WriteAsync(Guid operation, Guid? id, FamilyWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var create = id is null;
        var familyId = id ?? Guid.NewGuid();
        var resource = create ? "/api/v1/families" : $"/api/v1/families/{familyId:D}";
        var request = new OperationRequest(operation, create ? "POST" : "PUT", resource,
            "MasterData.Manage", JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            await context.Locks.DocumentsAsync([new DocumentLock("family", familyId)], token);
            var db = context.Database;
            var family = create ? null : await db.Set<Family>().SingleOrDefaultAsync(value => value.Id == familyId, token);
            if (!create && family is null)
                throw new InventoryProblemException(404, "NotFound", "La familia ya no existe.");
            if (!create) EntityTags.RequireMatch(ifMatch, family!.Version);
            var code = MasterDataText.Code(write.Code);
            var name = MasterDataText.Name(write.Name);
            var active = write.IsActive!.Value;
            if (create && !active) throw InventoryProblemException.Validation("isActive", "Las familias nuevas se crean activas.");
            var before = family is null ? null : FamilyDto.From(family);
            var action = create ? MasterDataAction.Create : family!.IsActive != active
                ? active ? MasterDataAction.Reactivate : MasterDataAction.Deactivate : MasterDataAction.Update;
            if (create) { family = new Family(familyId, code, name); db.Set<Family>().Add(family); }
            else
            {
                family!.Change(code, name, active);
                db.Entry(family).Property(value => value.Name).IsModified = true;
            }
            await db.SaveChangesAsync(token);
            var after = FamilyDto.From(family!);
            MasterDataAuditWriter.Record(context, MasterDataEntity.Family, family!.Id, action, before, after);
            var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(family.Version) };
            if (create) headers["Location"] = $"/api/v1/families/{family.Id:D}";
            return OperationResponse.Json(create ? 201 : 200, new FamilyMutation(operation, after), family.Id, headers);
        }, cancellation);
    }
}
