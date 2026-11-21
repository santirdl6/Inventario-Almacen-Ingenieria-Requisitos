using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inventario.Api.Features.Replenishment;

public sealed class StockAlertEvaluator
{
    private const decimal Limit = 1_000_000_000_000_000m;
    private static readonly Meter Metrics = new("Inventario.Replenishment");
    private static readonly Histogram<double> Duration = Metrics.CreateHistogram<double>("stock.alert.evaluate.duration", "ms");

    public async Task EvaluateAsync(OperationContext context, IEnumerable<Guid> articleIds, CancellationToken cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        await EvaluateCoreAsync(context, articleIds, cancellation);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static async Task EvaluateCoreAsync(OperationContext context, IEnumerable<Guid> articleIds, CancellationToken cancellation)
    {
        var db = context.Database;
        var ids = articleIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        var articles = db.Articles.Local.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        var missing = ids.Where(id => !articles.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            foreach (var article in await db.Articles.Where(x => missing.Contains(x.Id)).ToListAsync(cancellation))
                articles.Add(article.Id, article);
        // Tracking queries preserve the current policy and episode in this attempt.
        var policies = await db.Set<StockPolicy>().Where(x => ids.Contains(x.ArticleId)).ToDictionaryAsync(x => x.ArticleId, cancellation);
        await db.Set<StockAlert>().Where(x => ids.Contains(x.ArticleId) && x.State == "Active").LoadAsync(cancellation);
        var alerts = db.Set<StockAlert>().Local.Where(x => ids.Contains(x.ArticleId) && x.State == "Active").ToDictionary(x => x.ArticleId);
        var persisted = await db.Set<StockBalance>().Where(x => ids.Contains(x.ArticleId)).GroupBy(x => x.ArticleId)
            .Select(group => new { Id = group.Key, Physical = group.Sum(x => x.Physical), Reserved = group.Sum(x => x.Reserved) })
            .ToDictionaryAsync(x => x.Id, cancellation);
        var pending = db.ChangeTracker.Entries<StockBalance>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToLookup(x => x.Entity.ArticleId);
        foreach (var id in ids)
        {
            if (!articles.TryGetValue(id, out var article))
                throw new InventoryProblemException(404, "NotFound", "El artículo no existe.");
            if (!policies.TryGetValue(id, out var policy))
                throw new InventoryProblemException(503, "DependencyUnavailable", "Falta la política de existencias.");
            var total = persisted.GetValueOrDefault(id);
            var physical = total?.Physical ?? 0m;
            var reserved = total?.Reserved ?? 0m;
            foreach (var entry in pending[id])
            {
                if (entry.State != EntityState.Added)
                {
                    physical -= entry.Property(x => x.Physical).OriginalValue;
                    reserved -= entry.Property(x => x.Reserved).OriginalValue;
                }
                if (entry.State != EntityState.Deleted)
                {
                    physical += entry.Entity.Physical;
                    reserved += entry.Entity.Reserved;
                }
            }
            if (physical >= Limit || reserved >= Limit || reserved < 0 || physical < reserved)
                throw new InventoryProblemException(422, "ValidationError", "El saldo agregado no es representable.");
            var available = physical - reserved;
            var alert = alerts.GetValueOrDefault(id);
            var now = DateTimeOffset.UtcNow;
            if (article.IsActive && available < policy.MinimumAvailable)
            {
                if (alert is null) db.Add(new StockAlert(id, physical, reserved, policy.MinimumAvailable, now));
                else alert.Update(physical, reserved, policy.MinimumAvailable, now);
            }
            else if (alert is not null) alert.Resolve(now);
        }
    }
}

public sealed class ArticleAlertStateEffects(StockAlertEvaluator evaluator) : IArticleStateEffects
{
    public Task ApplyAsync(OperationContext context, Article article, CancellationToken cancellation) =>
        evaluator.EvaluateAsync(context, [article.Id], cancellation);
}
