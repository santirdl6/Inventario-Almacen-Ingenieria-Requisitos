using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T004 (H-02; research D-06, D-08–D-10; data-model, invariante 7). Esquema de salidas sobre la
// cadena común, cobertura de referencias de Article (INT-HU03-01), triggers de inmutabilidad, DENY de
// los consumos y consumos ligados a la operación que confirma. Todo con la conexión de ejecución
// (inventory_runtime), como Hu05SchemaTests; sin endpoints de HU-03 todavía.
public sealed class Hu03SchemaTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string MigrationSuffix = "_HU03_Shipments";
    private const int ConfirmedShipmentChanged = 51301;
    private const int ForeignConsumptionsAtConfirm = 51302;
    private const int ConfirmedLinesChanged = 51303;
    private const int LateConsumption = 51304;
    private const int PermissionDenied = 229;
    private const int ForeignKeyViolation = 547;

    [Fact]
    public void The_model_includes_hu03_is_fully_migrated_and_shipment_lines_are_covered()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        Assert.NotNull(database.Model.FindEntityType(typeof(Shipment)));
        Assert.NotNull(database.Model.FindEntityType(typeof(ShipmentLine)));
        Assert.NotNull(database.Model.FindEntityType(typeof(ShipmentReservationConsumption)));
        Assert.Contains(typeof(ShipmentLine), ArticleUsageCoverageValidator.RequiredTypes(database.Model));
        // Sin contributor registrado en AddStockCore, HU-08 respondería 503 DependencyUnavailable.
        scope.ServiceProvider.GetRequiredService<ArticleUsageCoverageValidator>().EnsureCovered();
        scope.ServiceProvider.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.EndsWith(MigrationSuffix, database.Database.GetMigrations().Last());
    }

    [Fact]
    public async Task Tables_constraints_indexes_and_triggers_exist()
    {
        await using var database = fixture.ApplicationDatabase();
        Assert.Contains(database.Database.GetAppliedMigrations(), name => name.EndsWith(MigrationSuffix, StringComparison.Ordinal));

        Assert.Superset(new HashSet<string>
        {
            "CK_Shipments_State", "CK_Shipments_OperationId", "CK_ShipmentLines_Quantity", "CK_ShipmentReservationConsumptions_Quantity"
        }, await Names(database, "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN "
            + "(OBJECT_ID('dbo.Shipments'), OBJECT_ID('dbo.ShipmentLines'), OBJECT_ID('dbo.ShipmentReservationConsumptions'))"));
        Assert.Superset(new HashSet<string>
        {
            "UX_Shipments_OperationId", "UX_ShipmentLines_Shipment_Bucket", "UX_ShipmentReservationConsumptions_Line_Allocation"
        }, await Names(database, "SELECT name AS [Value] FROM sys.indexes WHERE is_unique = 1 AND object_id IN "
            + "(OBJECT_ID('dbo.Shipments'), OBJECT_ID('dbo.ShipmentLines'), OBJECT_ID('dbo.ShipmentReservationConsumptions'))"));
        Assert.Contains("IX_Shipments_CustomerOrder", await Names(database,
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Shipments')"));
        Assert.Equal(new HashSet<string>
        {
            "TR_Shipments_ConfirmedImmutable", "TR_ShipmentLines_ConfirmedImmutable", "TR_ShipmentReservationConsumptions_ConfirmingOperation"
        }, await Names(database, "SELECT name AS [Value] FROM sys.triggers WHERE parent_id IN "
            + "(OBJECT_ID('dbo.Shipments'), OBJECT_ID('dbo.ShipmentLines'), OBJECT_ID('dbo.ShipmentReservationConsumptions'))"));
        // Sin columna OwnReservationConsumed: se deriva de los consumos (D-06, D-08).
        Assert.DoesNotContain("OwnReservationConsumed", await Names(database,
            "SELECT name AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ShipmentLines')"));
    }

    [Fact]
    public async Task A_draft_is_edited_and_deleted_through_ef_with_the_triggers_in_place()
    {
        var seed = await SeedAsync();
        await using (var database = fixture.ApplicationDatabase())
        {
            var shipment = Shipment.CreateDraft(Guid.NewGuid(), seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
                [new(seed.OrderLine, seed.Article, seed.Location, seed.Lot, 2m)], seed.Actor, DateTimeOffset.UtcNow);
            database.Add(shipment);
            await database.SaveChangesAsync();
            Assert.NotEmpty(shipment.Version);

            shipment.ReplaceDraft(DateTimeOffset.UtcNow.AddHours(-2), [new(seed.OrderLine, seed.Article, seed.Location, seed.Lot, 3m)],
                seed.Actor, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();

            database.Remove(shipment);
            await database.SaveChangesAsync();
        }

        await using var check = fixture.ApplicationDatabase();
        Assert.False(await check.Set<Shipment>().AnyAsync(value => value.CustomerOrderId == seed.Order));
        Assert.False(await check.Set<ShipmentLine>().AnyAsync(value => value.CustomerOrderLineId == seed.OrderLine));
    }

    [Fact]
    public async Task A_confirmed_shipment_its_lines_and_consumptions_cannot_change()
    {
        var seed = await SeedAsync();
        var operation = Guid.NewGuid();
        var (shipment, line, consumption) = await ConfirmedAsync(seed, operation);
        await using var database = fixture.ApplicationDatabase();

        await AssertSqlErrorAsync(database, ConfirmedShipmentChanged,
            $"UPDATE dbo.Shipments SET OccurredAt = SYSDATETIMEOFFSET() WHERE Id = '{shipment}'");
        // Con consumos, la FK Restrict desde los consumos (547) rechaza antes que los triggers; sin
        // consumos lo hace el trigger. En ambos casos la salida confirmada no se borra.
        await AssertSqlErrorAsync(database, $"DELETE FROM dbo.Shipments WHERE Id = '{shipment}'",
            ConfirmedShipmentChanged, ConfirmedLinesChanged, ForeignKeyViolation);
        await AssertSqlErrorAsync(database, ConfirmedLinesChanged,
            $"UPDATE dbo.ShipmentLines SET Quantity = Quantity + 1 WHERE Id = '{line}'");
        await AssertSqlErrorAsync(database, $"DELETE FROM dbo.ShipmentLines WHERE Id = '{line}'",
            ConfirmedLinesChanged, ForeignKeyViolation);

        // Salida confirmada sin consumos (sin reserva propia): ninguna FK lo impide, solo los triggers.
        var (bare, bareLine, _) = await ConfirmedAsync(seed, Guid.NewGuid(), withConsumption: false);
        await AssertSqlErrorAsync(database, $"DELETE FROM dbo.ShipmentLines WHERE Id = '{bareLine}'", ConfirmedLinesChanged);
        await AssertSqlErrorAsync(database, $"DELETE FROM dbo.Shipments WHERE Id = '{bare}'",
            ConfirmedShipmentChanged, ConfirmedLinesChanged);
        Assert.True(await database.Set<Shipment>().AnyAsync(value => value.Id == bare));
        await AssertSqlErrorAsync(database, ConfirmedLinesChanged,
            $"INSERT INTO dbo.ShipmentLines (Id, ShipmentId, CustomerOrderLineId, ArticleId, LocationId, LotId, Quantity) "
            + $"VALUES ('{Guid.NewGuid()}', '{shipment}', '{seed.OrderLine}', '{seed.Article}', '{seed.OtherLocation}', '{seed.Lot}', 1)");
        // Append-only: DENY UPDATE, DELETE a inventory_runtime (D-08).
        await AssertSqlErrorAsync(database, PermissionDenied,
            $"UPDATE dbo.ShipmentReservationConsumptions SET Quantity = Quantity + 1 WHERE Id = '{consumption}'");
        await AssertSqlErrorAsync(database, PermissionDenied,
            $"DELETE FROM dbo.ShipmentReservationConsumptions WHERE Id = '{consumption}'");

        var stored = await database.Set<Shipment>().AsNoTracking().Include(value => value.Lines).SingleAsync(value => value.Id == shipment);
        Assert.Equal(ShipmentState.Confirmed, stored.State);
        Assert.Equal(operation, stored.OperationId);
        Assert.Equal(2m, stored.Lines.Single().Quantity);
    }

    // Iteración del plan del 2026-11-21 (A, revisión de P-01/P-02): no se añaden consumos tras confirmar.
    [Fact]
    public async Task A_late_consumption_with_another_operation_is_rejected_by_the_database()
    {
        var seed = await SeedAsync();
        var operation = Guid.NewGuid();
        var (_, line, _) = await ConfirmedAsync(seed, operation);
        var other = await SecondAllocationAsync(seed);
        await using var database = fixture.ApplicationDatabase();

        await AssertSqlErrorAsync(database, LateConsumption, InsertConsumption(line, other, Guid.NewGuid()));
        Assert.Equal(1, await database.Set<ShipmentReservationConsumption>().CountAsync(value => value.ShipmentLineId == line));

        // M3 (decisión de B): los triggers solo hacen THROW, sin ROLLBACK explícito. Dentro de la
        // transacción del llamador el error es el del trigger (no 3609), la transacción se puede
        // deshacer sin otra excepción y la conexión sigue utilizable.
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                database.Database.ExecuteSqlRawAsync(InsertConsumption(line, other, Guid.NewGuid())));
            Assert.Equal(LateConsumption, error.Number);
            Assert.DoesNotContain(error.Errors.Cast<SqlError>(), value => value.Number == 3609);
            await transaction.RollbackAsync();
        }
        Assert.Equal(1, await database.Database.SqlQueryRaw<int>("SELECT 1 AS [Value]").SingleAsync());
        Assert.Equal(1, await database.Set<ShipmentReservationConsumption>().CountAsync(value => value.ShipmentLineId == line));
    }

    [Fact]
    public async Task Confirming_a_draft_that_already_has_a_consumption_of_another_operation_is_rejected()
    {
        var seed = await SeedAsync();
        var (shipment, line) = await DraftAsync(seed);
        await using var database = fixture.ApplicationDatabase();
        // Un consumo en un borrador no se detecta al insertarlo (límite aceptado en D-06)…
        await database.Database.ExecuteSqlRawAsync(InsertConsumption(line, seed.Allocation, Guid.NewGuid()));

        // …pero la confirmación con otra operación lo rechaza el complemento del trigger de cabecera.
        await AssertSqlErrorAsync(database, ForeignConsumptionsAtConfirm, Confirm(shipment, Guid.NewGuid(), seed.Actor));
        Assert.Equal(ShipmentState.Draft,
            (await database.Set<Shipment>().AsNoTracking().SingleAsync(value => value.Id == shipment)).State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirming_with_its_own_operation_works_in_either_statement_order(bool headerFirst)
    {
        var seed = await SeedAsync();
        var (shipment, line) = await DraftAsync(seed);
        var operation = Guid.NewGuid();
        await using var database = fixture.ApplicationDatabase();
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            if (headerFirst)
            {
                await database.Database.ExecuteSqlRawAsync(Confirm(shipment, operation, seed.Actor));
                await database.Database.ExecuteSqlRawAsync(InsertConsumption(line, seed.Allocation, operation));
            }
            else
            {
                await database.Database.ExecuteSqlRawAsync(InsertConsumption(line, seed.Allocation, operation));
                await database.Database.ExecuteSqlRawAsync(Confirm(shipment, operation, seed.Actor));
            }
            await transaction.CommitAsync();
        }

        var stored = await database.Set<Shipment>().AsNoTracking().SingleAsync(value => value.Id == shipment);
        Assert.Equal(ShipmentState.Confirmed, stored.State);
        Assert.Equal(operation, stored.OperationId);
        Assert.Equal(operation, (await database.Set<ShipmentReservationConsumption>().AsNoTracking()
            .SingleAsync(value => value.ShipmentLineId == line)).OperationId);
    }

    [Fact]
    public async Task Check_constraints_reject_invalid_rows_written_directly()
    {
        var seed = await SeedAsync();
        var (shipment, line) = await DraftAsync(seed);
        await using var database = fixture.ApplicationDatabase();

        // Confirmed sin operación y Draft con operación (CHECK State ⇔ OperationId).
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.Shipments SET State = N'Confirmed' WHERE Id = '{shipment}'"));
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.Shipments SET OperationId = '{Guid.NewGuid()}' WHERE Id = '{shipment}'"));
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.Shipments SET State = N'Cancelled', OperationId = '{Guid.NewGuid()}' WHERE Id = '{shipment}'"));
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.ShipmentLines SET Quantity = 0 WHERE Id = '{line}'"));
        // Mismo bucket dos veces en la salida.
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            $"INSERT INTO dbo.ShipmentLines (Id, ShipmentId, CustomerOrderLineId, ArticleId, LocationId, LotId, Quantity) "
            + $"VALUES ('{Guid.NewGuid()}', '{shipment}', '{seed.OrderLine}', '{seed.Article}', '{seed.Location}', '{seed.Lot}', 1)"));
        await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            $"INSERT INTO dbo.ShipmentReservationConsumptions (Id, ShipmentLineId, ReservationAllocationId, Quantity, OperationId) "
            + $"VALUES ('{Guid.NewGuid()}', '{line}', '{seed.Allocation}', 0, '{Guid.NewGuid()}')"));
    }

    [Fact]
    public async Task The_shipment_contributor_sees_draft_lines_and_stops_after_the_draft_is_deleted()
    {
        var seed = await SeedAsync();
        var (shipment, _) = await DraftAsync(seed);
        using var factory = fixture.Factory();

        Assert.True((await ShipmentUsageAsync(factory, seed.Article)).HasReferences);
        await using (var database = fixture.ApplicationDatabase())
        {
            var draft = await database.Set<Shipment>().Include(value => value.Lines).SingleAsync(value => value.Id == shipment);
            database.Remove(draft);
            await database.SaveChangesAsync();
        }
        var after = await ShipmentUsageAsync(factory, seed.Article);
        Assert.False(after.HasReferences);
        Assert.False(after.HasHistory);
    }

    // --- Preparación con la conexión de ejecución ---

    private sealed record Seed(Guid Actor, Guid Article, Guid Location, Guid OtherLocation, Guid Lot, Guid Order, Guid OrderLine,
        Guid Allocation);

    private async Task<Seed> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var article = await fixture.NewArticleAsync("H03-" + suffix, "Artículo de salida", "UD", false, false);
        var location = new Location("S-" + suffix, "S" + suffix[..8], "E01", "N1", LocationKind.Storage);
        var other = new Location("T-" + suffix, "T" + suffix[..8], "E01", "N1", LocationKind.Storage);
        var lot = Lot.Untracked(article.Id);
        var order = CustomerOrder.Create(Guid.NewGuid(), "PED-" + suffix, "Cliente de prueba", [new(article.Id, 10m)]);
        await using var database = fixture.ApplicationDatabase();
        database.Articles.Add(article);
        database.AddRange(location, other, lot, order);
        await database.SaveChangesAsync();
        var reservation = Reservation.Create(Guid.NewGuid(), order.Lines.Single().Id, [new(article.Id, location.Id, lot.Id, 4m)]);
        database.Add(reservation);
        await database.SaveChangesAsync();
        // ActorId no tiene FK (data-model): basta un identificador estable en la prueba.
        return new Seed(Guid.NewGuid(), article.Id, location.Id, other.Id, lot.Id, order.Id, order.Lines.Single().Id,
            reservation.Allocations.Single().Id);
    }

    private async Task<Guid> SecondAllocationAsync(Seed seed)
    {
        var reservation = Reservation.Create(Guid.NewGuid(), seed.OrderLine, [new(seed.Article, seed.OtherLocation, seed.Lot, 1m)]);
        await using var database = fixture.ApplicationDatabase();
        database.Add(reservation);
        await database.SaveChangesAsync();
        return reservation.Allocations.Single().Id;
    }

    private async Task<(Guid Shipment, Guid Line)> DraftAsync(Seed seed)
    {
        var shipment = Shipment.CreateDraft(Guid.NewGuid(), seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            [new(seed.OrderLine, seed.Article, seed.Location, seed.Lot, 2m)], seed.Actor, DateTimeOffset.UtcNow);
        await using var database = fixture.ApplicationDatabase();
        database.Add(shipment);
        await database.SaveChangesAsync();
        return (shipment.Id, shipment.Lines.Single().Id);
    }

    /// <summary>Confirma por EF como hará T009: cabecera y consumo con la misma operación en un SaveChanges.</summary>
    private async Task<(Guid Shipment, Guid Line, Guid Consumption)> ConfirmedAsync(Seed seed, Guid operation,
        bool withConsumption = true)
    {
        var (id, line) = await DraftAsync(seed);
        await using var database = fixture.ApplicationDatabase();
        var shipment = await database.Set<Shipment>().SingleAsync(value => value.Id == id);
        shipment.Confirm(operation, seed.Actor, DateTimeOffset.UtcNow);
        ShipmentReservationConsumption? consumption = null;
        if (withConsumption)
        {
            consumption = ShipmentReservationConsumption.Create(line, seed.Allocation, 2m, operation);
            database.Add(consumption);
        }
        await database.SaveChangesAsync();
        return (id, line, consumption?.Id ?? Guid.Empty);
    }

    private static string InsertConsumption(Guid line, Guid allocation, Guid operation) =>
        "INSERT INTO dbo.ShipmentReservationConsumptions (Id, ShipmentLineId, ReservationAllocationId, Quantity, OperationId) "
        + $"VALUES ('{Guid.NewGuid()}', '{line}', '{allocation}', 1, '{operation}')";

    private static string Confirm(Guid shipment, Guid operation, Guid actor) =>
        $"UPDATE dbo.Shipments SET State = N'Confirmed', OperationId = '{operation}', ActorId = '{actor}', "
        + $"RecordedAt = SYSDATETIMEOFFSET() WHERE Id = '{shipment}'";

    private static Task AssertSqlErrorAsync(InventoryDbContext database, int expected, string sql) =>
        AssertSqlErrorAsync(database, sql, expected);

    private static async Task AssertSqlErrorAsync(InventoryDbContext database, string sql, params int[] expected)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(sql));
        Assert.True(expected.Contains(error.Number),
            $"Se esperaba el error {string.Join(" o ", expected)} y llegó {error.Number}: {error.Message}");
    }

    private static async Task<ArticleUsage> ShipmentUsageAsync(ApiFactory factory, Guid article)
    {
        using var scope = factory.Services.CreateScope();
        var contributor = scope.ServiceProvider.GetServices<IArticleUsageContributor>()
            .Single(value => value.ReferencingEntityTypes.Contains(typeof(ShipmentLine)));
        return await contributor.ReadAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), article, CancellationToken.None);
    }

    private static async Task<HashSet<string>> Names(InventoryDbContext database, string sql) =>
        (await database.Database.SqlQueryRaw<string>(sql).ToListAsync()).ToHashSet(StringComparer.Ordinal);
}
