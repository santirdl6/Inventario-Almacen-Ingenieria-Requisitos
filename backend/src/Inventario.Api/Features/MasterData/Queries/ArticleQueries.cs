using System.Data;
using System.Globalization;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Queries;

public sealed record ArticleSummary(Guid Id, string Sku, string Name, FamilyDto Family,
    string BaseUnit, bool AllowsFractional, bool TracksLot, bool IsActive);
public sealed record ArticleCapabilities(bool CanEditDefinition, string? DefinitionLockReason, bool CanDelete, string? DeleteBlockReason);
public sealed record ArticleContextDto(string ArticleVersion, FamilyDto Family, string MinimumAvailable, ArticleCapabilities Capabilities);

public sealed class ArticleQueries(InventoryDbContext db, IArticleUsageReader usage, ArticleDefinitionReadiness readiness)
{
    public Task<PageResult<ArticleSummary>> ListAsync(MasterDataQueryOptions options, Guid? familyId, CancellationToken cancellation, string? skuPrefix = null) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var prefix = MasterDataText.Optional(skuPrefix, 64, "skuPrefix")?.ToUpperInvariant();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var query = db.Articles.AsNoTracking();
            if (prefix is not null) query = query.Where(value => value.Sku.StartsWith(prefix));
            if (options.IsActive is { } active) query = query.Where(value => value.IsActive == active);
            if (familyId is { } selected) query = query.Where(value => value.FamilyId == selected);
            if (options.Query is { } q) query = query.Where(value => value.Sku.Contains(q)
                || EF.Functions.Collate(value.Name, "Latin1_General_100_CI_AS").Contains(q));
            // FamilyId is a required restrictive FK: count needs no join, and only page rows need family data.
            var count = await query.CountAsync(cancellation);
            var pageIds = query.OrderBy(value => value.Sku).ThenBy(value => value.Id)
                .Skip((options.Page - 1) * options.PageSize).Take(options.PageSize)
                .Select(value => value.Id);
            var items = await (from article in db.Articles.AsNoTracking().Where(value => pageIds.Contains(value.Id))
                join family in db.Set<Family>() on article.FamilyId equals family.Id
                orderby article.Sku, article.Id
                select new ArticleSummary(article.Id, article.Sku, article.Name,
                    new FamilyDto(family.Id, family.Code, family.Name, family.IsActive),
                    article.BaseUnit, article.AllowsFractional, article.TracksLot, article.IsActive)).ToListAsync(cancellation);
            await tx.CommitAsync(cancellation);
            return new PageResult<ArticleSummary>(items, options.Page, options.PageSize, count);
        });

    public Task<Article?> DetailAsync(Guid id, CancellationToken cancellation) =>
        db.Articles.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation);

    public Task<ArticleContextDto> ContextAsync(Guid id, CancellationToken cancellation) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
        var article = await DetailAsync(id, cancellation) ?? throw new InventoryProblemException(404, "NotFound", "El artículo no existe.");
        readiness.EnsureReady();
        var references = await usage.ReadAsync(id, cancellation);
        var definitionReferences = await usage.ReadDefinitionAsync(id, cancellation);
        var family = await db.Set<Family>().AsNoTracking().SingleAsync(value => value.Id == article.FamilyId, cancellation);
        var policy = await db.Set<StockPolicy>().AsNoTracking().SingleOrDefaultAsync(value => value.ArticleId == id, cancellation)
            ?? throw new InventoryProblemException(503, "DependencyUnavailable", "Falta completar la política inicial del artículo.");
        var locked = article.IsDefinitionLocked || definitionReferences.HasReferences || definitionReferences.HasHistory;
        var result = new ArticleContextDto(EntityTags.Format(article.Version), FamilyDto.From(family),
            policy.MinimumAvailable.ToString("F3", CultureInfo.InvariantCulture),
            new ArticleCapabilities(!locked, locked ? "FirstReference" : null, !references.HasReferences && !references.HasHistory,
                references.HasHistory ? "History" : references.HasReferences ? "References" : null));
        await tx.CommitAsync(cancellation); return result;
    });
}
