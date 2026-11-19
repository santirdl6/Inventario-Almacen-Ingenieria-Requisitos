using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Features.Locations.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Locations.Commands;

// Mutaciones de HU-02 sobre el protocolo común de A (D-04–D-07): el ejecutor autoriza, abre la
// transacción, adquiere operation:{key}, resuelve el replay y guarda OperationResult; aquí se
// bloquea location:{id}, se valida, se escribe la ubicación y se audita con MasterDataAuditWriter.
public sealed class LocationCommands(InventoryOperationExecutor executor)
{
    public const string Permission = "Locations.Manage";

    public Task<OperationResponse> CreateAsync(Guid operation, LocationWrite write, CancellationToken cancellation)
    {
        var id = Guid.NewGuid();
        var request = new OperationRequest(operation, "POST", "/api/v1/locations", Permission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            await context.Locks.LocationsAsync([id], token);
            var values = Validate(write);
            if (write.IsActive != true)
                throw InventoryProblemException.Validation("isActive", "Las ubicaciones nuevas se crean activas.");
            var db = context.Database;
            await RejectOccupiedPlaceAsync(db, values, null, token);
            var location = new Location(id, values.Code, values.Aisle, values.Rack, values.Level, values.Kind);
            db.Add(location);
            await db.SaveChangesAsync(token);
            MasterDataAuditWriter.Record(context, MasterDataEntity.Location, id, MasterDataAction.Create, null,
                location.AuditSnapshot());
            return await ResponseAsync(context, location, 201, token);
        }, cancellation);
    }

    internal sealed record Values(string Code, string Aisle, string Rack, string Level, LocationKind Kind);

    internal static Values Validate(LocationWrite write)
    {
        var errors = new Dictionary<string, string[]>();
        void Check(string field, string? value, int maximum, out string normalized)
        {
            var problem = Location.Normalize(value, maximum, out normalized);
            if (problem is not null) errors[field] = [problem];
        }
        Check("code", write.Code, Location.CodeMaxLength, out var code);
        Check("aisle", write.Aisle, Location.PartMaxLength, out var aisle);
        Check("rack", write.Rack, Location.PartMaxLength, out var rack);
        Check("level", write.Level, Location.PartMaxLength, out var level);
        var kindValid = Enum.TryParse<LocationKind>(write.Kind, false, out var kind) && Enum.IsDefined(kind)
            && !int.TryParse(write.Kind, out _);
        if (!kindValid) errors["kind"] = ["Elige el tipo: almacenamiento o zona de recepción."];
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed",
                "Revisa los datos marcados: pasillo, estantería y nivel son obligatorios en todas las ubicaciones.", errors);
        return new(code, aisle, rack, level, kind);
    }

    /// <summary>US1.3: el lugar repetido identifica la ubicación ya registrada. El índice único
    /// cubre la carrera entre dos altas (MasterDataProblemMapper, sin nombrarla).</summary>
    internal static async Task RejectOccupiedPlaceAsync(InventoryDbContext db, Values values, Guid? except,
        CancellationToken cancellation)
    {
        var existing = await db.Set<Location>().AsNoTracking()
            .Where(location => location.Aisle == values.Aisle && location.Rack == values.Rack
                && location.Level == values.Level && location.Id != except)
            .Select(location => new { location.Code, location.IsActive }).FirstOrDefaultAsync(cancellation);
        if (existing is null) return;
        const string field = "Ese pasillo, estantería y nivel ya están registrados.";
        throw new InventoryProblemException(409, "DuplicateLocationPlace",
            $"Ese pasillo, estantería y nivel ya corresponden a la ubicación {existing.Code}"
            + (existing.IsActive ? "." : " (inactiva).") + " Usa esa ubicación o elige otro lugar.",
            new Dictionary<string, string[]> { ["aisle"] = [field], ["rack"] = [field], ["level"] = [field] });
    }

    internal static async Task<OperationResponse> ResponseAsync(OperationContext context, Location location, int status,
        CancellationToken cancellation)
    {
        var usage = context.Services.GetRequiredService<ILocationUsage>();
        var detail = await LocationQueries.DetailAsync(context.Database, usage, location, cancellation);
        var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(location.Version) };
        if (status == 201) headers["Location"] = $"/api/v1/locations/{location.Id:D}";
        return OperationResponse.Json(status, new LocationMutation(context.OperationId, detail), location.Id, headers);
    }
}
