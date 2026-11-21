using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Purchasing.Queries;

public sealed class PurchaseOrderQueries(InventoryDbContext database)
{
    public Task<PurchaseOrderPage> ListAsync(PurchaseOrderListOptions options, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var snapshot = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var query = database.Set<PurchaseOrder>().AsNoTracking();
            if (options.Query is { } prefix) query = query.Where(value => value.Number.StartsWith(prefix));
            if (options.SupplierId is { } supplier) query = query.Where(value => value.SupplierId == supplier);
            if (options.State is { } state) query = query.Where(value => value.State == state);
            if (options.From is { } from) query = query.Where(value => value.CreatedAt >= from);
            if (options.To is { } to) query = query.Where(value => value.CreatedAt < to);
            var count = await query.CountAsync(cancellation);
            // Paginate narrow indexed keys before fetching snapshots, avoiding a lookup for every skipped row.
            var pageIds = query.OrderBy(value => value.Number).ThenBy(value => value.Id)
                .Skip(options.Offset).Take(options.PageSize).Select(value => value.Id);
            var rows = await database.Set<PurchaseOrder>().AsNoTracking().Where(value => pageIds.Contains(value.Id))
                .OrderBy(value => value.Number).ThenBy(value => value.Id)
                .Select(value => new { value.Id, value.Number, value.SupplierId, value.SupplierSnapshot,
                    value.ExpectedDeliveryDate, value.CreatedAt, value.State, value.SendStatus }).ToListAsync(cancellation);
            var result = new PurchaseOrderPage(rows.Select(value => new PurchaseOrderSummary(value.Id, value.Number, value.SupplierId,
                JsonSerializer.Deserialize<PurchaseSupplierSnapshot>(value.SupplierSnapshot, PurchaseSnapshotJson.Options)!,
                value.ExpectedDeliveryDate, value.CreatedAt, value.State.ToString(), value.SendStatus.ToString())).ToArray(), options.Page, options.PageSize, count);
            await snapshot.CommitAsync(cancellation); return result;
        });

    public Task<(PurchaseOrderDetail Resource, string ETag)> DetailAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var snapshot = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
        var order = await RequiredAsync(id, cancellation);
        var delivery = await database.Set<PurchaseOrderDelivery>().AsNoTracking()
            .Where(value => value.PurchaseOrderId == id).Select(value => (Guid?)value.Id).SingleOrDefaultAsync(cancellation);
        var result = (PurchaseOrderDetail.From(order, delivery), EntityTags.Format(order.Version));
        await snapshot.CommitAsync(cancellation);
        return result;
    });

    public Task<object> ContextAsync(Guid id, CancellationToken cancellation) => database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var snapshot = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
        var order = await RequiredAsync(id, cancellation);
        var supplier = await database.Set<Supplier>().AsNoTracking().SingleAsync(value => value.Id == order.SupplierId, cancellation);
        var ids = order.Lines.Select(value => value.ArticleId).Distinct().ToArray();
        var articles = await database.Articles.AsNoTracking().Where(value => ids.Contains(value.Id)).ToListAsync(cancellation);
        var send = new List<string>();
        if (!order.IsEditable) send.Add("El envío ya se solicitó; consulta su seguimiento.");
        if (!supplier.IsActive) send.Add("El proveedor está inactivo.");
        if (string.IsNullOrWhiteSpace(supplier.Email)) send.Add("Completa el correo del proveedor antes de enviar.");
        if (articles.Any(value => !value.IsActive)) send.Add("El pedido contiene artículos inactivos.");
        var result = new
        {
            purchaseOrderVersion = EntityTags.Format(order.Version),
            supplier = new { supplier.Id, supplier.Code, supplier.Name, supplier.Email, supplier.IsActive, version = EntityTags.Format(supplier.Version) },
            articles = articles.Select(value => new { value.Id, value.IsActive, version = EntityTags.Format(value.Version) }).ToArray(),
            editBlockReasons = order.IsEditable ? Array.Empty<string>() : ["El pedido quedó congelado al solicitar envío."],
            sendBlockReasons = send.ToArray()
        };
        await snapshot.CommitAsync(cancellation); return (object)result;
    });

    private async Task<PurchaseOrder> RequiredAsync(Guid id, CancellationToken cancellation) =>
        await database.Set<PurchaseOrder>().AsNoTracking().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == id, cancellation)
        ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
}

public sealed record PurchaseOrderListOptions(string? Query, Guid? SupplierId, PurchaseOrderState? State,
    DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize)
{
    public int Offset => checked((Page - 1) * PageSize);
    public static PurchaseOrderListOptions Parse(string? q, Guid? supplierId, string? state, string? from, string? to, int? page, int? pageSize)
    {
        var number = page ?? 1; var size = pageSize ?? 25;
        if (number < 1) throw InventoryProblemException.Validation("page", "La página empieza en 1.");
        if (size is < 1 or > 100) throw InventoryProblemException.Validation("pageSize", "El tamaño de página debe estar entre 1 y 100.");
        if ((long)(number - 1) * size > int.MaxValue) throw InventoryProblemException.Validation("page", "La página excede el intervalo admitido.");
        PurchaseOrderState? selected = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<PurchaseOrderState>(state, out var value) || !Enum.IsDefined(value) || value.ToString() != state)
                throw InventoryProblemException.Validation("state", "Elige un estado logístico válido.");
            selected = value;
        }
        static DateTimeOffset? Moment(string? raw, string field)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!Regex.IsMatch(raw, @"^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
                || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                throw InventoryProblemException.Validation(field, "Indica un instante ISO con zona horaria (UTC o desplazamiento explícito).");
            return value.ToUniversalTime();
        }
        var start = Moment(from, "from"); var end = Moment(to, "to");
        if (start is not null && end is not null && start >= end)
            throw InventoryProblemException.Validation("to", "El final exclusivo debe ser posterior al inicio.");
        return new(string.IsNullOrWhiteSpace(q) ? null : q.Trim().ToUpperInvariant(), supplierId, selected, start, end, number, size);
    }
}
