using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.CycleCounts;

/// <summary>
/// Comandos de conteos (HU-04; research D-02, D-03, D-11) sobre el protocolo común de A: el ejecutor
/// autoriza el permiso de la fila, abre la transacción, adquiere operation:{key}, resuelve el replay y
/// guarda OperationResult. Aquí se bloquea cycle-count:{id} y, según el comando, las ubicaciones y los
/// artículos (D-03), se valida bajo bloqueo y se escribe.
/// </summary>
public sealed class CycleCountCommands(InventoryOperationExecutor executor)
{
    public const string RecordPermission = "Counts.Record";
    public const string AdjustPermission = "Inventory.Adjust";
    public const string Resource = "/api/v1/cycle-counts";

    /// <summary>POST /cycle-counts (T006): borrador con 1–50 ubicaciones activas sin repetir; no congela.</summary>
    public Task<OperationResponse> CreateAsync(Guid operation, CycleCountWrite write, CancellationToken cancellation)
    {
        // Identidad estable en los reintentos del ejecutor: se decide fuera de change.
        var id = Guid.NewGuid();
        var request = new OperationRequest(operation, "POST", Resource, RecordPermission, write.Fingerprint());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var locations = ValidateScope(write);
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            await context.Locks.LocationsAsync(locations, token);
            await RequireActiveLocationsAsync(db, locations, token);

            var count = CycleCount.CreateDraft(id, locations, context.ActorId, DateTimeOffset.UtcNow);
            db.Add(count);
            await db.SaveChangesAsync(token);
            return Mutation(201, context, count, withLocation: true);
        }, cancellation);
    }

    /// <summary>PUT /cycle-counts/{id} (T006): sustituye el alcance de un borrador con If-Match.</summary>
    public Task<OperationResponse> UpdateAsync(Guid operation, Guid id, CycleCountWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "PUT", $"{Resource}/{id:D}", RecordPermission, write.Fingerprint());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var locations = ValidateScope(write);
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            await context.Locks.LocationsAsync(locations, token);

            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.Draft);
            await RequireActiveLocationsAsync(db, locations, token);
            count.ReplaceScope(locations);
            Touch(db, count);
            await db.SaveChangesAsync(token);
            return Mutation(200, context, count, withLocation: false);
        }, cancellation);
    }

    /// <summary>
    /// POST /cycle-counts/{id}/start (T007; D-03–D-05): bloquea cycle-count:{id}, todas las ubicaciones
    /// del alcance y, tras leer sus saldos, los artículos con físico &gt; 0. Comprueba ubicaciones activas y
    /// no congeladas por otro conteo, congela el alcance, crea las líneas esperadas con su teórico y marca
    /// la primera referencia de sus artículos. Iniciar o un escritor de B serializan en location:{id}: o el
    /// teórico incluye el movimiento, o el movimiento ve la congelación (FR-017).
    /// </summary>
    public Task<OperationResponse> StartAsync(Guid operation, Guid id, string? ifMatch, CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "POST", $"{Resource}/{id:D}/start", RecordPermission, Empty());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            // El alcance solo cambia bajo cycle-count:{id}: leído ya bajo ese bloqueo.
            var scope = await db.Set<CountLocation>().AsNoTracking().Where(location => location.CountId == id)
                .Select(location => location.LocationId).ToListAsync(token);
            await context.Locks.LocationsAsync(scope, token);
            // El teórico de una ubicación solo cambia bajo su bloqueo, ya tomado (D-03).
            var balances = await (
                    from balance in db.Set<StockBalance>().AsNoTracking()
                    join lot in db.Set<Lot>().AsNoTracking() on balance.LotId equals lot.Id
                    where scope.Contains(balance.LocationId) && balance.Physical > 0
                    select new { balance.ArticleId, balance.LocationId, balance.LotId, LotCode = lot.Code, balance.Physical })
                .ToListAsync(token);
            var articleIds = balances.Select(balance => balance.ArticleId).Distinct().ToList();
            await context.Locks.ArticlesAsync(articleIds, token);

            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.Draft);
            var locations = count.Locations.Select(location => location.LocationId).ToList();
            await RequireActiveLocationsAsync(db, locations, token);
            await RequireNotFrozenAsync(db, count, token);

            await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, articleIds, token);
            count.Start(balances.Select(balance => new CountExpectedLine(balance.ArticleId, balance.LocationId, balance.LotId,
                balance.LotCode, balance.Physical)).ToList(), context.ActorId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(token);
            return Mutation(200, context, count, withLocation: false);
        }, cancellation);
    }

    /// <summary>409 CountScopeFrozen si una ubicación del alcance está congelada por otro conteo, nombrándolo.</summary>
    private static async Task RequireNotFrozenAsync(InventoryDbContext db, CycleCount count, CancellationToken token)
    {
        var scope = count.Locations.Select(location => location.LocationId).ToList();
        var frozen = await (
                from location in db.Set<CountLocation>().AsNoTracking()
                join other in db.Set<CycleCount>().AsNoTracking() on location.CountId equals other.Id
                join place in db.Set<Location>().AsNoTracking() on location.LocationId equals place.Id
                where scope.Contains(location.LocationId) && location.IsFrozen && location.CountId != count.Id
                orderby place.Code
                select new { location.LocationId, place.Code, other.CountCode })
            .ToListAsync(token);
        if (frozen.Count == 0) return;
        var errors = frozen.ToDictionary(row => $"locationIds[{scope.IndexOf(row.LocationId)}]",
            row => new[] { $"La ubicación {row.Code} está en el conteo abierto {row.CountCode}." });
        throw new InventoryProblemException(409, "CountScopeFrozen",
            "Alguna ubicación ya está en otro conteo abierto: "
            + string.Join("; ", frozen.Select(row => $"{row.Code} en {row.CountCode}")) + ". Espera a que termine.", errors);
    }

    /// <summary>Forma del alcance: 1–50 identificadores válidos y sin repetir; todos los errores juntos.</summary>
    internal static List<Guid> ValidateScope(CycleCountWrite write)
    {
        var errors = new Dictionary<string, string[]>();
        var requested = write.LocationIds ?? [];
        if (requested.Count == 0) errors["locationIds"] = ["Elige al menos una ubicación."];
        else if (requested.Count > CycleCount.MaxLocations)
            errors["locationIds"] = [$"Un conteo admite como máximo {CycleCount.MaxLocations} ubicaciones."];
        var locations = new List<Guid>();
        for (var index = 0; index < requested.Count && requested.Count <= CycleCount.MaxLocations; index++)
        {
            if (Guid.TryParse(requested[index], out var location) && location != Guid.Empty) locations.Add(location);
            else errors[$"locationIds[{index}]"] = ["La ubicación no es válida."];
        }
        if (locations.Distinct().Count() != locations.Count) errors["locationIds"] = ["Cada ubicación solo puede aparecer una vez."];
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa las ubicaciones del conteo.", errors);
        return locations;
    }

    /// <summary>Ubicaciones existentes y activas, leídas tras su bloqueo (422 por índice).</summary>
    internal static async Task RequireActiveLocationsAsync(InventoryDbContext db, IReadOnlyList<Guid> locations,
        CancellationToken token)
    {
        var stored = await db.Set<Location>().AsNoTracking().Where(location => locations.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, token);
        var errors = new Dictionary<string, string[]>();
        for (var index = 0; index < locations.Count; index++)
        {
            if (!stored.TryGetValue(locations[index], out var location)) errors[$"locationIds[{index}]"] = ["La ubicación no existe."];
            else if (!location.IsActive) errors[$"locationIds[{index}]"] = [$"La ubicación {location.Code} está desactivada."];
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa las ubicaciones del conteo.", errors);
    }

    /// <summary>
    /// Conteo con su alcance y líneas, leído tras cycle-count:{id}. 404 si no existe; 409
    /// DocumentAlreadyConfirmed si está cerrado; 409 CountStateConflict si el estado no admite la acción;
    /// después el If-Match (428/412).
    /// </summary>
    internal static async Task<CycleCount> LoadAsync(InventoryDbContext db, Guid id, string? ifMatch, CancellationToken token,
        params CycleCountState[] allowed)
    {
        var count = await db.Set<CycleCount>().Include(value => value.Locations).Include(value => value.Lines)
            .AsSplitQuery().SingleOrDefaultAsync(value => value.Id == id, token)
            ?? throw new InventoryProblemException(404, "NotFound", "El conteo no existe.");
        if (count.IsClosed)
            throw new InventoryProblemException(409, "DocumentAlreadyConfirmed",
                $"El conteo {count.CountCode} ya está cerrado y no admite cambios.");
        if (!allowed.Contains(count.State))
            throw new InventoryProblemException(409, "CountStateConflict",
                $"El conteo {count.CountCode} está en {StateText(count.State)}: la acción no corresponde a ese estado.");
        EntityTags.RequireMatch(ifMatch, count.Version);
        return count;
    }

    /// <summary>
    /// Renueva la versión de la cabecera aunque solo cambien filas hijas (D-11): un UPDATE de la cabecera
    /// con el mismo estado cambia su rowversion, como hacen HU-05 y HU-03 con el pedido.
    /// </summary>
    internal static void Touch(InventoryDbContext db, CycleCount count) =>
        db.Entry(count).Property(value => value.State).IsModified = true;

    internal static string StateText(CycleCountState state) => state switch
    {
        CycleCountState.Draft => "borrador",
        CycleCountState.Counting => "conteo",
        CycleCountState.AwaitingApproval => "pendiente de aprobación",
        CycleCountState.Confirmed => "confirmado",
        _ => "cancelado"
    };

    internal static OperationResponse Mutation(int status, OperationContext context, CycleCount count, bool withLocation)
    {
        var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(count.Version) };
        if (withLocation) headers["Location"] = $"{Resource}/{count.Id:D}";
        return OperationResponse.Json(status, new CycleCountMutation(context.OperationId, CycleCountView.From(count)), count.Id, headers);
    }

    internal static JsonElement Empty() => JsonSerializer.SerializeToElement(new { }, OperationResponse.JsonOptions);
}

/// <summary>
/// Tipo canónico de documento de HU-04 para InventoryLocks y StockCore (research D-03). En orden
/// ordinal: customer-order-reference &lt; customer-order &lt; cycle-count &lt; reservation-release &lt;
/// reservation &lt; shipment.
/// </summary>
public static class CycleCountDocuments
{
    public const string CycleCount = "cycle-count";
}
