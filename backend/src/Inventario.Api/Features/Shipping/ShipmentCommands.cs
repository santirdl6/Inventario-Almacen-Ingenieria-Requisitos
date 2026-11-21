using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
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
}
