using System.Globalization;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Core;

internal sealed class StockArticleMovementReader : IArticleMovementReader
{
    public async Task<PageResult<ArticleMovementDto>> ReadAsync(InventoryDbContext database, Guid articleId,
        ArticleMovementOptions options, CancellationToken cancellation)
    {
        var migrations = await database.Database.GetAppliedMigrationsAsync(cancellation);
        if (!migrations.Contains(StockSchema.Migration))
            throw new InventoryProblemException(503, "DependencyUnavailable", "El diario de stock todavía no está disponible.");

        var entries = from entry in database.Set<StockEntry>().AsNoTracking()
            join movement in database.Set<StockMovement>() on entry.MovementId equals movement.Id
            where entry.ArticleId == articleId
            select new { Entry = entry, Movement = movement };

        if (options.From is { } from) entries = entries.Where(row => row.Movement.RecordedAt >= from);
        if (options.To is { } to) entries = entries.Where(row => row.Movement.RecordedAt < to);
        if (options.ActorId is { } actorId) entries = entries.Where(row => row.Movement.ActorId == actorId);
        if (options.LocationId is { } locationId) entries = entries.Where(row => row.Entry.LocationId == locationId);
        if (options.LotId is { } lotId) entries = entries.Where(row => row.Entry.LotId == lotId);

        var total = await entries.CountAsync(cancellation);
        var page = await (from row in entries
            join location in database.Set<Location>() on row.Entry.LocationId equals location.Id
            join lot in database.Set<Lot>() on row.Entry.LotId equals lot.Id
            join actor in database.Set<ApplicationUser>() on row.Movement.ActorId equals actor.Id
            orderby row.Movement.RecordedAt descending, row.Entry.Id
            select new { row.Entry, row.Movement, Location = location, Lot = lot, ActorName = actor.UserName })
            .Skip((options.Page - 1) * options.PageSize).Take(options.PageSize).ToListAsync(cancellation);

        var items = page.Select(row =>
        {
            var entry = row.Entry;
            var movement = row.Movement;
            var location = new MovementLocation(row.Location.Id, row.Location.Code,
                $"Pasillo {row.Location.Aisle}, estantería {row.Location.Rack}, nivel {row.Location.Level}");
            var isOrigin = entry.Effect is StockEffect.ShipAvailable or StockEffect.ShipReserved or StockEffect.AdjustDown;
            var isDestination = entry.Effect is StockEffect.Receive or StockEffect.AdjustUp;
            // Reserve/Release retain the bucket location even though neither changes physical stock.
            var reservation = entry.Effect is StockEffect.Reserve or StockEffect.Release;
            return new ArticleMovementDto(entry.Id, movement.Id, movement.Kind.ToString(),
                entry.Quantity.ToString("F3", CultureInfo.InvariantCulture), entry.BaseUnitSnapshot,
                movement.OccurredAt, movement.RecordedAt,
                new MovementActor(movement.ActorId, row.ActorName ?? movement.ActorId.ToString("D")),
                movement.Reason, new MovementDocument(movement.SourceDocumentType, movement.SourceDocumentId),
                movement.SupportReference, movement.SupportNotApplicableReason, movement.CounterpartyReference, null,
                isOrigin || reservation ? location : null, isDestination ? location : null,
                row.Lot.IsUntracked ? null : new MovementLot(row.Lot.Id, row.Lot.Code));
        }).ToArray();
        return new(items, options.Page, options.PageSize, total);
    }
}
