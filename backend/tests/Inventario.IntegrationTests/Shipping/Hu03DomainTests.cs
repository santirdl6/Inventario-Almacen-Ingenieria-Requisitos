using Inventario.Api.Domain.Shipping;
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

    // --- Documento de salida: Draft → Confirmed, líneas y consumos (data-model) ---

    private static readonly Guid OrderLineA = Guid.NewGuid();
    private static readonly Guid OtherLocation = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 30, 0, TimeSpan.Zero);

    private static ShipmentLineRequest Line(Guid? location = null, decimal quantity = 4m, Guid? orderLine = null) =>
        new(orderLine ?? OrderLineA, ArticleA, location ?? Location, Lot, quantity);

    private static Shipment Draft(params ShipmentLineRequest[] lines) =>
        Shipment.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1), lines.Length == 0 ? [Line()] : lines,
            Guid.NewGuid(), Now);

    [Fact]
    public void A_new_shipment_is_a_draft_without_operation_and_with_its_lines()
    {
        var actor = Guid.NewGuid();
        var shipment = Shipment.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1),
            [Line(), Line(OtherLocation, 2m)], actor, Now);

        Assert.Equal(ShipmentState.Draft, shipment.State);
        Assert.Null(shipment.OperationId);
        Assert.Equal(Now.AddHours(-1), shipment.OccurredAt);
        Assert.Equal(Now, shipment.RecordedAt);
        Assert.Equal(actor, shipment.ActorId);
        Assert.Equal(2, shipment.Lines.Count);
        Assert.All(shipment.Lines, line => Assert.Equal(shipment.Id, line.ShipmentId));
        // Una línea de pedido puede servirse desde varios orígenes (FR-002).
        Assert.Equal([4m, 2m], shipment.Lines.Select(line => line.Quantity));
    }

    [Fact]
    public void A_shipment_requires_identity_order_actor_and_the_time_of_the_fact()
    {
        Assert.Throws<ArgumentException>(() => Shipment.CreateDraft(Guid.Empty, Guid.NewGuid(), Now, [Line()], Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => Shipment.CreateDraft(Guid.NewGuid(), Guid.Empty, Now, [Line()], Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => Shipment.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Now, [Line()], Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => Shipment.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), default, [Line()], Guid.NewGuid(), Now));
    }

    [Fact]
    public void A_shipment_has_between_1_and_100_lines_and_one_per_order_line_and_bucket()
    {
        Assert.Throws<ArgumentException>(() => Shipment.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Now, [], Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => Draft(Enumerable.Range(0, Shipment.MaxLines + 1)
            .Select(_ => Line(Guid.NewGuid(), 1m)).ToArray()));
        Assert.Equal(Shipment.MaxLines, Draft(Enumerable.Range(0, Shipment.MaxLines)
            .Select(_ => Line(Guid.NewGuid(), 1m)).ToArray()).Lines.Count);
        // UX_ShipmentLines_Shipment_Bucket: misma línea de pedido, ubicación y lote dos veces.
        Assert.Throws<ArgumentException>(() => Draft(Line(), Line(quantity: 2m)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.0005)]
    public void Line_quantity_is_positive_with_three_decimals(decimal quantity) =>
        Assert.Throws<ArgumentException>(() => Draft(Line(quantity: quantity)));

    [Fact]
    public void Lines_require_order_line_article_location_and_lot() =>
        Assert.Throws<ArgumentException>(() => Draft(new ShipmentLineRequest(OrderLineA, ArticleA, Location, Guid.Empty, 1m)));

    [Fact]
    public void A_draft_can_replace_its_date_and_lines()
    {
        var shipment = Draft();
        var editor = Guid.NewGuid();
        shipment.ReplaceDraft(Now.AddHours(-2), [Line(OtherLocation, 3m)], editor, Now.AddMinutes(5));

        Assert.Equal(ShipmentState.Draft, shipment.State);
        Assert.Equal(Now.AddHours(-2), shipment.OccurredAt);
        Assert.Equal(Now.AddMinutes(5), shipment.RecordedAt);
        Assert.Equal(editor, shipment.ActorId);
        Assert.Equal(OtherLocation, shipment.Lines.Single().LocationId);
        Assert.Equal(3m, shipment.Lines.Single().Quantity);
    }

    [Fact]
    public void Only_draft_to_confirmed_and_a_confirmed_shipment_is_immutable()
    {
        var shipment = Draft();
        var operation = Guid.NewGuid();
        var confirmer = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => shipment.Confirm(Guid.Empty, confirmer, Now));

        shipment.Confirm(operation, confirmer, Now.AddMinutes(10));
        Assert.Equal(ShipmentState.Confirmed, shipment.State);
        Assert.Equal(operation, shipment.OperationId);
        Assert.Equal(confirmer, shipment.ActorId);
        Assert.Equal(Now.AddMinutes(10), shipment.RecordedAt);

        Assert.Throws<InvalidOperationException>(() => shipment.Confirm(Guid.NewGuid(), confirmer, Now));
        Assert.Throws<InvalidOperationException>(() => shipment.ReplaceDraft(Now, [Line()], confirmer, Now));
        Assert.Equal(operation, shipment.OperationId);
    }

    [Fact]
    public void A_consumption_belongs_to_the_operation_that_creates_it()
    {
        var line = Guid.NewGuid();
        var allocation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var consumption = ShipmentReservationConsumption.Create(line, allocation, 4m, operation);

        Assert.Equal(line, consumption.ShipmentLineId);
        Assert.Equal(allocation, consumption.ReservationAllocationId);
        Assert.Equal(4m, consumption.Quantity);
        Assert.Equal(operation, consumption.OperationId);
        Assert.Throws<ArgumentException>(() => ShipmentReservationConsumption.Create(line, allocation, 0m, operation));
        Assert.Throws<ArgumentException>(() => ShipmentReservationConsumption.Create(line, allocation, 1m, Guid.Empty));
        Assert.Throws<ArgumentException>(() => ShipmentReservationConsumption.Create(Guid.Empty, allocation, 1m, operation));
    }
}
