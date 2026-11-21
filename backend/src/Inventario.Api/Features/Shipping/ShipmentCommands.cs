using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Features.Locations;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Shipping;

/// <summary>
/// Borrador de salida (HU-03 T007; research D-02, D-03) sobre el protocolo común de A: el ejecutor
/// autoriza (Shipping.Record), abre la transacción, adquiere operation:{key}, resuelve el replay y
/// guarda OperationResult. Aquí se bloquean la salida y los artículos de sus líneas (contrato de
/// escritores, INT-HU03-01), se valida bajo bloqueo y se escribe. Un borrador no retiene existencias
/// ni cambia el pedido, así que no toma customer-order ni ubicaciones; su validación del pendiente y
/// del bucket es orientativa y la confirmación (T009) la repite bajo los bloqueos completos.
/// </summary>
public sealed class ShipmentCommands(InventoryOperationExecutor executor)
{
    public const string Permission = "Shipping.Record";
    public const string Resource = "/api/v1/shipments";
    // Misma tolerancia que StockCore para la fecha del hecho (research D-07; FR-005).
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    public Task<OperationResponse> CreateAsync(Guid operation, ShipmentWrite write, CancellationToken cancellation)
    {
        // Identidad estable en los reintentos del ejecutor: se decide fuera de change.
        var id = Guid.NewGuid();
        var request = new OperationRequest(operation, "POST", Resource, Permission, Fingerprint(write));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var values = Validate(write, requireOrder: true);
            var db = context.Database;
            var orderLines = await OrderLinesAsync(db, values, token);
            var articleIds = orderLines.Values.Select(line => line.ArticleId).Distinct().ToList();

            // Orden del protocolo: documentos → artículos, antes de decidir (D-03).
            await context.Locks.DocumentsAsync([new DocumentLock(ShippingDocuments.Shipment, id)], token);
            await context.Locks.ArticlesAsync(articleIds, token);

            var order = await OpenOrderAsync(db, values.OrderId!.Value, token);
            var lines = await ResolveAsync(db, order, values, orderLines, token);
            await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, articleIds, token);
            var shipment = Shipment.CreateDraft(id, order.Id, values.OccurredAt, lines, context.ActorId, DateTimeOffset.UtcNow);
            db.Add(shipment);
            await db.SaveChangesAsync(token);
            return Mutation(201, context, shipment, withLocation: true);
        }, cancellation);
    }

    public Task<OperationResponse> UpdateAsync(Guid operation, Guid id, ShipmentWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "PUT", $"{Resource}/{id:D}", Permission, Fingerprint(write));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var values = Validate(write, requireOrder: false);
            var db = context.Database;
            // Artículos de las líneas anteriores (retiradas) y nuevas: se bloquean todos (D-03). Si otra
            // edición cambia las líneas entre esta lectura y el bloqueo, el If-Match lo rechaza.
            var previous = await db.Set<ShipmentLine>().AsNoTracking().Where(line => line.ShipmentId == id)
                .Select(line => line.ArticleId).ToListAsync(token);
            var orderLines = await OrderLinesAsync(db, values, token);
            var added = orderLines.Values.Select(line => line.ArticleId).Distinct().ToList();

            await context.Locks.DocumentsAsync([new DocumentLock(ShippingDocuments.Shipment, id)], token);
            await context.Locks.ArticlesAsync(previous.Concat(added).Distinct().ToList(), token);

            var shipment = await DraftAsync(db, id, ifMatch, token);
            if (values.OrderId is { } requested && requested != shipment.CustomerOrderId)
                throw InventoryProblemException.Validation("customerOrderId", "La salida pertenece a otro pedido; no se puede cambiar.");
            var order = await OpenOrderAsync(db, shipment.CustomerOrderId, token);
            var lines = await ResolveAsync(db, order, values, orderLines, token);
            await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, added, token);
            shipment.ReplaceDraft(values.OccurredAt, lines, context.ActorId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(token);
            return Mutation(200, context, shipment, withLocation: false);
        }, cancellation);
    }

    public Task<OperationResponse> DeleteAsync(Guid operation, Guid id, string? ifMatch, CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "DELETE", $"{Resource}/{id:D}", Permission,
            JsonSerializer.SerializeToElement(new { }, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var db = context.Database;
            var articles = await db.Set<ShipmentLine>().AsNoTracking().Where(line => line.ShipmentId == id)
                .Select(line => line.ArticleId).Distinct().ToListAsync(token);
            await context.Locks.DocumentsAsync([new DocumentLock(ShippingDocuments.Shipment, id)], token);
            await context.Locks.ArticlesAsync(articles, token);

            var shipment = await DraftAsync(db, id, ifMatch, token);
            // Retira las referencias de sus líneas; la marca histórica del artículo no se revierte (R-05).
            db.Remove(shipment);
            await db.SaveChangesAsync(token);
            return new OperationResponse(204, null, sourceDocumentId: id);
        }, cancellation);
    }

    /// <summary>
    /// Confirmar (T009; research D-03–D-05, D-07; reglas 1–6 del contrato HU-03 de HU-05). Bloquea en
    /// una sola llamada el pedido, las reservas a consumir y la salida; después ubicaciones y
    /// artículos. Decide con el lector del pedido tras los bloqueos y aplica la salida con StockCore.
    /// Todo queda en el mismo commit que el OperationResult: sin SaveChanges aquí (FK del diario).
    /// </summary>
    public Task<OperationResponse> ConfirmAsync(Guid operation, Guid id, ShipmentConfirmWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "POST", $"{Resource}/{id:D}/confirm", Permission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            if (string.IsNullOrWhiteSpace(write.OrderVersion))
                throw InventoryProblemException.Validation("orderVersion", "Revisa la salida antes de confirmarla.");
            var db = context.Database;

            // 1. Relaciones inmutables, antes de bloquear: salida → pedido; líneas → línea de pedido,
            // bucket y artículo; reservas activas de esas líneas con remanente en esos buckets.
            var header = await db.Set<Shipment>().AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == id, token)
                ?? throw new InventoryProblemException(404, "NotFound", "La salida no existe.");
            var orderLineIds = header.Lines.Select(line => line.CustomerOrderLineId).Distinct().ToList();
            var buckets = header.Lines.Select(line => (line.CustomerOrderLineId, line.LocationId, line.LotId)).ToHashSet();
            var candidates = await db.Set<Reservation>().AsNoTracking().Include(reservation => reservation.Allocations)
                .Where(reservation => orderLineIds.Contains(reservation.CustomerOrderLineId) && reservation.State == ReservationState.Active)
                .ToListAsync(token);
            var reservationIds = candidates.Where(reservation => reservation.Allocations.Any(allocation => allocation.RemainingQuantity > 0
                    && buckets.Contains((reservation.CustomerOrderLineId, allocation.LocationId, allocation.LotId))))
                .Select(reservation => reservation.Id).ToHashSet();

            // 2. Bloqueos: documentos en una sola llamada → ubicaciones → artículos (D-03, regla 1).
            await context.Locks.DocumentsAsync([new DocumentLock(StockDocuments.CustomerOrder, header.CustomerOrderId),
                .. reservationIds.Select(reservation => new DocumentLock(StockDocuments.Reservation, reservation)),
                new DocumentLock(ShippingDocuments.Shipment, id)], token);
            var locationIds = header.Lines.Select(line => line.LocationId).Distinct().ToList();
            await context.Locks.LocationsAsync(locationIds, token);
            await context.Locks.ArticlesAsync(header.Lines.Select(line => line.ArticleId).Distinct().ToList(), token);

            // 3. Salida, If-Match y versión revisada del pedido, leídos tras los bloqueos (D-04, D-05).
            var shipment = await db.Set<Shipment>().Include(value => value.Lines).SingleAsync(value => value.Id == id, token);
            if (shipment.State != ShipmentState.Draft)
                throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La salida ya está confirmada.");
            EntityTags.RequireMatch(ifMatch, shipment.Version);
            var snapshot = await context.Services.GetRequiredService<ICustomerOrderReader>()
                .ReadAsync(context, shipment.CustomerOrderId, token)
                ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
            if (EntityTags.Format(snapshot.Version) != write.OrderVersion)
                throw new InventoryProblemException(412, "VersionConflict",
                    "El pedido cambió desde que revisaste la salida (otra salida, reserva o liberación). Revisa las cifras de nuevo.");
            if (snapshot.State != nameof(CustomerOrderState.Open))
                throw new InventoryProblemException(409, "CustomerOrderNotOpen",
                    $"El pedido {snapshot.ExternalReference} no está abierto: no admite nuevas salidas.");
            var lines = shipment.Lines.OrderBy(line => line.Id).ToList();
            var orderLines = snapshot.Lines.ToDictionary(line => line.Id);

            var pendingErrors = new Dictionary<string, string[]>();
            foreach (var group in lines.Select((line, index) => (line, index)).GroupBy(pair => pair.line.CustomerOrderLineId))
            {
                var pending = orderLines[group.Key].Pending;
                if (group.Sum(pair => pair.line.Quantity) <= pending) continue;
                foreach (var (_, index) in group)
                    pendingErrors[$"lines[{index}].quantity"] =
                        [$"La suma de esta línea de pedido supera su pendiente actual ({ShipmentView.Quantity(pending)})."];
            }
            if (pendingErrors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "La salida supera lo pendiente del pedido.", pendingErrors);

            var usage = context.Services.GetRequiredService<ILocationUsage>();
            foreach (var location in locationIds)
                if (await usage.IsInOpenCountAsync(location, token))
                    throw new InventoryProblemException(409, "CountScopeFrozen",
                        "Una ubicación de origen está en un conteo abierto. Espera a que termine.");

            // 4. Reserva propia por bucket (regla 2), repartida por Id de asignación (regla 3).
            var plan = new List<(ShipmentLine Line, ReservationAllocationSnapshot Allocation, decimal Quantity)>();
            var consumed = new Dictionary<Guid, decimal>();
            foreach (var line in lines)
            {
                var own = orderLines[line.CustomerOrderLineId].ActiveAllocations
                    .Where(allocation => allocation.LocationId == line.LocationId && allocation.LotId == line.LotId)
                    .OrderBy(allocation => allocation.AllocationId).ToList();
                var left = Math.Min(line.Quantity, own.Sum(allocation => allocation.Remaining));
                consumed[line.Id] = left;
                foreach (var allocation in own)
                {
                    if (left == 0) break;
                    var take = Math.Min(left, allocation.Remaining);
                    plan.Add((line, allocation, take));
                    left -= take;
                }
            }
            // Una reserva creada entre la lectura previa y el bloqueo habría renovado el pedido (412);
            // si aun así apareciera sin bloquear, no se decide sobre ella.
            if (plan.Any(item => !reservationIds.Contains(item.Allocation.ReservationId)))
                throw new InventoryProblemException(412, "VersionConflict", "Las reservas del pedido cambiaron. Revisa la salida de nuevo.");

            // 5. Remanente sobrante (regla 5): sin liberar nada en silencio.
            foreach (var group in lines.GroupBy(line => line.CustomerOrderLineId))
            {
                var orderLine = orderLines[group.Key];
                var remainingAfter = orderLine.ActiveAllocations.Sum(allocation => allocation.Remaining) - group.Sum(line => consumed[line.Id]);
                var pendingAfter = orderLine.Pending - group.Sum(line => line.Quantity);
                if (remainingAfter > pendingAfter)
                {
                    var article = await db.Articles.AsNoTracking().Where(value => value.Id == orderLine.ArticleId)
                        .Select(value => value.Sku).SingleAsync(token);
                    throw new InventoryProblemException(409, "ReservedStockConflict",
                        $"Antes de servir desde otro origen hay que liberar {ShipmentView.Quantity(remainingAfter - pendingAfter)} "
                        + $"reservadas de la línea {article}.");
                }
            }

            // 6. StockCore: físico, reservado y disponible de cada bucket; diario ShipReserved/ShipAvailable.
            var order = await db.Set<CustomerOrder>().Include(value => value.Lines).SingleAsync(value => value.Id == snapshot.Id, token);
            var lotIds = lines.Select(line => line.LotId).Distinct().ToList();
            var lotCodes = await db.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id))
                .ToDictionaryAsync(lot => lot.Id, lot => lot.Code, token);
            var command = StockCore.PrepareShipment(new StockCommandContext(
                    new StockDocumentRef(ShippingDocuments.Shipment, id), shipment.OccurredAt,
                    $"Salida del pedido {order.ExternalReference}", SupportReference: order.ExternalReference,
                    CounterpartyReference: order.CustomerReference),
                lines.Select(line => new ShipmentAllocation(line.ArticleId, line.LocationId, lotCodes[line.LotId], line.Quantity,
                    consumed[line.Id])).ToList());
            StockOperationOutcome outcome;
            try
            {
                outcome = await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token);
            }
            catch (InventoryProblemException problem) when (problem is { Status: 409, Code: "InsufficientStock" })
            {
                // Convergence C2 (G1; US2.2, FR-010, FR-024): StockCore, compartido con HU-08, sigue
                // redactando físico y disponible; HU-03 añade la cantidad posible de cada línea rechazada.
                throw await WithPossibleQuantityAsync(db, problem, lines, consumed, token);
            }

            // 7–8. Efectos de HU-05 y de la salida; versiones renovadas (regla 4).
            var reservations = await db.Set<Reservation>().Include(reservation => reservation.Allocations)
                .Where(reservation => reservationIds.Contains(reservation.Id)).ToDictionaryAsync(reservation => reservation.Id, token);
            foreach (var item in plan)
            {
                var reservation = reservations[item.Allocation.ReservationId];
                reservation.Allocations.Single(allocation => allocation.Id == item.Allocation.AllocationId).Consume(item.Quantity);
                db.Add(ShipmentReservationConsumption.Create(item.Line.Id, item.Allocation.AllocationId, item.Quantity, context.OperationId));
            }
            foreach (var reservation in plan.Select(item => reservations[item.Allocation.ReservationId]).Distinct())
            {
                reservation.DeriveState();
                db.Entry(reservation).Property(value => value.State).IsModified = true;
            }
            foreach (var group in lines.GroupBy(line => line.CustomerOrderLineId))
                order.Lines.Single(line => line.Id == group.Key).Ship(group.Sum(line => line.Quantity));
            order.DeriveFulfilment();
            // Siempre, aunque no se consuma ninguna reserva: cambia lo servido (regla 4, FR-012).
            db.Entry(order).Property(value => value.State).IsModified = true;
            // Convergence C1: declara en la misma sentencia el número y la suma de los consumos que inserta.
            shipment.Confirm(context.OperationId, context.ActorId, DateTimeOffset.UtcNow, plan.Count, plan.Sum(item => item.Quantity));

            return OperationResponse.Json(200, new ShipmentConfirmation(context.OperationId, id, outcome.MovementId, order.Id,
                order.State.ToString(), lines.Select(line => new ShipmentConfirmedLine(line.Id, ShipmentView.Quantity(line.Quantity),
                    ShipmentView.Quantity(consumed[line.Id]))).ToList()), id);
        }, cancellation);
    }

    internal sealed record Line(Guid CustomerOrderLineId, Guid LocationId, Guid LotId, decimal Quantity);

    internal sealed record Values(Guid? OrderId, DateTimeOffset OccurredAt, IReadOnlyList<Line> Lines);

    /// <summary>Validación de forma: reúne todos los errores antes de responder (D-02).</summary>
    internal static Values Validate(ShipmentWrite write, bool requireOrder)
    {
        var errors = new Dictionary<string, string[]>();
        Guid? order = null;
        if (write.CustomerOrderId is null)
        {
            if (requireOrder) errors["customerOrderId"] = ["Elige el pedido de la salida."];
        }
        else if (Guid.TryParse(write.CustomerOrderId, out var parsed) && parsed != Guid.Empty) order = parsed;
        else errors["customerOrderId"] = ["El pedido no es válido."];

        var occurredAt = default(DateTimeOffset);
        if (!ShipmentWrite.TryInstant(write.OccurredAt, out occurredAt))
            errors["occurredAt"] = ["Indica la fecha y hora en que salió la mercancía."];
        else if (occurredAt > DateTimeOffset.UtcNow + AllowedClockSkew)
            errors["occurredAt"] = ["La fecha y hora del hecho no puede ser posterior al registro."];

        var requested = write.Lines ?? [];
        if (requested.Count == 0) errors["lines"] = ["Añade al menos una línea."];
        else if (requested.Count > Shipment.MaxLines) errors["lines"] = [$"La salida admite como máximo {Shipment.MaxLines} líneas."];
        var lines = new List<Line>();
        var buckets = new HashSet<(Guid, Guid, Guid)>();
        for (var index = 0; index < requested.Count; index++)
        {
            var line = requested[index];
            var orderLine = Id(line.CustomerOrderLineId, $"lines[{index}].customerOrderLineId", "Elige la línea del pedido.", errors);
            var location = Id(line.LocationId, $"lines[{index}].locationId", "Elige la ubicación de origen.", errors);
            var lot = Id(line.LotId, $"lines[{index}].lotId", "Elige el lote (o «sin lote»).", errors);
            var quantityProblem = !ShipmentWrite.TryQuantity(line.Quantity, out var quantity) ? "Indica una cantidad numérica."
                : quantity <= 0 ? "La cantidad debe ser mayor que cero."
                : decimal.Round(quantity, 3) != quantity ? "La cantidad admite como máximo tres decimales."
                : quantity >= 1_000_000_000_000_000m ? "La cantidad es demasiado grande."
                : null;
            if (quantityProblem is not null) errors[$"lines[{index}].quantity"] = [quantityProblem];
            if (orderLine is null || location is null || lot is null || quantityProblem is not null) continue;
            if (!buckets.Add((orderLine.Value, location.Value, lot.Value)))
                errors["lines"] = ["Cada línea de pedido solo puede salir una vez de cada ubicación y lote."];
            lines.Add(new(orderLine.Value, location.Value, lot.Value, quantity));
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa los datos marcados de la salida.", errors);
        return new(order, occurredAt, lines);
    }

    private static Guid? Id(string? value, string field, string message, Dictionary<string, string[]> errors)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty) return id;
        errors[field] = [message];
        return null;
    }

    /// <summary>Líneas de pedido pedidas (relación inmutable con su artículo), leídas antes de bloquear.</summary>
    private static async Task<Dictionary<Guid, CustomerOrderLine>> OrderLinesAsync(InventoryDbContext db, Values values,
        CancellationToken token)
    {
        var ids = values.Lines.Select(line => line.CustomerOrderLineId).Distinct().ToList();
        return await db.Set<CustomerOrderLine>().AsNoTracking().Where(line => ids.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, token);
    }

    private static async Task<CustomerOrder> OpenOrderAsync(InventoryDbContext db, Guid id, CancellationToken token)
    {
        var order = await db.Set<CustomerOrder>().AsNoTracking().Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == id, token)
            ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
        if (order.State != CustomerOrderState.Open)
            throw new InventoryProblemException(409, "CustomerOrderNotOpen",
                $"El pedido {order.ExternalReference} no está abierto: no admite nuevas salidas.");
        return order;
    }

    private static async Task<Shipment> DraftAsync(InventoryDbContext db, Guid id, string? ifMatch, CancellationToken token)
    {
        var shipment = await db.Set<Shipment>().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == id, token)
            ?? throw new InventoryProblemException(404, "NotFound", "La salida no existe.");
        if (shipment.State != ShipmentState.Draft)
            throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La salida ya está confirmada y no admite cambios.");
        EntityTags.RequireMatch(ifMatch, shipment.Version);
        return shipment;
    }

    /// <summary>
    /// Reglas bajo bloqueo (D-02): línea del pedido, ubicación y lote del artículo (422); bucket con
    /// existencias (409 InsufficientStock); fracciones y Σ por línea de pedido ≤ pendiente (422).
    /// </summary>
    private static async Task<List<ShipmentLineRequest>> ResolveAsync(InventoryDbContext db, CustomerOrder order, Values values,
        Dictionary<Guid, CustomerOrderLine> orderLines, CancellationToken token)
    {
        var locationIds = values.Lines.Select(line => line.LocationId).Distinct().ToList();
        var lotIds = values.Lines.Select(line => line.LotId).Distinct().ToList();
        var locations = await db.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
            .Select(location => location.Id).ToListAsync(token);
        var lots = await db.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id)).ToDictionaryAsync(lot => lot.Id, token);

        var errors = new Dictionary<string, string[]>();
        for (var index = 0; index < values.Lines.Count; index++)
        {
            var line = values.Lines[index];
            if (!orderLines.TryGetValue(line.CustomerOrderLineId, out var orderLine) || orderLine.CustomerOrderId != order.Id)
            {
                errors[$"lines[{index}].customerOrderLineId"] = ["La línea no pertenece al pedido."];
                continue;
            }
            if (!locations.Contains(line.LocationId)) errors[$"lines[{index}].locationId"] = ["La ubicación no existe."];
            if (!lots.TryGetValue(line.LotId, out var lot) || lot.ArticleId != orderLine.ArticleId)
                errors[$"lines[{index}].lotId"] = ["El lote no pertenece al artículo de la línea."];
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa las líneas marcadas de la salida.", errors);

        var articleIds = orderLines.Values.Select(line => line.ArticleId).Distinct().ToList();
        var articles = await db.Articles.AsNoTracking().Where(article => articleIds.Contains(article.Id))
            .ToDictionaryAsync(article => article.Id, token);
        var balances = await db.Set<StockBalance>().AsNoTracking()
            .Where(balance => articleIds.Contains(balance.ArticleId) && locationIds.Contains(balance.LocationId)
                && lotIds.Contains(balance.LotId) && balance.Physical > 0)
            .Select(balance => new { balance.ArticleId, balance.LocationId, balance.LotId }).ToListAsync(token);
        var stocked = balances.Select(balance => (balance.ArticleId, balance.LocationId, balance.LotId)).ToHashSet();
        var empty = new Dictionary<string, string[]>();
        for (var index = 0; index < values.Lines.Count; index++)
        {
            var line = values.Lines[index];
            if (!stocked.Contains((orderLines[line.CustomerOrderLineId].ArticleId, line.LocationId, line.LotId)))
                empty[$"lines[{index}].locationId"] = ["No hay existencias del artículo en esta ubicación y lote."];
        }
        if (empty.Count > 0)
            throw new InventoryProblemException(409, "InsufficientStock",
                "Alguna línea sale de una ubicación y lote sin existencias del artículo.", empty);

        for (var index = 0; index < values.Lines.Count; index++)
        {
            var line = values.Lines[index];
            var article = articles[orderLines[line.CustomerOrderLineId].ArticleId];
            if (!article.AllowsFractional && decimal.Truncate(line.Quantity) != line.Quantity)
                errors[$"lines[{index}].quantity"] = ["Este artículo solo admite cantidades enteras."];
        }
        foreach (var group in values.Lines.Select((line, index) => (line, index)).GroupBy(pair => pair.line.CustomerOrderLineId))
        {
            var pending = order.Lines.Single(line => line.Id == group.Key).PendingQuantity;
            if (group.Sum(pair => pair.line.Quantity) <= pending) continue;
            foreach (var (_, index) in group)
                errors.TryAdd($"lines[{index}].quantity",
                    [$"La suma de esta línea de pedido supera su pendiente ({ShipmentView.Quantity(pending)})."]);
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa las cantidades marcadas de la salida.", errors);

        return values.Lines.Select(line => new ShipmentLineRequest(line.CustomerOrderLineId,
            orderLines[line.CustomerOrderLineId].ArticleId, line.LocationId, line.LotId, line.Quantity)).ToList();
    }

    private static OperationResponse Mutation(int status, OperationContext context, Shipment shipment, bool withLocation)
    {
        var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(shipment.Version) };
        if (withLocation) headers["Location"] = $"{Resource}/{shipment.Id:D}";
        return OperationResponse.Json(status, new ShipmentMutation(context.OperationId, ShipmentView.From(shipment, [])),
            shipment.Id, headers);
    }

    private static JsonElement Fingerprint(ShipmentWrite write) =>
        JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions);
    // Posible = mín(físico, propia consumible + disponible), leído de la base bajo los bloqueos ya
    // tomados: AsNoTracking ignora lo que ApplyAsync haya aplicado en memoria antes de rechazar.
    private static async Task<InventoryProblemException> WithPossibleQuantityAsync(InventoryDbContext db,
        InventoryProblemException problem, IReadOnlyList<ShipmentLine> lines, IReadOnlyDictionary<Guid, decimal> consumed,
        CancellationToken token)
    {
        var articleIds = lines.Select(line => line.ArticleId).Distinct().ToList();
        var locationIds = lines.Select(line => line.LocationId).Distinct().ToList();
        var balances = await db.Set<StockBalance>().AsNoTracking()
            .Where(balance => articleIds.Contains(balance.ArticleId) && locationIds.Contains(balance.LocationId))
            .ToListAsync(token);
        var articles = await db.Articles.AsNoTracking().Where(article => articleIds.Contains(article.Id))
            .ToDictionaryAsync(article => article.Id, token);
        var locations = await db.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Code, token);
        var parts = new List<string>();
        foreach (var line in lines)
        {
            var balance = balances.SingleOrDefault(value =>
                value.ArticleId == line.ArticleId && value.LocationId == line.LocationId && value.LotId == line.LotId);
            var possible = balance is null ? 0m : Math.Max(0m, Math.Min(balance.Physical, consumed[line.Id] + balance.Available));
            if (line.Quantity <= possible) continue;
            var article = articles[line.ArticleId];
            parts.Add($"Se pueden servir como máximo {ShipmentView.Quantity(possible)} {article.BaseUnit} de {article.Sku} "
                + $"en {locations[line.LocationId]}.");
        }
        return parts.Count == 0 ? problem
            : new InventoryProblemException(problem.Status, problem.Code, $"{problem.Message} {string.Join(" ", parts)}", problem.Errors);
    }
}
