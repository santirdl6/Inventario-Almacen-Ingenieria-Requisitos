using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T003b/T004: esquema de pedidos de cliente y reservas sobre la cadena común, cobertura de
// referencias de Article (INT-HU05-01) y DENY DELETE. Sin endpoints de HU-05 todavía: el pedido
// de la prueba de borrado se inserta con la conexión de ejecución, como hace la suite de stock.
public sealed class Hu05SchemaTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string MigrationSuffix = "_HU05_CustomerOrdersReservations";

    [Fact]
    public void The_model_includes_hu05_is_fully_migrated_and_every_article_reference_is_covered()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        Assert.NotNull(database.Model.FindEntityType(typeof(CustomerOrder)));
        Assert.NotNull(database.Model.FindEntityType(typeof(CustomerOrderLine)));
        Assert.NotNull(database.Model.FindEntityType(typeof(Reservation)));
        Assert.NotNull(database.Model.FindEntityType(typeof(ReservationAllocation)));
        // Conjunto exacto de tipos con FK a Article; HU-03 (T004, D-09) añade ShipmentLine y HU-04 (T003, D-08), CountLine.
        Assert.Equal(new HashSet<Type> { typeof(Lot), typeof(StockBalance), typeof(StockEntry),
                typeof(CustomerOrderLine), typeof(ReservationAllocation), typeof(Inventario.Api.Domain.Shipping.ShipmentLine),
                typeof(CountLine) },
            ArticleUsageCoverageValidator.RequiredTypes(database.Model));
        // Sin contributor de HU-05, HU-08 respondería 503 DependencyUnavailable.
        scope.ServiceProvider.GetRequiredService<ArticleUsageCoverageValidator>().EnsureCovered();
        scope.ServiceProvider.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
        Assert.False(database.Database.HasPendingModelChanges());
        // HU-05 sigue en la cadena; desde HU-03 (T004) ya no es la última (Hu03SchemaTests lo comprueba).
        Assert.Contains(database.Database.GetMigrations(), name => name.EndsWith(MigrationSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tables_constraints_indexes_and_the_computed_remaining_column_exist()
    {
        await using var database = fixture.ApplicationDatabase();
        Assert.Contains(database.Database.GetAppliedMigrations(), name => name.EndsWith(MigrationSuffix, StringComparison.Ordinal));

        var constraints = await Names(database, "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN "
            + "(OBJECT_ID('dbo.CustomerOrders'), OBJECT_ID('dbo.CustomerOrderLines'), OBJECT_ID('dbo.Reservations'), OBJECT_ID('dbo.ReservationAllocations'))");
        Assert.Superset(new HashSet<string>
        {
            "CK_CustomerOrders_State", "CK_CustomerOrders_ExternalReference_NotBlank", "CK_CustomerOrders_CustomerReference_NotBlank",
            "CK_CustomerOrderLines_Ordered", "CK_CustomerOrderLines_Shipped",
            "CK_Reservations_State",
            "CK_ReservationAllocations_Allocated", "CK_ReservationAllocations_Consumed", "CK_ReservationAllocations_Released",
            "CK_ReservationAllocations_Remaining"
        }, constraints);

        var indexes = await Names(database, "SELECT name AS [Value] FROM sys.indexes WHERE is_unique = 1 AND object_id IN "
            + "(OBJECT_ID('dbo.CustomerOrders'), OBJECT_ID('dbo.CustomerOrderLines'), OBJECT_ID('dbo.ReservationAllocations'))");
        Assert.Superset(new HashSet<string>
        {
            "UX_CustomerOrders_ExternalReference", "UX_CustomerOrderLines_Order_Article", "UX_ReservationAllocations_Reservation_Bucket"
        }, indexes);
        Assert.Contains("IX_ReservationAllocations_Bucket", await Names(database,
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ReservationAllocations')"));

        var persisted = await Names(database, "SELECT name AS [Value] FROM sys.computed_columns "
            + "WHERE object_id = OBJECT_ID('dbo.ReservationAllocations') AND is_persisted = 1");
        Assert.Contains("RemainingQuantity", persisted);
        var collation = await Names(database, "SELECT collation_name AS [Value] FROM sys.columns "
            + "WHERE object_id = OBJECT_ID('dbo.CustomerOrders') AND name = 'ExternalReference'");
        Assert.Equal("Latin1_General_100_CI_AS", Assert.Single(collation));
    }

    [Fact]
    public async Task A_customer_order_is_kept_and_its_article_cannot_be_deleted()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles",
            MasterDataArticleTests.Body(family, "HU05-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();

        // Un pedido con una sola línea, sin movimientos ni saldo: referencia real sin historial.
        var order = CustomerOrder.Create(Guid.NewGuid(), "PED-" + Guid.NewGuid().ToString("N")[..12], "Cliente de prueba",
            [new(article, 3m)]);
        await using (var database = fixture.ApplicationDatabase())
        {
            database.Add(order);
            await database.SaveChangesAsync();
            Assert.NotEmpty(order.Version);
        }

        using (var scope = factory.Services.CreateScope())
            Assert.Equal(new ArticleUsage(true, false),
                await scope.ServiceProvider.GetRequiredService<IArticleUsageReader>().ReadAsync(article));

        var path = $"/api/v1/articles/{article:D}";
        var current = await api.Client.GetAsync(path);
        var capabilities = (await api.Client.GetFromJsonAsync<JsonElement>(path + "/context")).GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("canDelete").GetBoolean());
        Assert.Equal("References", capabilities.GetProperty("deleteBlockReason").GetString());
        Assert.False(capabilities.GetProperty("canEditDefinition").GetBoolean());

        var deleted = await api.WriteAsync(HttpMethod.Delete, path, new { }, version: current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("HistoryPreventsDeletion", (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using var check = fixture.ApplicationDatabase();
        Assert.True(await check.Articles.AnyAsync(value => value.Id == article));
        var line = await check.Set<CustomerOrderLine>().SingleAsync(value => value.CustomerOrderId == order.Id);
        Assert.Equal(3m, line.OrderedQuantity);
        Assert.Equal(0m, line.ShippedQuantity);
        Assert.Equal(CustomerOrderState.Open,
            (await check.Set<CustomerOrder>().SingleAsync(value => value.Id == order.Id)).State);

        // Pedidos, líneas, reservas y asignaciones no se borran (DENY DELETE, como StockBalances).
        foreach (var table in new[] { "CustomerOrderLines", "CustomerOrders", "ReservationAllocations", "Reservations" })
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() =>
                check.Database.ExecuteSqlRawAsync($"DELETE FROM dbo.{table} WHERE 1 = 0"));
            Assert.Equal(229, denied.Number);
        }
    }

    [Fact]
    public async Task Check_constraints_reject_invalid_quantities_and_states_written_directly()
    {
        var created = await fixture.NewArticleAsync("CK-" + Guid.NewGuid().ToString("N")[..12], "Artículo CHECK", "UD", false, false);
        await using var database = fixture.ApplicationDatabase();
        database.Add(created);
        await database.SaveChangesAsync();
        var article = created.Id;
        var order = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.CustomerOrders (Id, ExternalReference, CustomerReference, State) VALUES ({order}, {"CK-" + order.ToString("N")[..10]}, {"Cliente"}, {"Open"})");

        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.CustomerOrderLines (Id, CustomerOrderId, ArticleId, OrderedQuantity, ShippedQuantity) VALUES ({Guid.NewGuid()}, {order}, {article}, {0m}, {0m})"));
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.CustomerOrderLines (Id, CustomerOrderId, ArticleId, OrderedQuantity, ShippedQuantity) VALUES ({Guid.NewGuid()}, {order}, {article}, {2m}, {3m})"));
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.CustomerOrders (Id, ExternalReference, CustomerReference, State) VALUES ({Guid.NewGuid()}, {"CK2-" + order.ToString("N")[..10]}, {"Cliente"}, {"Borrador"})"));
        // Referencia repetida sin distinguir mayúsculas (índice único con intercalación CI_AS).
        var reference = "Ref-" + order.ToString("N")[..10];
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.CustomerOrders (Id, ExternalReference, CustomerReference, State) VALUES ({Guid.NewGuid()}, {reference}, {"Cliente"}, {"Open"})");
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.CustomerOrders (Id, ExternalReference, CustomerReference, State) VALUES ({Guid.NewGuid()}, {reference.ToUpperInvariant()}, {"Cliente"}, {"Open"})"));
    }

    private static async Task<HashSet<string>> Names(InventoryDbContext database, string sql) =>
        (await database.Database.SqlQueryRaw<string>(sql).ToListAsync()).ToHashSet(StringComparer.Ordinal);
}
