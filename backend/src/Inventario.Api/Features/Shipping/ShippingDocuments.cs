namespace Inventario.Api.Features.Shipping;

/// <summary>
/// Tipo canónico de documento de HU-03 para InventoryLocks y StockCore (research D-03). En orden
/// ordinal va después de todos los de HU-05: customer-order-reference &lt; customer-order &lt;
/// reservation-release &lt; reservation &lt; shipment.
/// </summary>
public static class ShippingDocuments
{
    public const string Shipment = "shipment";
}
