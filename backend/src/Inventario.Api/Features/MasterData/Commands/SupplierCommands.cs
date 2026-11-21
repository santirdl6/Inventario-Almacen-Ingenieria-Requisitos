using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Commands;

public sealed record SupplierDto(Guid Id, string Code, string Name, string? Email, string? TaxIdentifier, bool IsActive)
{
    public static SupplierDto From(Supplier supplier) => new(supplier.Id, supplier.Code, supplier.Name, supplier.Email, supplier.TaxIdentifier, supplier.IsActive);
}
public sealed record SupplierMutation(Guid OperationId, SupplierDto Resource);
public sealed record SupplierWrite([property: Required, StringLength(64)] string? Code,
    [property: Required, StringLength(200)] string? Name, [property: StringLength(256)] string? Email,
    [property: StringLength(64)] string? TaxIdentifier, bool? IsActive)
{
    public static SupplierWrite Read(JsonElement body, bool create)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        foreach (var property in body.EnumerateObject())
            if (property.Name is not ("code" or "name" or "email" or "taxIdentifier" or "isActive"))
                throw InventoryProblemException.Validation(property.Name, "Este campo no es editable en proveedores.");
        SupplierWrite value;
        try { value = body.Deserialize<SupplierWrite>(OperationResponse.JsonOptions)!; }
        catch (JsonException) { throw new InventoryProblemException(400, "InvalidRequest", "Los tipos de los campos JSON no son válidos."); }
        if (!create && value.IsActive is null || body.TryGetProperty("isActive", out var state) && state.ValueKind == JsonValueKind.Null)
            throw InventoryProblemException.Validation("isActive", "El estado activo debe estar definido.");
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return value with { Code = value.Code?.Trim().ToUpperInvariant(), Name = value.Name?.Trim(), Email = Optional(value.Email), TaxIdentifier = Optional(value.TaxIdentifier), IsActive = value.IsActive ?? true };
    }
}

public sealed class SupplierCommands(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> WriteAsync(Guid operation, Guid? id, SupplierWrite write, string? ifMatch, CancellationToken cancellation)
    {
        var create = id is null; var supplierId = id ?? Guid.NewGuid();
        return executor.ExecuteAsync(new(operation, create ? "POST" : "PUT", create ? "/api/v1/suppliers" : $"/api/v1/suppliers/{supplierId:D}",
            "MasterData.Manage", JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions)), async (context, token) =>
        {
            await context.Locks.DocumentsAsync([new DocumentLock("supplier", supplierId)], token);
            var db = context.Database;
            var supplier = create ? null : await db.Set<Supplier>().SingleOrDefaultAsync(value => value.Id == supplierId, token);
            if (!create && supplier is null) throw new InventoryProblemException(404, "NotFound", "El proveedor ya no existe.");
            if (!create) EntityTags.RequireMatch(ifMatch, supplier!.Version);
            var code = MasterDataText.Code(write.Code); var name = MasterDataText.Name(write.Name);
            var email = MasterDataText.Email(write.Email); var tax = MasterDataText.Optional(write.TaxIdentifier, 64, "taxIdentifier");
            var active = write.IsActive!.Value;
            if (create && !active) throw InventoryProblemException.Validation("isActive", "Los proveedores nuevos se crean activos.");
            var before = supplier is null ? null : SupplierDto.From(supplier);
            var action = create ? MasterDataAction.Create : supplier!.IsActive != active
                ? active ? MasterDataAction.Reactivate : MasterDataAction.Deactivate : MasterDataAction.Update;
            if (create) { supplier = new Supplier(supplierId, code, name, email, tax); db.Set<Supplier>().Add(supplier); }
            else { supplier!.Change(code, name, email, tax, active); db.Entry(supplier).Property(value => value.Name).IsModified = true; }
            await db.SaveChangesAsync(token);
            var after = SupplierDto.From(supplier!);
            MasterDataAuditWriter.Record(context, MasterDataEntity.Supplier, supplierId, action, before, after);
            var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(supplier!.Version) };
            if (create) headers["Location"] = $"/api/v1/suppliers/{supplierId:D}";
            return OperationResponse.Json(create ? 201 : 200, new SupplierMutation(operation, after), supplierId, headers);
        }, cancellation);
    }
}
