using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Shipping;

/// <summary>Escenario de una salida: artículo sin lote con existencias en un bucket y un pedido abierto.</summary>
internal sealed record ShippingSeed(Guid Article, string Sku, Guid Location, Guid Lot, Guid Order, Guid OrderLine);

/// <summary>
/// Soporte HTTP de las pruebas de HU-03 (T008). Sesiones reales por rol, pedidos y reservas con los
/// comandos reales de HU-05 (StockApiSupport) y existencias solo con StockCore (Receive). Sin dobles
/// de HU-05 ni escrituras directas de saldos.
/// </summary>
internal static class ShippingApiSupport
{
    public const string Shipments = "/api/v1/shipments";

    /// <summary>Artículo entero sin lote, <paramref name="physical"/> unidades en N01 y un pedido de una línea.</summary>
    public static async Task<ShippingSeed> SeedAsync(StockApiSupport support, MasterDataApi operario, decimal physical, string ordered)
    {
        var (article, sku) = await support.ArticleAsync();
        var location = await support.LocationAsync("N01-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(article, location, physical);
        var (order, line, _) = await StockApiSupport.CreateOrderAsync(operario, article, ordered);
        return new ShippingSeed(article, sku, location, await UntrackedLotAsync(support.Stock.Fixture, article), order, line);
    }

    /// <summary>Lote SIN-LOTE del artículo: el que creó la recepción o uno nuevo si aún no hay existencias.</summary>
    public static async Task<Guid> UntrackedLotAsync(IdentityFixture fixture, Guid article)
    {
        await using var database = fixture.ApplicationDatabase();
        var existing = await database.Set<Lot>().Where(lot => lot.ArticleId == article && lot.IsUntracked)
            .Select(lot => (Guid?)lot.Id).SingleOrDefaultAsync();
        if (existing is { } id) return id;
        var created = Lot.Untracked(article);
        database.Add(created);
        await database.SaveChangesAsync();
        return created.Id;
    }

    /// <summary>Pedido de varias líneas (un artículo por línea); devuelve el id y las líneas por artículo.</summary>
    public static async Task<(Guid Order, Dictionary<Guid, Guid> Lines)> CreateOrderAsync(MasterDataApi operario,
        params (Guid Article, string Quantity)[] lines)
    {
        using var created = await operario.WriteAsync(HttpMethod.Post, StockApiSupport.Orders, new
        {
            externalReference = "PED-" + StockApiSupport.Unique(8),
            customerReference = "Cliente Norte",
            lines = lines.Select(line => new { articleId = line.Article, quantity = line.Quantity })
        });
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == created.StatusCode, text);
        var resource = JsonDocument.Parse(text).RootElement.GetProperty("resource");
        return (resource.GetProperty("id").GetGuid(), resource.GetProperty("lines").EnumerateArray()
            .ToDictionary(line => line.GetProperty("articleId").GetGuid(), line => line.GetProperty("id").GetGuid()));
    }

    public static object Body(Guid? order, DateTimeOffset occurredAt, params (Guid Line, Guid Location, Guid Lot, string Quantity)[] lines) =>
        order is null
            ? new { occurredAt, lines = Lines(lines) }
            : new { customerOrderId = order, occurredAt, lines = Lines(lines) };

