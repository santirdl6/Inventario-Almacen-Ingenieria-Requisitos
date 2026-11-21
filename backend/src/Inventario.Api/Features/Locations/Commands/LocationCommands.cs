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

    /// <summary>
    /// PUT /locations/{id}: corrección, cambio de tipo, desactivación o reactivación (US4).
    /// Orden: location:{id} → (retirada de una Receiving) locations:receiving → If-Match → reglas.
    /// </summary>
    public Task<OperationResponse> UpdateAsync(Guid operation, Guid id, LocationWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "PUT", $"/api/v1/locations/{id:D}", Permission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            await context.Locks.LocationsAsync([id], token);
            var db = context.Database;
            var location = await db.Set<Location>().SingleOrDefaultAsync(value => value.Id == id, token)
                ?? throw new InventoryProblemException(404, "NotFound", "La ubicación ya no existe.");
            var values = Validate(write);
            var activate = write.IsActive!.Value;
            var retiresReceiving = location is { Kind: LocationKind.Receiving, IsActive: true }
                && (!activate || values.Kind == LocationKind.Storage);
            if (retiresReceiving) await context.Locks.ReceivingZonesAsync(token);
            var currentDetail = await LocationQueries.DetailAsync(db,
                context.Services.GetRequiredService<ILocationUsage>(), location, token);
            EntityTags.RequireMatch(ifMatch, LocationEntityTags.Token(currentDetail, location.Version));

            var identity = new Dictionary<string, string[]>();
            const string locked = "Forma parte del historial de movimientos y no puede cambiarse.";
            if (values.Code != location.Code) identity["code"] = [locked];
            if (values.Aisle != location.Aisle) identity["aisle"] = [locked];
            if (values.Rack != location.Rack) identity["rack"] = [locked];
            if (values.Level != location.Level) identity["level"] = [locked];
            var kindChanges = values.Kind != location.Kind;
            var stateChanges = activate != location.IsActive;
            if (stateChanges && (identity.Count > 0 || kindChanges))
            {
                const string separate = "Desactiva o reactiva la ubicación sin cambiar a la vez sus datos; guarda primero la corrección.";
                throw new InventoryProblemException(422, "StateChangeMustBeSeparate", separate,
                    new Dictionary<string, string[]> { ["isActive"] = [separate] });
            }

            var usage = context.Services.GetRequiredService<ILocationUsage>();
            if (identity.Count > 0)
            {
                if (await usage.HasMovementsAsync(id, token))
                    throw new InventoryProblemException(409, "LocationIdentityLocked",
                        "La ubicación tiene movimientos: su código y su posición forman parte del historial. "
                        + "Crea una ubicación nueva y desactiva esta cuando quede vacía.", identity);
                if (values.Aisle != location.Aisle || values.Rack != location.Rack || values.Level != location.Level)
                    await RejectOccupiedPlaceAsync(db, values, id, token);
            }
            if (kindChanges || stateChanges && !activate)
            {
                // La congelación va antes que LocationNotEmpty (HU-04 D-04, H2 de analyze-1; US3.4): una
                // ubicación en conteo responde CountScopeFrozen esté vacía u ocupada.
                if (await usage.IsInOpenCountAsync(id, token))
                    throw new InventoryProblemException(409, "CountScopeFrozen",
                        "La ubicación está incluida en un conteo abierto. Espera a que termine el conteo.");
                var totals = await usage.GetTotalsAsync(id, token);
                if (!totals.IsEmpty)
                    throw new InventoryProblemException(409, "LocationNotEmpty",
                        $"La ubicación no está vacía: tiene {LocationText.Quantity(totals.Physical)} unidades físicas y "
                        + $"{LocationText.Quantity(totals.Reserved)} reservadas. Revisa su contenido; no se ha movido nada.");
            }
            if (retiresReceiving && await db.Set<Location>().AsNoTracking().CountAsync(value =>
                    value.Kind == LocationKind.Receiving && value.IsActive && value.Id != id, token) == 0)
                throw new InventoryProblemException(409, "NoActiveReceivingLeft",
                    "No puede quedar el almacén sin zonas de recepción activas. "
                    + "Crea o activa otra zona de recepción antes de retirar esta.");

            var before = location.AuditSnapshot();
            var corrected = location.Correct(values.Code, values.Aisle, values.Rack, values.Level);
            var kindChanged = location.ChangeKind(values.Kind);
            var stateChanged = activate ? location.Reactivate() : location.Deactivate();
            if (corrected || kindChanged || stateChanged)
            {
                await db.SaveChangesAsync(token);
                var action = stateChanged ? activate ? MasterDataAction.Reactivate : MasterDataAction.Deactivate
                    : MasterDataAction.Update;
                MasterDataAuditWriter.Record(context, MasterDataEntity.Location, id, action, before, location.AuditSnapshot());
            }
            return await ResponseAsync(context, location, 200, token);
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
        var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(LocationEntityTags.Token(detail, location.Version)) };
        if (status == 201) headers["Location"] = $"/api/v1/locations/{location.Id:D}";
        return OperationResponse.Json(status, new LocationMutation(context.OperationId, detail), location.Id, headers);
    }
}
