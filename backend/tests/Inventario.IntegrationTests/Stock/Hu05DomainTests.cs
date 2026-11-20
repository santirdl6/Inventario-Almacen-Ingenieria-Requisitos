using Inventario.Api.Domain.Stock;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T003: reglas locales de CustomerOrder y Reservation, sin base de datos. Las reglas que
// dependen de saldos, del pendiente o de otras reservas se evalúan en los comandos (T008, T011).
public sealed class Hu05DomainTests
{
    private static readonly Guid ArticleA = Guid.NewGuid();
    private static readonly Guid ArticleB = Guid.NewGuid();

    [Fact]
    public void A_new_order_is_open_trimmed_with_nothing_shipped()
    {
        var order = CustomerOrder.Create(Guid.NewGuid(), "  PED-1001 ", " Cliente Norte ",
            [new(ArticleA, 7m), new(ArticleB, 3.5m)]);

        Assert.Equal("PED-1001", order.ExternalReference);
        Assert.Equal("Cliente Norte", order.CustomerReference);
        Assert.Equal(CustomerOrderState.Open, order.State);
        Assert.Equal(2, order.Lines.Count);
        Assert.All(order.Lines, line =>
        {
            Assert.Equal(order.Id, line.CustomerOrderId);
            Assert.Equal(0m, line.ShippedQuantity);
            Assert.Equal(line.OrderedQuantity, line.PendingQuantity);
        });
    }

    [Theory]
    [InlineData("", "Cliente")]
    [InlineData("   ", "Cliente")]
    [InlineData("PED-1", "")]
    public void An_order_requires_both_references(string external, string customer) =>
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(), external, customer, [new(ArticleA, 1m)]));

    [Fact]
    public void References_respect_their_maximum_length()
    {
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(),
            new string('P', CustomerOrder.ExternalReferenceMaxLength + 1), "Cliente", [new(ArticleA, 1m)]));
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(), "PED-1",
            new string('C', CustomerOrder.CustomerReferenceMaxLength + 1), [new(ArticleA, 1m)]));
        Assert.Equal(CustomerOrder.ExternalReferenceMaxLength, CustomerOrder.Create(Guid.NewGuid(),
            new string('P', CustomerOrder.ExternalReferenceMaxLength), "Cliente", [new(ArticleA, 1m)]).ExternalReference.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.0005)]
    public void Ordered_quantity_is_positive_with_three_decimals(decimal quantity) =>
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(), "PED-1", "Cliente", [new(ArticleA, quantity)]));

    [Fact]
    public void An_order_needs_lines_and_each_article_only_once()
    {
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(), "PED-1", "Cliente", []));
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(), "PED-1", "Cliente",
            [new(ArticleA, 1m), new(ArticleA, 2m)]));
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.NewGuid(), "PED-1", "Cliente",
            Enumerable.Range(0, CustomerOrder.MaxLines + 1).Select(_ => new CustomerOrderLineRequest(Guid.NewGuid(), 1m)).ToList()));
        Assert.Throws<ArgumentException>(() => CustomerOrder.Create(Guid.Empty, "PED-1", "Cliente", [new(ArticleA, 1m)]));
    }

    [Fact]
    public void A_new_reservation_is_active_with_its_whole_allocation_remaining()
    {
        var reservation = NewReservation(7m);
        var allocation = Assert.Single(reservation.Allocations);

        Assert.Equal(ReservationState.Active, reservation.State);
        Assert.Equal(reservation.Id, allocation.ReservationId);
        Assert.Equal(7m, allocation.AllocatedQuantity);
        Assert.Equal(0m, allocation.ConsumedQuantity);
        Assert.Equal(0m, allocation.ReleasedQuantity);
        Assert.Equal(7m, allocation.RemainingQuantity);
    }

    [Fact]
    public void A_reservation_rejects_empty_non_positive_or_repeated_buckets()
    {
        var location = Guid.NewGuid();
        var lot = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), []));
        Assert.Throws<ArgumentException>(() => Reservation.Create(Guid.NewGuid(), Guid.NewGuid(),
            [new(ArticleA, location, lot, 0m)]));
        // P-02: una asignación por bucket y reserva.
        Assert.Throws<ArgumentException>(() => Reservation.Create(Guid.NewGuid(), Guid.NewGuid(),
            [new(ArticleA, location, lot, 2m), new(ArticleA, location, lot, 3m)]));
    }

    [Fact]
    public void Releasing_only_grows_released_and_never_exceeds_the_remaining()
    {
        var reservation = NewReservation(6m);
        var allocation = reservation.Allocations[0];

        allocation.Release(2m);
        allocation.Release(2m);
        Assert.Equal(4m, allocation.ReleasedQuantity);
        Assert.Equal(2m, allocation.RemainingQuantity);
        Assert.Throws<InvalidOperationException>(() => allocation.Release(3m));
        Assert.Throws<ArgumentException>(() => allocation.Release(0m));
        Assert.Throws<ArgumentException>(() => allocation.Release(-1m));
        Assert.Equal(4m, allocation.ReleasedQuantity);

        reservation.DeriveState();
        Assert.Equal(ReservationState.Active, reservation.State);
    }

    [Fact]
    public void State_is_released_when_nothing_remains_after_releases()
    {
        var reservation = NewReservation(5m);
        reservation.Allocations[0].Release(5m);
        reservation.DeriveState();
        Assert.Equal(ReservationState.Released, reservation.State);
        Assert.Throws<InvalidOperationException>(() => reservation.Allocations[0].Release(1m));
    }

    [Fact]
    public void The_reference_lock_is_a_stable_uuid_v5_of_the_normalized_reference()
    {
        var id = Inventario.Api.Features.Stock.StockDocuments.ReferenceLockId("PED-1001");
        Assert.Equal(id, Inventario.Api.Features.Stock.StockDocuments.ReferenceLockId("  ped-1001 "));
        Assert.NotEqual(id, Inventario.Api.Features.Stock.StockDocuments.ReferenceLockId("PED-1002"));
        var text = id.ToString("D");
        Assert.Equal('5', text[14]);                    // versión 5
        Assert.Contains(text[19], "89ab");              // variante RFC
    }

    [Fact]
    public void Document_types_follow_the_d04_lock_order_and_the_canonical_pattern()
    {
        string[] types = [Inventario.Api.Features.Stock.StockDocuments.CustomerOrderReference,
            Inventario.Api.Features.Stock.StockDocuments.CustomerOrder,
            Inventario.Api.Features.Stock.StockDocuments.ReservationRelease,
            Inventario.Api.Features.Stock.StockDocuments.Reservation];
        Assert.All(types, type => Assert.Matches("^[a-z][a-z0-9-]{0,31}$", type));
        var resources = types.Select(type => type + ":").ToArray();
        Assert.Equal(resources, resources.Order(StringComparer.Ordinal));
    }

    private static Reservation NewReservation(decimal quantity) =>
        Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), [new(ArticleA, Guid.NewGuid(), Guid.NewGuid(), quantity)]);
}
