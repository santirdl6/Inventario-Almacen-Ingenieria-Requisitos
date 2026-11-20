using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Reservations;

/// <summary>
/// Lecturas de reservas (HU-05 T011; research D-07, D-12), cada una en una transacción SNAPSHOT.
/// GET /reservations/{id} lleva solo datos propios y su ETag; los nombres y códigos actuales salen
/// de GET /customer-orders/{id}/context. GET /stock/reservations lista los compromisos por
/// asignación con remanente, y su total coincide con totals.reserved de GET /stock (S-07).
/// </summary>
public sealed class ReservationQueries(InventoryDbContext database)
{
    public Task<(ReservationView View, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var reservation = await database.Set<Reservation>().AsNoTracking().Include(value => value.Allocations)
                .SingleOrDefaultAsync(value => value.Id == id, cancellation);
            if (reservation is null) return ((ReservationView, byte[])?)null;
            var line = await database.Set<CustomerOrderLine>().AsNoTracking()
                .SingleAsync(value => value.Id == reservation.CustomerOrderLineId, cancellation);
            await transaction.CommitAsync(cancellation);
            return (ReservationView.From(reservation, line.CustomerOrderId, line.ArticleId), reservation.Version);
        });

    public async Task<IResult> CommitmentsAsync(string? articleId, string? locationId, string? lotId, int? page, int? pageSize,
        CancellationToken cancellation)
    {
        if (!Guid.TryParse(articleId, out var article) || article == Guid.Empty)
            throw InventoryProblemException.Validation("articleId", "Elige un artículo para ver sus reservas.");
        var location = OptionalId(locationId, "locationId", "La ubicación indicada no es válida.");
        var lot = OptionalId(lotId, "lotId", "El lote indicado no es válido.");
        var paging = Paging.From(page, pageSize);

        var result = await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            if (!await database.Set<Article>().AsNoTracking().AnyAsync(value => value.Id == article, cancellation))
                return (ReservationCommitments?)null;
            var asOf = DateTimeOffset.UtcNow;
            var filtered = database.Set<ReservationAllocation>().AsNoTracking()
                .Where(allocation => allocation.ArticleId == article && allocation.RemainingQuantity > 0m);
            if (location is { } locationFilter) filtered = filtered.Where(allocation => allocation.LocationId == locationFilter);
            if (lot is { } lotFilter) filtered = filtered.Where(allocation => allocation.LotId == lotFilter);

            var remaining = await filtered.SumAsync(allocation => (decimal?)allocation.RemainingQuantity, cancellation) ?? 0m;
            var rows = from allocation in filtered
                       join reservation in database.Set<Reservation>().AsNoTracking() on allocation.ReservationId equals reservation.Id
                       join line in database.Set<CustomerOrderLine>().AsNoTracking() on reservation.CustomerOrderLineId equals line.Id
                       join order in database.Set<CustomerOrder>().AsNoTracking() on line.CustomerOrderId equals order.Id
                       join place in database.Set<Location>().AsNoTracking() on allocation.LocationId equals place.Id
                       join bucket in database.Set<Lot>().AsNoTracking() on allocation.LotId equals bucket.Id
                       select new { allocation, reservation.State, LineId = line.Id, order.Id, order.ExternalReference,
                           OrderState = order.State, place.Code, LotId = bucket.Id, LotCode = bucket.Code, bucket.IsUntracked };
            var count = await rows.CountAsync(cancellation);
            // Orden del contrato: referencia externa, ubicación y lote; el Id de la asignación desempata.
            var items = await rows.OrderBy(row => row.ExternalReference).ThenBy(row => row.Code).ThenBy(row => row.LotCode)
                .ThenBy(row => row.allocation.Id).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellation);
            await transaction.CommitAsync(cancellation);

            return new ReservationCommitments(asOf, new CommitmentTotals(Quantity(remaining)), items.Select(row =>
                new CommitmentRow(new CommitmentOrder(row.Id, row.ExternalReference, row.OrderState.ToString()), row.LineId,
                    row.allocation.ReservationId, row.allocation.Id, row.State.ToString(),
                    new CommitmentLocation(row.allocation.LocationId, row.Code), new CommitmentLot(row.LotId, row.LotCode, row.IsUntracked),
                    Quantity(row.allocation.AllocatedQuantity), Quantity(row.allocation.ConsumedQuantity),
                    Quantity(row.allocation.ReleasedQuantity), Quantity(row.allocation.RemainingQuantity))).ToList(),
                paging.Page, paging.PageSize, count);
        });
        return result is null ? ApiProblems.Status(404) : Results.Ok(result);
    }

    private static string Quantity(decimal value) => CustomerOrderView.Quantity(value);

    private static Guid? OptionalId(string? value, string field, string message)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return Guid.TryParse(value, out var id) ? id : throw InventoryProblemException.Validation(field, message);
    }
}
