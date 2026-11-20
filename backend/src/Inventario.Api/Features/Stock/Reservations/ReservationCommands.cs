using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Reservations;

/// <summary>
/// Reservar (HU-05 T011; research D-04, D-05, D-07–D-09) sobre el protocolo común de A. El ejecutor
/// autoriza Reservations.Manage, abre la transacción, adquiere operation:{key}, resuelve el replay
/// y guarda OperationResult. Aquí: bloqueos documentos → ubicaciones → artículos, reglas tras los
/// bloqueos y StockCore para el diario y el saldo. Sin SaveChanges dentro de change (D-08).
/// </summary>
public sealed class ReservationCommands(InventoryOperationExecutor executor)
{
    public const string Permission = "Reservations.Manage";
    public const string Resource = "/api/v1/reservations";

    public Task<OperationResponse> ReserveAsync(Guid operation, ReservationWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        // Identidad del servidor, estable en los reintentos del ejecutor; también es el documento
        // origen del movimiento Reservation.
        var reservationId = Guid.NewGuid();
        var request = new OperationRequest(operation, "POST", Resource, Permission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            // 1. Forma (422).
            var (lineId, requested) = Validate(write);
            var db = context.Database;

            // Relaciones inmutables antes de los bloqueos: línea → pedido y artículo; lotes.
            var line = await db.Set<CustomerOrderLine>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == lineId, token)
                ?? throw new InventoryProblemException(404, "NotFound", "La línea de pedido no existe.");
            var lotIds = requested.Where(value => value.LotId is not null).Select(value => value.LotId!.Value).Distinct().ToList();
            var lots = await db.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id) || lot.ArticleId == line.ArticleId
                    && lot.Code == Lot.UntrackedCode)
                .ToListAsync(token);

            // Bloqueos: documentos en una sola llamada → ubicaciones → artículo (D-04).
            await context.Locks.DocumentsAsync([new DocumentLock(StockDocuments.CustomerOrder, line.CustomerOrderId),
                new DocumentLock(StockDocuments.Reservation, reservationId)], token);
            var locationIds = requested.Select(value => value.LocationId).Distinct().ToList();
            await context.Locks.LocationsAsync(locationIds, token);
            await context.Locks.ArticlesAsync([line.ArticleId], token);

            // 2–3. Pedido, If-Match y estado, leídos tras los bloqueos.
            var order = await db.Set<CustomerOrder>().Include(value => value.Lines)
                .SingleAsync(value => value.Id == line.CustomerOrderId, token);
            EntityTags.RequireMatch(ifMatch, order.Version);
            if (order.State != CustomerOrderState.Open)
                throw new InventoryProblemException(409, "CustomerOrderNotOpen",
                    $"El pedido {order.ExternalReference} no está abierto: no admite reservas.");
            var article = await db.Articles.AsNoTracking().SingleAsync(value => value.Id == line.ArticleId, token);

            // 4. Lote del artículo de la línea y ubicación activa; bucket una sola vez.
            var locations = await db.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
                .ToDictionaryAsync(location => location.Id, token);
            var errors = new Dictionary<string, string[]>();
            var resolved = new List<(Guid LocationId, Lot Lot, decimal Quantity)>();
            for (var index = 0; index < requested.Count; index++)
            {
                var allocation = requested[index];
                if (!locations.TryGetValue(allocation.LocationId, out var location))
                    errors[$"allocations[{index}].locationId"] = ["La ubicación no existe."];
                else if (!location.IsActive)
                    errors[$"allocations[{index}].locationId"] = [$"La ubicación {location.Code} está desactivada."];
                var lot = allocation.LotId is { } chosen
                    ? lots.SingleOrDefault(value => value.Id == chosen && value.ArticleId == article.Id)
                    : article.TracksLot ? null : lots.SingleOrDefault(value => value.ArticleId == article.Id && value.IsUntracked);
                // Sin seguimiento y sin SIN-LOTE: el artículo nunca tuvo existencias.
                if (lot is null && allocation.LotId is null && !article.TracksLot)
                    throw new InventoryProblemException(409, "InsufficientStock",
                        $"No hay existencias de {article.Sku} para reservar.");
                if (lot is null)
                {
                    errors[$"allocations[{index}].lotId"] = [allocation.LotId is null && article.TracksLot
                        ? $"El artículo {article.Sku} exige lote." : $"El lote no pertenece al artículo {article.Sku}."];
                    continue;
                }
                if (resolved.Any(value => value.LocationId == allocation.LocationId && value.Lot.Id == lot.Id))
                    errors[$"allocations[{index}].locationId"] = ["Cada ubicación y lote solo puede aparecer en una asignación."];
                else resolved.Add((allocation.LocationId, lot, allocation.Quantity));
            }
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa las asignaciones marcadas.", errors);

            // 5. Conteo abierto (activo desde HU-04, INT-HU04-03).
            var usage = context.Services.GetRequiredService<ILocationUsage>();
            foreach (var location in locationIds)
                if (await usage.IsInOpenCountAsync(location, token))
                    throw new InventoryProblemException(409, "CountScopeFrozen",
                        $"La ubicación {locations[location].Code} está en un conteo abierto. Espera a que termine.");

            // 6. Σ asignaciones + remanente activo de la línea ≤ pendiente.
            var lineReservations = await db.Set<Reservation>().AsNoTracking().Include(value => value.Allocations)
                .Where(value => value.CustomerOrderLineId == line.Id && value.State == ReservationState.Active)
                .ToListAsync(token);
            var current = order.Lines.Single(value => value.Id == line.Id);
            var active = lineReservations.SelectMany(value => value.Allocations).Sum(value => value.RemainingQuantity);
            var total = resolved.Sum(value => value.Quantity);
            if (total + active > current.PendingQuantity)
                throw new InventoryProblemException(422, "ValidationFailed",
                    "Lo reservado no puede superar lo pendiente de la línea.",
                    new Dictionary<string, string[]>
                    {
                        ["allocations"] = [$"Pendiente {Quantity(current.PendingQuantity)}, ya reservado {Quantity(active)}: "
                            + $"puedes reservar como máximo {Quantity(Math.Max(0m, current.PendingQuantity - active))}."]
                    });

            // 7. StockCore: artículo inactivo, saldo y disponible; diario y saldo en la transacción.
            var command = StockCore.Prepare(StockOperation.Reserve, new StockCommandContext(
                    new StockDocumentRef(StockDocuments.Reservation, reservationId), DateTimeOffset.UtcNow,
                    $"Reserva para el pedido {order.ExternalReference}", SupportReference: order.ExternalReference,
                    CounterpartyReference: order.CustomerReference),
                resolved.Select(value => new StockAllocation(article.Id, value.LocationId, value.Lot.Code, value.Quantity)).ToList());
            var outcome = await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token);

            db.Add(Reservation.Create(reservationId, line.Id, resolved
                .Select(value => new ReservationAllocationRequest(article.Id, value.LocationId, value.Lot.Id, value.Quantity)).ToList()));
            Touch(context, order);

            return OperationResponse.Json(201, new ReservationResult(context.OperationId, reservationId, order.Id, outcome.MovementId),
                reservationId, new Dictionary<string, string> { ["Location"] = $"{Resource}/{reservationId:D}" });
        }, cancellation);
    }

    /// <summary>
    /// Liberar (HU-05 T013; research D-06, D-07, R-05): una o varias asignaciones de la reserva,
    /// con motivo libre y releaseId propio como documento origen. Se permite con el artículo
    /// desactivado o la ubicación en conteo abierto, así que no se comprueban.
    /// </summary>
    public Task<OperationResponse> ReleaseAsync(Guid operation, Guid reservationId, ReleaseWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "POST", $"{Resource}/{reservationId:D}/release", Permission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var (releaseId, reason, requested) = ValidateRelease(write);
            var db = context.Database;

            // Relaciones inmutables antes de los bloqueos: reserva → línea → pedido; buckets y lotes.
            var known = await db.Set<Reservation>().AsNoTracking().Include(value => value.Allocations)
                .SingleOrDefaultAsync(value => value.Id == reservationId, token)
                ?? throw new InventoryProblemException(404, "NotFound", "La reserva no existe.");
            var line = await db.Set<CustomerOrderLine>().AsNoTracking()
                .SingleAsync(value => value.Id == known.CustomerOrderLineId, token);
            var lotIds = known.Allocations.Select(value => value.LotId).Distinct().ToList();
            var lotCodes = await db.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id))
                .ToDictionaryAsync(lot => lot.Id, lot => lot.Code, token);

            // Bloqueos: customer-order < reservation-release < reservation en una sola llamada (D-04)
            // → ubicaciones de la reserva → artículo.
            await context.Locks.DocumentsAsync([new DocumentLock(StockDocuments.CustomerOrder, line.CustomerOrderId),
                new DocumentLock(StockDocuments.ReservationRelease, releaseId),
                new DocumentLock(StockDocuments.Reservation, reservationId)], token);
            await context.Locks.LocationsAsync(known.Allocations.Select(value => value.LocationId).Distinct(), token);
            await context.Locks.ArticlesAsync([line.ArticleId], token);

            // Estado actual tras los bloqueos: If-Match de la reserva, asignaciones y remanente.
            var reservation = await db.Set<Reservation>().Include(value => value.Allocations)
                .SingleAsync(value => value.Id == reservationId, token);
            EntityTags.RequireMatch(ifMatch, reservation.Version);
            var errors = new Dictionary<string, string[]>();
            var chosen = new List<(ReservationAllocation Allocation, decimal Quantity)>();
            for (var index = 0; index < requested.Count; index++)
            {
                var allocation = reservation.Allocations.SingleOrDefault(value => value.Id == requested[index].AllocationId);
                if (allocation is null) errors[$"allocations[{index}].allocationId"] = ["La asignación no pertenece a esta reserva."];
                else chosen.Add((allocation, requested[index].Quantity));
            }
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa las asignaciones marcadas.", errors);
            foreach (var (allocation, quantity) in chosen)
                if (quantity > allocation.RemainingQuantity)
                    throw new InventoryProblemException(409, "ReservedStockConflict",
                        $"No se pueden liberar {Quantity(quantity)}: a la asignación solo le quedan "
                        + $"{Quantity(allocation.RemainingQuantity)} reservadas.");

            var order = await db.Set<CustomerOrder>().SingleAsync(value => value.Id == line.CustomerOrderId, token);
            var command = StockCore.Prepare(StockOperation.Release, new StockCommandContext(
                    new StockDocumentRef(StockDocuments.ReservationRelease, releaseId), DateTimeOffset.UtcNow, reason,
                    SupportReference: $"{StockDocuments.Reservation}:{reservationId:D}",
                    CounterpartyReference: order.CustomerReference),
                chosen.Select(value => new StockAllocation(value.Allocation.ArticleId, value.Allocation.LocationId,
                    lotCodes[value.Allocation.LotId], value.Quantity)).ToList());
            var outcome = await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token);

            foreach (var (allocation, quantity) in chosen) allocation.Release(quantity);
            reservation.DeriveState();
            db.Entry(reservation).Property(value => value.State).IsModified = true;
            Touch(context, order);

            return OperationResponse.Json(200, new ReleaseResult(context.OperationId, reservationId, releaseId,
                outcome.MovementId, reservation.State.ToString()), releaseId);
        }, cancellation);
    }

    internal sealed record ReleaseRequested(Guid AllocationId, decimal Quantity);

    internal static (Guid ReleaseId, string Reason, IReadOnlyList<ReleaseRequested> Allocations) ValidateRelease(ReleaseWrite write)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Guid.TryParse(write.ReleaseId, out var releaseId) || releaseId == Guid.Empty)
            errors["releaseId"] = ["Falta el identificador de la liberación."];
        var reason = write.Reason?.Trim() ?? "";
        if (reason.Length == 0) errors["reason"] = ["Indica el motivo de la liberación."];
        else if (reason.Length > 500) errors["reason"] = ["El motivo no puede superar 500 caracteres."];
        var allocations = write.Allocations ?? [];
        if (allocations.Count == 0) errors["allocations"] = ["Indica al menos una asignación y cantidad."];
        else if (allocations.Count > 100) errors["allocations"] = ["Una liberación admite como máximo 100 asignaciones."];
        var requested = new List<ReleaseRequested>();
        for (var index = 0; index < allocations.Count; index++)
        {
            var allocation = allocations[index];
            var idValid = Guid.TryParse(allocation.AllocationId, out var id) && id != Guid.Empty;
            if (!idValid) errors[$"allocations[{index}].allocationId"] = ["Elige la asignación que liberas."];
            else if (requested.Any(value => value.AllocationId == id))
                errors[$"allocations[{index}].allocationId"] = ["Cada asignación solo puede aparecer una vez."];
            var problem = JsonBody.QuantityProblem(allocation.Quantity, out var quantity);
            if (problem is not null) errors[$"allocations[{index}].quantity"] = [problem];
            if (idValid && problem is null && requested.All(value => value.AllocationId != id)) requested.Add(new(id, quantity));
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa los datos marcados de la liberación.", errors);
        return (releaseId, reason, requested);
    }

    /// <summary>Renueva la versión de la cabecera aunque no cambie nada en ella (research D-07).</summary>
    internal static void Touch(OperationContext context, CustomerOrder order) =>
        context.Database.Entry(order).Property(value => value.State).IsModified = true;

    internal sealed record Requested(Guid LocationId, Guid? LotId, decimal Quantity);

    internal static (Guid Line, IReadOnlyList<Requested> Allocations) Validate(ReservationWrite write)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Guid.TryParse(write.CustomerOrderLineId, out var line) || line == Guid.Empty)
            errors["customerOrderLineId"] = ["Elige la línea del pedido."];
        var allocations = write.Allocations ?? [];
        if (allocations.Count == 0) errors["allocations"] = ["Indica al menos una ubicación y cantidad."];
        else if (allocations.Count > 100) errors["allocations"] = ["Una reserva admite como máximo 100 asignaciones."];
        var requested = new List<Requested>();
        for (var index = 0; index < allocations.Count; index++)
        {
            var allocation = allocations[index];
            var locationValid = Guid.TryParse(allocation.LocationId, out var location) && location != Guid.Empty;
            if (!locationValid) errors[$"allocations[{index}].locationId"] = ["Elige una ubicación."];
            Guid? lot = null;
            if (allocation.LotId is not null)
            {
                if (Guid.TryParse(allocation.LotId, out var parsed) && parsed != Guid.Empty) lot = parsed;
                else errors[$"allocations[{index}].lotId"] = ["El lote indicado no es válido."];
            }
            var problem = JsonBody.QuantityProblem(allocation.Quantity, out var quantity);
            if (problem is not null) errors[$"allocations[{index}].quantity"] = [problem];
            if (locationValid && problem is null) requested.Add(new(location, lot, quantity));
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa los datos marcados de la reserva.", errors);
        return (line, requested);
    }

    internal static string Quantity(decimal value) => LocationText.Quantity(value);
}
