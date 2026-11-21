using System.Data;
using System.Globalization;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Replenishment;

public static class StockAlertEndpoints
{
    public static void MapStockAlertEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stock-alerts", async (string? state, string? q, Guid? articleId, DateTimeOffset? from, DateTimeOffset? to,
            int? page, int? pageSize, InventoryDbContext db, CancellationToken token) =>
        {
            state ??= "Active";
            if (state is not ("Active" or "Resolved" or "All") || page is < 1 || pageSize is < 1 or > 100 || from >= to)
                throw InventoryProblemException.Validation("filters", "Revisa el estado, las fechas y la paginación.");
            var currentPage = page ?? 1;
            var size = pageSize ?? 25;
            if ((long)(currentPage - 1) * size > int.MaxValue)
                throw InventoryProblemException.Validation("page", "La página solicitada es demasiado grande.");
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
                var query = from alert in db.Set<StockAlert>().AsNoTracking()
                            join article in db.Articles.AsNoTracking() on alert.ArticleId equals article.Id
                            select new { alert, article };
                if (state != "All") query = query.Where(x => x.alert.State == state);
                if (articleId is not null) query = query.Where(x => x.alert.ArticleId == articleId);
                if (from is not null) query = query.Where(x => x.alert.OpenedAt >= from);
                if (to is not null) query = query.Where(x => x.alert.OpenedAt < to);
                if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim(); query = query.Where(x => x.article.Sku.Contains(term) || x.article.Name.Contains(term)); }
                var count = await query.CountAsync(token);
                var rows = await query.OrderByDescending(x => x.alert.OpenedAt).ThenBy(x => x.alert.Id)
                    .Skip((currentPage - 1) * size).Take(size).ToListAsync(token);
                var items = rows.Select(x => Dto(x.alert, x.article.Sku, x.article.Name, x.article.BaseUnit)).ToArray();
                await tx.CommitAsync(token);
                return Results.Ok(new { items, page = currentPage, pageSize = size, totalCount = count });
            });
        }).RequireAuthorization("Alerts.Read");
        api.MapGet("/stock-alerts/{id:guid}", async (Guid id, InventoryDbContext db, CancellationToken token) =>
        {
            var row = await (from alert in db.Set<StockAlert>().AsNoTracking()
                             join article in db.Articles.AsNoTracking() on alert.ArticleId equals article.Id
                             where alert.Id == id select new { alert, article }).SingleOrDefaultAsync(token);
            return row is null ? ApiProblems.Status(404) : Results.Ok(Dto(row.alert, row.article.Sku, row.article.Name, row.article.BaseUnit));
        }).RequireAuthorization("Alerts.Read");
    }

    private static object Dto(StockAlert alert, string sku, string name, string unit) => new
    {
        id = alert.Id, articleId = alert.ArticleId, articleSku = sku, articleName = name, baseUnit = unit, state = alert.State,
        physicalSnapshot = Format(alert.PhysicalSnapshot), reservedSnapshot = Format(alert.ReservedSnapshot),
        availableSnapshot = Format(alert.AvailableSnapshot), minimumSnapshot = Format(alert.MinimumSnapshot),
        suggestedQuantity = Format(alert.SuggestedQuantity), openedAt = alert.OpenedAt, updatedAt = alert.UpdatedAt, resolvedAt = alert.ResolvedAt
    };

    private static string Format(decimal value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