    public static object Body(ShippingSeed seed, string quantity) =>
        Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1), (seed.OrderLine, seed.Location, seed.Lot, quantity));

    private static object[] Lines((Guid Line, Guid Location, Guid Lot, string Quantity)[] lines) =>
        lines.Select(value => (object)new
        {
            customerOrderLineId = value.Line, locationId = value.Location, lotId = value.Lot, quantity = value.Quantity
        }).ToArray();

    /// <summary>Crea el borrador y devuelve su id y su ETag.</summary>
    public static async Task<(Guid Id, string ETag)> CreateDraftAsync(MasterDataApi session, object body, Guid? key = null)
    {
        using var created = await session.WriteAsync(HttpMethod.Post, Shipments, body, key);
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == created.StatusCode, text);
        return (JsonDocument.Parse(text).RootElement.GetProperty("shipment").GetProperty("id").GetGuid(),
            created.Headers.ETag!.ToString());
    }

    /// <summary>
    /// POST /shipments/{id}/confirm con el If-Match de la salida y la versión del pedido revisada
    /// (D-05). Sin <paramref name="orderVersion"/> usa el ETag actual de GET /customer-orders/{id}.
    /// </summary>
    public static async Task<HttpResponseMessage> ConfirmAsync(MasterDataApi session, Guid shipment, Guid order,
        string? shipmentEtag = null, string? orderVersion = null, Guid? key = null)
    {
        shipmentEtag ??= (await DetailAsync(session.Client, shipment)).ETag;
        orderVersion ??= (await StockApiSupport.OrderAsync(session.Client, order)).ETag;
        return await session.WriteAsync(HttpMethod.Post, $"{Shipments}/{shipment:D}/confirm", new { orderVersion }, key, shipmentEtag);
    }

    /// <summary>Reserva por HTTP (HU-05) con el ETag actual del pedido; devuelve la reserva creada.</summary>
    public static async Task<Guid> ReserveAsync(MasterDataApi responsable, Guid order, Guid line, Guid location, Guid lot, string quantity)
    {
        var (_, etag) = await StockApiSupport.OrderAsync(responsable.Client, order);
        using var response = await StockApiSupport.PostReservationAsync(responsable, line, etag, null, (location, lot, quantity));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == response.StatusCode, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("reservationId").GetGuid();
    }

    /// <summary>
    /// H-14: invariantes 1–7 de data-model para una salida confirmada. Saldos y reservas por bucket,
    /// Σ consumos = ConsumedQuantity por asignación, consumos = ShipReserved y resto = ShipAvailable
    /// por línea, servido = Σ salidas confirmadas, remanente ≤ pendiente, diario = saldo, un único
    /// movimiento y todo consumo con la operación de su cabecera.
    /// </summary>
    public static async Task AssertShipmentInvariantsAsync(StockApiSupport support, Guid shipmentId)
    {
        await using var database = support.Stock.Fixture.ApplicationDatabase();
        var shipment = await database.Set<Shipment>().AsNoTracking().Include(value => value.Lines).SingleAsync(value => value.Id == shipmentId);
        Assert.Equal(ShipmentState.Confirmed, shipment.State);
        var lineIds = shipment.Lines.Select(line => line.Id).ToList();
        var consumptions = await database.Set<ShipmentReservationConsumption>().AsNoTracking()
            .Where(value => lineIds.Contains(value.ShipmentLineId)).ToListAsync();
        // 7. Ningún consumo de otra operación.
        Assert.All(consumptions, value => Assert.Equal(shipment.OperationId, value.OperationId));
        // 6. Un único movimiento Shipment.
        var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
            .SingleAsync(value => value.SourceDocumentType == "shipment" && value.SourceDocumentId == shipmentId);
        Assert.Equal(MovementKind.Shipment, movement.Kind);
        Assert.Equal(shipment.OperationId, movement.OperationId);
        // 3. Por línea: Σ consumos = ShipReserved y Quantity − Σ consumos = ShipAvailable de su bucket.
        foreach (var line in shipment.Lines)
        {
            var own = consumptions.Where(value => value.ShipmentLineId == line.Id).Sum(value => value.Quantity);
            var entries = movement.Entries.Where(entry => entry.LocationId == line.LocationId && entry.LotId == line.LotId).ToList();
            Assert.Equal(own, entries.Where(entry => entry.Effect == StockEffect.ShipReserved).Sum(entry => entry.Quantity));
            Assert.Equal(line.Quantity - own, entries.Where(entry => entry.Effect == StockEffect.ShipAvailable).Sum(entry => entry.Quantity));
        }
        // 2. Por asignación: Σ consumos de todas las salidas = ConsumedQuantity.
        var allocationIds = consumptions.Select(value => value.ReservationAllocationId).Distinct().ToList();
        foreach (var allocation in await database.Set<ReservationAllocation>().AsNoTracking()
                     .Where(value => allocationIds.Contains(value.Id)).ToListAsync())
            Assert.Equal(allocation.ConsumedQuantity, await database.Set<ShipmentReservationConsumption>()
                .Where(value => value.ReservationAllocationId == allocation.Id).SumAsync(value => value.Quantity));
        // 4. Por línea de pedido: servido = Σ líneas confirmadas; remanente activo ≤ pendiente.
        foreach (var orderLineId in shipment.Lines.Select(line => line.CustomerOrderLineId).Distinct())
        {
            var orderLine = await database.Set<CustomerOrderLine>().AsNoTracking().SingleAsync(value => value.Id == orderLineId);
            var shipped = await (from line in database.Set<ShipmentLine>()
                                 join header in database.Set<Shipment>() on line.ShipmentId equals header.Id
                                 where line.CustomerOrderLineId == orderLineId && header.State == ShipmentState.Confirmed
                                 select line.Quantity).SumAsync();
            Assert.Equal(orderLine.ShippedQuantity, shipped);
            var remaining = await database.Set<Reservation>().Where(value => value.CustomerOrderLineId == orderLineId
                    && value.State == ReservationState.Active)
                .SelectMany(value => value.Allocations).SumAsync(value => value.RemainingQuantity);
            Assert.True(remaining <= orderLine.PendingQuantity, $"Remanente {remaining} > pendiente {orderLine.PendingQuantity}");
        }
        // 1 y 5. Σ remanente = reservado y diario = saldo, por bucket.
        foreach (var line in shipment.Lines)
        {
            await support.AssertCommitmentsMatchBalancesAsync(line.ArticleId, line.LocationId);
            await support.Stock.AssertJournalMatchesBalanceAsync(line.ArticleId, line.LocationId);
        }
    }

    /// <summary>GET /shipments/{id}: cuerpo y ETag.</summary>
    public static async Task<(JsonElement Body, string ETag)> DetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{Shipments}/{id:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).Clone(), response.Headers.ETag!.ToString());
    }

    /// <summary>Lo que ve el contributor de HU-03 (solo líneas de salida) para el artículo.</summary>
    public static async Task<ArticleUsage> ShipmentUsageAsync(ApiFactory factory, Guid article)
    {
        using var scope = factory.Services.CreateScope();
        var contributor = scope.ServiceProvider.GetServices<IArticleUsageContributor>()
            .Single(value => value.ReferencingEntityTypes.Contains(typeof(ShipmentLine)));
        return await contributor.ReadAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), article, CancellationToken.None);
    }

    /// <summary>Capacidades del artículo según HU-08 (definición y borrado).</summary>
    public static async Task<JsonElement> ArticleCapabilitiesAsync(MasterDataApi gestor, Guid article) =>
        (await gestor.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{article:D}/context")).GetProperty("capabilities").Clone();
}
