namespace Inventario.Api.Infrastructure.Identity;

public static class PermissionCatalog
{
    public const string InventoryRead = "Inventory.Read";
    public const string MasterDataManage = "MasterData.Manage";
    public const string LocationsManage = "Locations.Manage";
    public const string PurchasingManage = "Purchasing.Manage";
    public const string PurchasingSend = "Purchasing.Send";
    public const string ReceivingRecord = "Receiving.Record";
    public const string ShippingRecord = "Shipping.Record";
    public const string CountsRecord = "Counts.Record";
    public const string ReservationsManage = "Reservations.Manage";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string StockPolicyManage = "StockPolicy.Manage";
    public const string AlertsRead = "Alerts.Read";
    public const string UsersManage = "Users.Manage";

    public static readonly IReadOnlyList<string> Roles =
    ["Operario", "ResponsableAlmacen", "Comprador", "GestorMaestros", "Administrador"];

    public static readonly IReadOnlyList<string> All =
    [
        InventoryRead, MasterDataManage, LocationsManage, PurchasingManage, PurchasingSend,
        ReceivingRecord, ShippingRecord, CountsRecord, ReservationsManage, InventoryAdjust,
        StockPolicyManage, AlertsRead, UsersManage
    ];

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        "Operario" => [InventoryRead, LocationsManage, ReceivingRecord, ShippingRecord, CountsRecord],
        "ResponsableAlmacen" =>
        [InventoryRead, ReservationsManage, InventoryAdjust, CountsRecord, StockPolicyManage, AlertsRead],
        "Comprador" => [PurchasingManage, PurchasingSend, InventoryRead, AlertsRead],
        "GestorMaestros" => [MasterDataManage, InventoryRead],
        "Administrador" => [UsersManage],
        _ => []
    };
}
