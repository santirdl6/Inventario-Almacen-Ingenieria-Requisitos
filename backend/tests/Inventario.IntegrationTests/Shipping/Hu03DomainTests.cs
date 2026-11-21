using Inventario.Api.Domain.Stock;
using Xunit;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T003 (H-01): reglas locales que la confirmación de la salida necesita en el dominio de
// HU-05 (research D-04) y en el documento de salida, sin base de datos. Saldos, pendiente bajo
// bloqueo y reservas ajenas se deciden en el comando (T009).
public sealed class Hu03DomainTests
{
    private static readonly Guid ArticleA = Guid.NewGuid();
    private static readonly Guid ArticleB = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly Guid Lot = Guid.NewGuid();

    // --- HU-05: consumir reservas, servir líneas y derivar el estado del pedido ---

    [Fact]
    public void Consume_lowers_the_remaining_and_derives_consumed_when_nothing_is_left()
    {
        var reservation = Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), [new(ArticleA, Location, Lot, 4m)]);
        var allocation = reservation.Allocations.Single();

        allocation.Consume(1.5m);
        reservation.DeriveState();
        Assert.Equal(1.5m, allocation.ConsumedQuantity);
        Assert.Equal(2.5m, allocation.RemainingQuantity);
        Assert.Equal(ReservationState.Active, reservation.State);

        allocation.Consume(2.5m);
        reservation.DeriveState();
        Assert.Equal(4m, allocation.ConsumedQuantity);
        Assert.Equal(0m, allocation.RemainingQuantity);
        Assert.Equal(ReservationState.Consumed, reservation.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.0005)]
    public void Consume_requires_a_positive_quantity_with_three_decimals(decimal quantity)
    {
        var allocation = Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), [new(ArticleA, Location, Lot, 4m)]).Allocations.Single();
        Assert.Throws<ArgumentException>(() => allocation.Consume(quantity));
        Assert.Equal(0m, allocation.ConsumedQuantity);
    }

    [Fact]
    public void Consume_never_exceeds_the_remaining_after_releases()
    {
        var allocation = Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), [new(ArticleA, Location, Lot, 4m)]).Allocations.Single();
        allocation.Release(3m);
        Assert.Throws<InvalidOperationException>(() => allocation.Consume(1.001m));
        allocation.Consume(1m);
        Assert.Equal(0m, allocation.RemainingQuantity);
    }

    [Fact]
    public void Consumed_and_released_together_derive_released()
    {
        var reservation = Reservation.Create(Guid.NewGuid(), Guid.NewGuid(), [new(ArticleA, Location, Lot, 4m)]);
        var allocation = reservation.Allocations.Single();
        allocation.Consume(3m);
        allocation.Release(1m);
        reservation.DeriveState();
        Assert.Equal(ReservationState.Released, reservation.State);
    }

    [Fact]
    public void Ship_increases_shipped_without_exceeding_the_order()
    {
        var order = CustomerOrder.Create(Guid.NewGuid(), "PED-1", "Cliente", [new(ArticleA, 10m)]);
        var line = order.Lines.Single();

        line.Ship(6m);
        Assert.Equal(6m, line.ShippedQuantity);
        Assert.Equal(4m, line.PendingQuantity);
        Assert.Throws<InvalidOperationException>(() => line.Ship(4.001m));
        Assert.Throws<ArgumentException>(() => line.Ship(0m));
        Assert.Equal(6m, line.ShippedQuantity);
    }

    [Fact]
    public void Derive_fulfilment_keeps_the_order_open_until_every_line_is_shipped()
    {
        var order = CustomerOrder.Create(Guid.NewGuid(), "PED-1", "Cliente", [new(ArticleA, 10m), new(ArticleB, 2m)]);
        var a = order.Lines.Single(line => line.ArticleId == ArticleA);
        var b = order.Lines.Single(line => line.ArticleId == ArticleB);

        a.Ship(10m);
        order.DeriveFulfilment();
        Assert.Equal(CustomerOrderState.Open, order.State);

        b.Ship(2m);
        order.DeriveFulfilment();
        Assert.Equal(CustomerOrderState.Fulfilled, order.State);
        // Idempotente: un pedido ya servido sigue servido.
        order.DeriveFulfilment();
        Assert.Equal(CustomerOrderState.Fulfilled, order.State);
    }
}
