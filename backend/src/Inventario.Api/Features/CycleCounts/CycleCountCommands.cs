using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
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

    /// <summary>
    /// PUT /cycle-counts/{id}/lines (T008; FR-004–FR-007, US5): registra el contado (cero explícito,
    /// fracciones según el artículo) y añade hallazgos en el alcance, con lote si el artículo lo gestiona.
    /// Bloquea cycle-count:{id} y los artículos de los hallazgos, cuya primera referencia marca (D-03, D-08).
    /// </summary>
    public Task<OperationResponse> RecordLinesAsync(Guid operation, Guid id, CycleCountLinesWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "PUT", $"{Resource}/{id:D}/lines", RecordPermission, write.Fingerprint());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var (counted, found) = ValidateLines(write);
            var db = context.Database;
            var foundArticles = found.Select(item => item.ArticleId).Distinct().ToList();
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            await context.Locks.ArticlesAsync(foundArticles, token);

            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.Counting);
            var lines = count.Lines.ToDictionary(line => line.Id);
            var articleIds = lines.Values.Where(line => counted.Any(item => item.LineId == line.Id)).Select(line => line.ArticleId)
                .Concat(foundArticles).Distinct().ToList();
            var articles = await db.Articles.AsNoTracking().Where(article => articleIds.Contains(article.Id))
                .ToDictionaryAsync(article => article.Id, token);
            var scope = count.Locations.Select(location => location.LocationId).ToHashSet();
            var errors = new Dictionary<string, string[]>();

            for (var index = 0; index < counted.Count; index++)
            {
                var item = counted[index];
                if (!lines.TryGetValue(item.LineId, out var line))
                {
                    errors[$"counted[{index}].lineId"] = ["La línea no pertenece al conteo."];
                    continue;
                }
                if (!articles[line.ArticleId].AllowsFractional && decimal.Truncate(item.Quantity) != item.Quantity)
                    errors[$"counted[{index}].countedPhysical"] = ["Este artículo solo admite cantidades enteras."];
                else if (line.IsFound && item.Quantity == 0m)
                    errors[$"counted[{index}].countedPhysical"] = ["Un hallazgo tiene una cantidad mayor que cero; retíralo si no existe."];
            }

            var buckets = count.Lines.Select(line => (line.ArticleId, line.LocationId, line.LotCode)).ToHashSet();
            var resolved = new List<(FoundItem Item, string LotCode)>();
            for (var index = 0; index < found.Count; index++)
            {
                var item = found[index];
                if (!articles.TryGetValue(item.ArticleId, out var article))
                {
                    errors[$"found[{index}].articleId"] = ["El artículo no existe."];
                    continue;
                }
                if (!scope.Contains(item.LocationId))
                    errors[$"found[{index}].locationId"] = ["La ubicación no está en el alcance del conteo."];
                string? lotCode;
                if (article.TracksLot)
                {
                    lotCode = item.LotCode is null or Lot.UntrackedCode ? null : item.LotCode;
                    if (lotCode is null) errors[$"found[{index}].lotCode"] = [$"El artículo {article.Sku} exige lote."];
                }
                else if (item.LotCode is null or Lot.UntrackedCode) lotCode = Lot.UntrackedCode;
                else
                {
                    lotCode = null;
                    errors[$"found[{index}].lotCode"] = [$"El artículo {article.Sku} no tiene seguimiento de lote."];
                }
                if (!article.AllowsFractional && decimal.Truncate(item.Quantity) != item.Quantity)
                    errors[$"found[{index}].countedPhysical"] = ["Este artículo solo admite cantidades enteras."];
                if (lotCode is null || !scope.Contains(item.LocationId)) continue;
                if (!buckets.Add((item.ArticleId, item.LocationId, lotCode)))
                    errors[$"found[{index}].articleId"] = ["Ese artículo, ubicación y lote ya tienen línea: registra su contado."];
                else resolved.Add((item, lotCode));
            }
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa las líneas marcadas del conteo.", errors);

            var codes = resolved.Select(value => value.LotCode).Distinct().ToList();
            var lots = await db.Set<Lot>().AsNoTracking().Where(lot => foundArticles.Contains(lot.ArticleId) && codes.Contains(lot.Code))
                .Select(lot => new { lot.Id, lot.ArticleId, lot.Code }).ToListAsync(token);
            await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, foundArticles, token);
            var now = DateTimeOffset.UtcNow;
            foreach (var item in counted)
                count.RecordCounted(item.LineId, item.Quantity, context.ActorId, now);
            foreach (var (item, lotCode) in resolved)
                count.AddFound(item.ArticleId, item.LocationId,
                    lots.SingleOrDefault(lot => lot.ArticleId == item.ArticleId && lot.Code == lotCode)?.Id, lotCode, item.Quantity,
                    context.ActorId, now);
            Touch(db, count);
            await db.SaveChangesAsync(token);
            return Mutation(200, context, count, withLocation: false);
        }, cancellation);
    }

    /// <summary>
    /// DELETE /cycle-counts/{id}/lines/{lineId} (T008; US5, INT-HU04-01): retira un hallazgo antes de
    /// enviar y con él su referencia al artículo. Las líneas esperadas no se retiran (422; 51403 en SQL).
    /// </summary>
    public Task<OperationResponse> RemoveLineAsync(Guid operation, Guid id, Guid lineId, string? ifMatch, CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "DELETE", $"{Resource}/{id:D}/lines/{lineId:D}", RecordPermission, Empty());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            // El artículo de una línea no cambia: se lee bajo cycle-count:{id} y se bloquea antes de decidir.
            var article = await db.Set<CountLine>().AsNoTracking().Where(line => line.Id == lineId && line.CountId == id)
                .Select(line => (Guid?)line.ArticleId).SingleOrDefaultAsync(token);
            if (article is { } articleId) await context.Locks.ArticlesAsync([articleId], token);

            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.Counting);
            var line = count.Lines.SingleOrDefault(value => value.Id == lineId)
                ?? throw new InventoryProblemException(404, "NotFound", "La línea no pertenece al conteo.");
            if (!line.IsFound)
                throw InventoryProblemException.Validation("lineId", "Solo se retiran hallazgos; las líneas esperadas se cuentan, también con cero.");
            count.RemoveFound(lineId);
            Touch(db, count);
            await db.SaveChangesAsync(token);
            return new OperationResponse(204, null, new Dictionary<string, string> { ["ETag"] = EntityTags.Format(count.Version) }, id);
        }, cancellation);
    }

    /// <summary>POST /cycle-counts/{id}/submit (T008; FR-007): Counting → AwaitingApproval con todo contado.</summary>
    public Task<OperationResponse> SubmitAsync(Guid operation, Guid id, string? ifMatch, CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "POST", $"{Resource}/{id:D}/submit", RecordPermission, Empty());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.Counting);
            // Mismo orden que el detalle (líneas por Id) para que el índice señale la fila correcta.
            var errors = count.Lines.OrderBy(line => line.Id).Select((line, index) => (line, index))
                .Where(pair => pair.line.CountedPhysical is null)
                .ToDictionary(pair => $"lines[{pair.index}].countedPhysical", _ => new[] { "Falta el contado de esta línea (el cero se escribe)." });
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Hay líneas sin contar.", errors);
            count.Submit(context.ActorId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(token);
            return Mutation(200, context, count, withLocation: false);
        }, cancellation);
    }

    /// <summary>
    /// PUT /cycle-counts/{id}/review (T009, T012; FR-008, FR-018–FR-020; D-07, D-09): Inventory.Adjust fija o
    /// retira el motivo de las líneas con diferencia y, opcional, la salida confirmada que corrigen, sin
    /// cambiar el contado. Solo bloquea cycle-count:{id} (D-03): la salida confirmada es inmutable (triggers
    /// de HU-03) y se lee sin bloquearla. El vínculo no modifica la salida ni el pedido (P-07).
    /// </summary>
    public Task<OperationResponse> ReviewAsync(Guid operation, Guid id, CycleCountReviewWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "PUT", $"{Resource}/{id:D}/review", AdjustPermission, write.Fingerprint());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var errors = new Dictionary<string, string[]>();
            var requested = write.Lines ?? [];
            if (requested.Count == 0) errors["lines"] = ["Indica al menos una línea."];
            var items = new List<(int Index, Guid LineId, string? Reason, Guid? Shipment)>();
            for (var index = 0; index < requested.Count; index++)
            {
                var line = Id(requested[index].LineId, $"lines[{index}].lineId", "La línea no es válida.", errors);
                Guid? shipment = null;
                if (requested[index].CorrectsShipmentId is { } text)
                    shipment = Id(text, $"lines[{index}].correctsShipmentId", "La salida no es válida.", errors);
                if (requested[index].Reason is { Length: > CountLine.ReasonMaxLength })
                    errors[$"lines[{index}].reason"] = [$"El motivo no puede superar {CountLine.ReasonMaxLength} caracteres."];
                else if (line is not null && (requested[index].CorrectsShipmentId is null || shipment is not null))
                    items.Add((index, line.Value, requested[index].Reason, shipment));
            }
            if (items.Select(item => item.LineId).Distinct().Count() != items.Count)
                errors["lines"] = ["Cada línea solo puede aparecer una vez."];
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa los motivos marcados.", errors);

            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.AwaitingApproval);
            // D-09: salida confirmada con alguna línea en el mismo artículo, ubicación y lote.
            var shipmentIds = items.Where(item => item.Shipment is not null).Select(item => item.Shipment!.Value).Distinct().ToList();
            var shipments = await db.Set<Shipment>().AsNoTracking().Include(value => value.Lines)
                .Where(value => shipmentIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, token);
            foreach (var (index, lineId, reason, shipmentId) in items)
            {
                var line = count.Lines.SingleOrDefault(value => value.Id == lineId);
                if (line is null)
                {
                    errors[$"lines[{index}].lineId"] = ["La línea no pertenece al conteo."];
                    continue;
                }
                if (reason is not null && !line.HasDifference)
                    errors[$"lines[{index}].reason"] = ["Solo las líneas con diferencia llevan motivo."];
                if (shipmentId is not { } target) continue;
                if (!line.HasDifference)
                    errors[$"lines[{index}].correctsShipmentId"] = ["Solo una línea con diferencia corrige una salida."];
                else if (!shipments.TryGetValue(target, out var shipment) || shipment.State != ShipmentState.Confirmed)
                    errors[$"lines[{index}].correctsShipmentId"] = ["Elige una salida confirmada."];
                else if (!shipment.Lines.Any(value => value.ArticleId == line.ArticleId && value.LocationId == line.LocationId
                             && value.LotId == line.LotId))
                    errors[$"lines[{index}].correctsShipmentId"] = ["Esa salida no sacó este artículo de esta ubicación y lote."];
            }
            if (errors.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa los motivos marcados.", errors);
            foreach (var (_, lineId, reason, shipmentId) in items)
                count.Review(lineId, reason, context.ActorId, shipmentId);
            Touch(db, count);
            await db.SaveChangesAsync(token);
            return Mutation(200, context, count, withLocation: false);
        }, cancellation);
    }

    /// <summary>
    /// POST /cycle-counts/{id}/confirm (T009; FR-009–FR-011; D-03, D-05–D-07, D-13). Bloquea
    /// cycle-count:{id}, todas las ubicaciones y los artículos de todas las líneas; exige motivo en cada
    /// diferencia; revalida el teórico (409 CountTheoreticalChanged) y el reservado (409
    /// ReservedStockConflict) conservando la congelación; aplica un único movimiento Adjustment con
    /// StockCore y libera el alcance con la aprobación. No modifica líneas. Sin SaveChanges aquí: el
    /// ejecutor guarda todo con el OperationResult (FK del diario).
    /// </summary>
    public Task<OperationResponse> ConfirmAsync(Guid operation, Guid id, string? ifMatch, CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "POST", $"{Resource}/{id:D}/confirm", AdjustPermission, Empty());
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            // Alcance y líneas solo cambian bajo cycle-count:{id}: leídos ya bajo ese bloqueo.
            var scope = await db.Set<CountLocation>().AsNoTracking().Where(location => location.CountId == id)
                .Select(location => location.LocationId).ToListAsync(token);
            var lineArticles = await db.Set<CountLine>().AsNoTracking().Where(line => line.CountId == id)
                .Select(line => line.ArticleId).Distinct().ToListAsync(token);
            await context.Locks.LocationsAsync(scope, token);
            await context.Locks.ArticlesAsync(lineArticles, token);

            var count = await LoadAsync(db, id, ifMatch, token, CycleCountState.AwaitingApproval);
            var lines = count.Lines.OrderBy(line => line.Id).ToList();
            var missing = lines.Select((line, index) => (line, index)).Where(pair => pair.line.HasDifference && pair.line.Reason is null)
                .ToDictionary(pair => $"lines[{pair.index}].reason", _ => new[] { "Indica el motivo de esta diferencia." });
            if (missing.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Cada diferencia necesita su motivo.", missing);

            // D-05: teórico y reservado actuales por bucket, leídos tras los bloqueos.
            var articleIds = lines.Select(line => line.ArticleId).Distinct().ToList();
            var balances = await (
                    from balance in db.Set<StockBalance>().AsNoTracking()
                    join lot in db.Set<Lot>().AsNoTracking() on balance.LotId equals lot.Id
                    where articleIds.Contains(balance.ArticleId) && scope.Contains(balance.LocationId)
                    select new { balance.ArticleId, balance.LocationId, LotCode = lot.Code, balance.Physical, balance.Reserved })
                .ToListAsync(token);
            var names = await BucketNamesAsync(db, articleIds, scope, token);
            foreach (var line in lines)
            {
                var balance = balances.SingleOrDefault(value => value.ArticleId == line.ArticleId && value.LocationId == line.LocationId
                    && value.LotCode == line.LotCode);
                var physical = balance?.Physical ?? 0m;
                if (physical != line.TheoreticalPhysical)
                    throw new InventoryProblemException(409, "CountTheoreticalChanged",
                        $"El físico de {names(line)} es {CycleCountView.Quantity(physical)} y al iniciar era "
                        + $"{CycleCountView.Quantity(line.TheoreticalPhysical)}: alguien lo movió sin respetar el conteo. Revísalo antes de confirmar.");
            }
            foreach (var line in lines)
            {
                var reserved = balances.SingleOrDefault(value => value.ArticleId == line.ArticleId && value.LocationId == line.LocationId
                    && value.LotCode == line.LotCode)?.Reserved ?? 0m;
                if (line.CountedPhysical < reserved)
                    throw new InventoryProblemException(409, "ReservedStockConflict",
                        $"En {names(line)} el contado ({CycleCountView.Quantity(line.CountedPhysical)}) es menor que lo reservado "
                        + $"({CycleCountView.Quantity(reserved)}). Libera antes la reserva necesaria; el conteo sigue abierto.");
            }

            var adjusted = lines.Where(line => line.HasDifference).ToList();
            Guid? movementId = null;
            if (adjusted.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var command = StockCore.PrepareAdjustment(new StockCommandContext(
                        new StockDocumentRef(CycleCountDocuments.CycleCount, id), now, AdjustmentReason,
                        SupportReference: count.CountCode, CounterpartyNotApplicableReason: AdjustmentCounterparty),
                    adjusted.Select(line => new AdjustmentAllocation(line.ArticleId, line.LocationId, line.LotCode, line.Difference!.Value))
                        .ToList());
                movementId = (await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token)).MovementId;
            }
            count.Confirm(context.OperationId, movementId, context.ActorId, DateTimeOffset.UtcNow);
            // P-07: las salidas corregidas se informan, pero ni ellas ni su pedido se tocan (OrderChanged = false).
            var linked = adjusted.Where(line => line.CorrectsShipmentId is not null).ToList();
            var linkedIds = linked.Select(line => line.CorrectsShipmentId!.Value).Distinct().ToList();
            var orders = await db.Set<Shipment>().AsNoTracking().Where(value => linkedIds.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, value => value.CustomerOrderId, token);
            return OperationResponse.Json(200, new CycleCountConfirmation(context.OperationId, id, movementId,
                adjusted.Select(line => new AdjustedLine(line.Id, CycleCountView.Quantity(line.Difference!.Value))).ToList(),
                linked.Select(line => new ShipmentCorrection(line.Id, line.CorrectsShipmentId!.Value, orders[line.CorrectsShipmentId!.Value],
                    OrderChanged: false, ShipmentCorrections.Notice)).ToList()), id);
        }, cancellation);
    }

    /// <summary>
    /// POST /cycle-counts/{id}/cancel (T009; FR-012): Inventory.Adjust cancela un conteo abierto con motivo,
    /// conserva sus líneas y libera el alcance, sin efectos en stock. Bloquea todas las ubicaciones (D-03).
    /// </summary>
    public Task<OperationResponse> CancelAsync(Guid operation, Guid id, CycleCountCancelWrite write, string? ifMatch,
        CancellationToken cancellation)
    {
        var request = new OperationRequest(operation, "POST", $"{Resource}/{id:D}/cancel", AdjustPermission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            if (write.Reason is null)
                throw InventoryProblemException.Validation("reason", "Indica el motivo de la cancelación.");
            if (write.Reason.Length > CycleCount.CancelReasonMaxLength)
                throw InventoryProblemException.Validation("reason", $"El motivo no puede superar {CycleCount.CancelReasonMaxLength} caracteres.");
            var db = context.Database;
            await context.Locks.DocumentsAsync([new DocumentLock(CycleCountDocuments.CycleCount, id)], token);
            var scope = await db.Set<CountLocation>().AsNoTracking().Where(location => location.CountId == id)
                .Select(location => location.LocationId).ToListAsync(token);
            await context.Locks.LocationsAsync(scope, token);
            var count = await LoadAsync(db, id, ifMatch, token,
                CycleCountState.Draft, CycleCountState.Counting, CycleCountState.AwaitingApproval);
            count.Cancel(context.OperationId, write.Reason, context.ActorId, DateTimeOffset.UtcNow);
            return OperationResponse.Json(200, new CycleCountCancellation(context.OperationId, id), id);
        }, cancellation);
    }

    private const string AdjustmentReason = "Ajuste por conteo cíclico";
    private const string AdjustmentCounterparty = "Ajuste de inventario: sin contraparte externa";

    /// <summary>Texto «SKU en ubicación, lote L» de cada bucket para los mensajes de conflicto.</summary>
    private static async Task<Func<CountLine, string>> BucketNamesAsync(InventoryDbContext db, IReadOnlyList<Guid> articleIds,
        IReadOnlyList<Guid> locationIds, CancellationToken token)
    {
        var skus = await db.Articles.AsNoTracking().Where(article => articleIds.Contains(article.Id))
            .ToDictionaryAsync(article => article.Id, article => article.Sku, token);
        var codes = await db.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Code, token);
        return line => $"{skus[line.ArticleId]} en {codes[line.LocationId]}, lote {line.LotCode}";
    }

    internal sealed record CountedItem(Guid LineId, decimal Quantity);

    internal sealed record FoundItem(Guid ArticleId, Guid LocationId, string? LotCode, decimal Quantity);

    /// <summary>Forma del contado y los hallazgos; todos los errores juntos.</summary>
    internal static (List<CountedItem> Counted, List<FoundItem> Found) ValidateLines(CycleCountLinesWrite write)
    {
        var errors = new Dictionary<string, string[]>();
        var counted = new List<CountedItem>();
        var found = new List<FoundItem>();
        var requestedCounted = write.Counted ?? [];
        var requestedFound = write.Found ?? [];
        if (requestedCounted.Count == 0 && requestedFound.Count == 0)
            errors["counted"] = ["Registra al menos un contado o un hallazgo."];
        for (var index = 0; index < requestedCounted.Count; index++)
        {
            var item = requestedCounted[index];
            var line = Id(item.LineId, $"counted[{index}].lineId", "La línea no es válida.", errors);
            var quantity = Quantity(item.CountedPhysical, $"counted[{index}].countedPhysical", allowZero: true, errors);
            if (line is not null && quantity is not null) counted.Add(new(line.Value, quantity.Value));
        }
        if (counted.Select(item => item.LineId).Distinct().Count() != counted.Count)
            errors["counted"] = ["Cada línea solo puede aparecer una vez."];
        for (var index = 0; index < requestedFound.Count; index++)
        {
            var item = requestedFound[index];
            var article = Id(item.ArticleId, $"found[{index}].articleId", "Elige el artículo.", errors);
            var location = Id(item.LocationId, $"found[{index}].locationId", "Elige la ubicación.", errors);
            var quantity = Quantity(item.CountedPhysical, $"found[{index}].countedPhysical", allowZero: false, errors);
            string? lot = null;
            try { lot = Lot.NormalizeCode(item.LotCode); }
            catch (ArgumentException error) { errors[$"found[{index}].lotCode"] = [error.Message]; }
            if (article is not null && location is not null && quantity is not null)
                found.Add(new(article.Value, location.Value, lot, quantity.Value));
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa las líneas marcadas del conteo.", errors);
        return (counted, found);
    }

    private static Guid? Id(string? value, string field, string message, Dictionary<string, string[]> errors)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty) return id;
        errors[field] = [message];
        return null;
    }

    private static decimal? Quantity(string? value, string field, bool allowZero, Dictionary<string, string[]> errors)
    {
        var problem = !CycleCountJson.TryQuantity(value, out var quantity) ? "Indica una cantidad numérica."
            : quantity < 0 ? "La cantidad no puede ser negativa."
            : !allowZero && quantity == 0 ? "Un hallazgo tiene una cantidad mayor que cero."
            : decimal.Round(quantity, 3) != quantity ? "La cantidad admite como máximo tres decimales."
            : quantity >= 1_000_000_000_000_000m ? "La cantidad es demasiado grande."
            : null;
        if (problem is null) return quantity;
        errors[field] = [problem];
        return null;
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
