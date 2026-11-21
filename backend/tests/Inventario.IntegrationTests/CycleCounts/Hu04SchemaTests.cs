using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T003 (C-01; research D-04, D-07, D-08, D-10, D-14). Esquema de conteos sobre la cadena común:
// tablas, índice filtrado de congelación, DENY DELETE, triggers 51401–51407 con la conexión de
// ejecución (inventory_runtime) y cobertura de referencias de Article (INT-HU04-01). Cada trigger
// rechaza con su código (no con 3609) y la conexión sigue utilizable.
public sealed class Hu04SchemaTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string MigrationSuffix = "_HU04_CycleCounts";
    private const int ClosedCountChanged = 51401;
    private const int ClosedCountLines = 51402;
    private const int ExpectedLineDeleted = 51403;
    private const int CountedChangedInApproval = 51404;
    private const int ScopeChangedOutsideDraft = 51405;
    private const int ScopeColumnChanged = 51406;
    private const int InvalidFreeze = 51407;
    private const int PermissionDenied = 229;
    private const int UniqueIndexViolation = 2601;

    [Fact]
    public void The_model_includes_hu04_is_fully_migrated_and_count_lines_are_covered()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        Assert.NotNull(database.Model.FindEntityType(typeof(CycleCount)));
        Assert.NotNull(database.Model.FindEntityType(typeof(CountLocation)));
        Assert.NotNull(database.Model.FindEntityType(typeof(CountLine)));
        Assert.Contains(typeof(CountLine), ArticleUsageCoverageValidator.RequiredTypes(database.Model));
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

        const string tables = "(OBJECT_ID('dbo.CycleCounts'), OBJECT_ID('dbo.CountLocations'), OBJECT_ID('dbo.CountLines'))";
        Assert.Superset(new HashSet<string>
        {
            "CK_CycleCounts_State", "CK_CycleCounts_Pairs", "CK_CycleCounts_CancelReason", "CK_CountLines_Quantities",
            "CK_CountLines_Found", "CK_CountLines_Pairs", "CK_CountLines_Shipment"
        }, await Names(database, $"SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN {tables}"));
        Assert.Superset(new HashSet<string>
        {
            "UX_CycleCounts_CountCode", "UX_CycleCounts_OperationId", "UX_CountLocations_Frozen", "UX_CountLines_Bucket"
        }, await Names(database, $"SELECT name AS [Value] FROM sys.indexes WHERE is_unique = 1 AND object_id IN {tables}"));
        // filter_definition exige VIEW DEFINITION; inventory_runtime ve has_filter.
        Assert.Equal(1, await database.Database.SqlQueryRaw<int>(
            "SELECT CAST(has_filter AS int) AS [Value] FROM sys.indexes WHERE name = 'UX_CountLocations_Frozen'").SingleAsync());
        Assert.Contains("IX_CountLocations_Location", await Names(database,
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.CountLocations')"));
        Assert.Equal(new HashSet<string>
        {
            "TR_CycleCounts_ClosedImmutable", "TR_CountLocations_Scope", "TR_CountLines_OpenCount"
        }, await Names(database, $"SELECT name AS [Value] FROM sys.triggers WHERE parent_id IN {tables}"));
        // Difference es una columna calculada persistida (data-model).
        Assert.Equal(1, await database.Database.SqlQueryRaw<int>(
            "SELECT CAST(is_persisted AS int) AS [Value] FROM sys.computed_columns "
            + "WHERE object_id = OBJECT_ID('dbo.CountLines') AND name = 'Difference'").SingleAsync());
        // La línea pertenece al alcance: FK compuesta (CountId, LocationId) a CountLocations.
        Assert.Contains("FK_CountLines_CountLocations_CountId_LocationId", await Names(database,
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.CountLines')"));
    }

    [Fact]
    public async Task The_full_cycle_runs_through_ef_with_the_triggers_in_place_and_lines_are_article_references()
    {
        var seed = await SeedAsync();
        using var factory = fixture.Factory();
        var id = Guid.NewGuid();
        await using (var database = fixture.ApplicationDatabase())
        {
            var count = CycleCount.CreateDraft(id, [seed.Location, seed.OtherLocation], seed.Actor, DateTimeOffset.UtcNow);
            database.Add(count);
            await database.SaveChangesAsync();
            Assert.Equal(CycleCount.CodeFor(id), count.CountCode);
            Assert.Matches("^CC-[0-9A-F]{12}$", count.CountCode);
            Assert.NotEmpty(count.Version);

            // Editar el alcance de un borrador: quita una ubicación y añade otra.
            count.ReplaceScope([seed.Location]);
            await database.SaveChangesAsync();
            Assert.False((await UsageAsync(factory, seed.Article)).HasReferences);

            // Iniciar: cabecera Draft → Counting, alcance congelado y líneas en el mismo SaveChanges.
            count.Start([new(seed.Article, seed.Location, seed.Lot, Lot.UntrackedCode, 10m)], seed.Actor, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
            var expected = count.Lines.Single();
            count.RecordCounted(expected.Id, 8m, seed.Actor, DateTimeOffset.UtcNow);
            var found = count.AddFound(seed.OtherArticle, seed.Location, seed.OtherLot, Lot.UntrackedCode, 3m, seed.Actor,
                DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
            count.RemoveFound(found.Id);
            await database.SaveChangesAsync();
            count.Submit(seed.Actor, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
            count.Review(expected.Id, "Rotura no registrada", seed.Actor);
            await database.SaveChangesAsync();
        }

        await using (var check = fixture.ApplicationDatabase())
        {
            var line = await check.Set<CountLine>().AsNoTracking().SingleAsync(value => value.CountId == id);
            Assert.Equal(-2m, line.Difference);
            Assert.True(await check.Set<CountLocation>().AnyAsync(value => value.CountId == id && value.IsFrozen));
        }
        var open = await UsageAsync(factory, seed.Article);
        Assert.True(open.HasReferences);
        Assert.False(open.HasHistory);
        Assert.False((await UsageAsync(factory, seed.OtherArticle)).HasReferences);

        // Confirmar: cabecera y liberación del alcance en un SaveChanges, sin tocar líneas (D-07).
        var movement = await MovementAsync(seed, id);
        await using (var database = fixture.ApplicationDatabase())
        {
            var count = await LoadAsync(database, id);
            count.Confirm(Guid.NewGuid(), movement, seed.Actor, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }
        await using (var check = fixture.ApplicationDatabase())
        {
            Assert.False(await check.Set<CountLocation>().AnyAsync(value => value.CountId == id && value.IsFrozen));
            Assert.Equal(CycleCountState.Confirmed, (await check.Set<CycleCount>().SingleAsync(value => value.Id == id)).State);
        }
        Assert.True((await UsageAsync(factory, seed.Article)).HasHistory);
    }

    [Fact]
    public async Task A_count_is_cancelled_from_counting_through_ef()
    {
        var seed = await SeedAsync();
        var id = await CountingAsync(seed);
        await using (var database = fixture.ApplicationDatabase())
        {
            var count = await LoadAsync(database, id);
            count.Cancel(Guid.NewGuid(), "Conteo repetido", seed.Actor, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }
        await using var check = fixture.ApplicationDatabase();
        Assert.False(await check.Set<CountLocation>().AnyAsync(value => value.CountId == id && value.IsFrozen));
        Assert.True(await check.Set<CountLine>().AnyAsync(value => value.CountId == id));
    }

    [Fact]
    public async Task Each_trigger_rejects_with_its_code_and_the_connection_remains_usable()
    {
        var seed = await SeedAsync();
        await using var database = fixture.ApplicationDatabase();

        // Borrador: el alcance no admite cambiar columnas distintas de IsFrozen (51406).
        var draft = await DraftAsync(seed, seed.OtherLocation);
        await AssertSqlErrorAsync(database, ScopeColumnChanged,
            $"UPDATE dbo.CountLocations SET LocationId = '{seed.Location}' WHERE CountId = '{draft}'");

        // En conteo: no se borra una línea esperada (51403) ni cambia el alcance (51405).
        var counting = await CountingAsync(seed, seed.OtherLocation);
        var line = await LineAsync(database, counting);
        await AssertSqlErrorAsync(database, ExpectedLineDeleted, $"DELETE FROM dbo.CountLines WHERE Id = '{line}'");
        await AssertSqlErrorAsync(database, ScopeChangedOutsideDraft,
            $"INSERT INTO dbo.CountLocations (CountId, LocationId, IsFrozen) VALUES ('{counting}', '{seed.ThirdLocation}', 0)");
        await AssertSqlErrorAsync(database, ScopeChangedOutsideDraft,
            $"DELETE FROM dbo.CountLocations WHERE CountId = '{counting}' AND LocationId = '{seed.OtherLocation}'");
        // Una ubicación solo puede estar congelada en un conteo abierto (índice filtrado).
        await AssertSqlErrorAsync(database, UniqueIndexViolation,
            $"UPDATE dbo.CountLocations SET IsFrozen = 1 WHERE CountId = '{await DraftAsync(seed, seed.Location)}'");

        // Pendiente de aprobación: el contado no cambia (51404); el motivo sí.
        await SubmitAsync(seed, counting);
        await AssertSqlErrorAsync(database, CountedChangedInApproval,
            $"UPDATE dbo.CountLines SET CountedPhysical = 9 WHERE Id = '{line}'");
        await database.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.CountLines SET Reason = N'Rotura', ReasonBy = '{seed.Actor}' WHERE Id = '{line}'");

        // Cerrado: cabecera (51401), líneas (51402) y volver a congelar (51407) rechazados.
        await using (var close = fixture.ApplicationDatabase())
        {
            var count = await LoadAsync(close, counting);
            count.Cancel(Guid.NewGuid(), "Prueba de triggers", seed.Actor, DateTimeOffset.UtcNow);
            await close.SaveChangesAsync();
        }
        await AssertSqlErrorAsync(database, ClosedCountChanged,
            $"UPDATE dbo.CycleCounts SET CancelReason = N'Otro' WHERE Id = '{counting}'");
        await AssertSqlErrorAsync(database, ClosedCountLines,
            $"UPDATE dbo.CountLines SET Reason = N'Otro' WHERE Id = '{line}'");
        await AssertSqlErrorAsync(database, InvalidFreeze,
            $"UPDATE dbo.CountLocations SET IsFrozen = 1 WHERE CountId = '{counting}'");
        // DENY DELETE en CycleCounts a inventory_runtime: el borrador se cancela, no se elimina (D-02).
        await AssertSqlErrorAsync(database, PermissionDenied, $"DELETE FROM dbo.CycleCounts WHERE Id = '{draft}'");

        // La conexión sigue utilizable tras cada rechazo.
        Assert.Equal(CycleCountState.Cancelled,
            (await database.Set<CycleCount>().AsNoTracking().SingleAsync(value => value.Id == counting)).State);
    }

    // --- Preparación con la conexión de ejecución ---

    private sealed record Seed(Guid Actor, Guid Article, Guid OtherArticle, Guid Location, Guid OtherLocation, Guid ThirdLocation,
        Guid Lot, Guid OtherLot);

    private async Task<Seed> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var email = await fixture.CreateUserAsync("Operario");
        var article = await fixture.NewArticleAsync("H04-" + suffix, "Artículo contado", "UD", false, false);
        var other = await fixture.NewArticleAsync("H04B-" + suffix, "Artículo hallado", "UD", false, false);
        var location = new Location("C-" + suffix, "C" + suffix[..8], "E01", "N1", LocationKind.Storage);
        var otherLocation = new Location("D-" + suffix, "D" + suffix[..8], "E01", "N1", LocationKind.Storage);
        var thirdLocation = new Location("E-" + suffix, "E" + suffix[..8], "E01", "N1", LocationKind.Storage);
        var lot = Lot.Untracked(article.Id);
        var otherLot = Lot.Untracked(other.Id);
        await using var database = fixture.ApplicationDatabase();
        database.Articles.AddRange(article, other);
        database.AddRange(location, otherLocation, thirdLocation, lot, otherLot);
        await database.SaveChangesAsync();
        var actor = await database.Users.Where(value => value.Email == email).Select(value => value.Id).SingleAsync();
        return new Seed(actor, article.Id, other.Id, location.Id, otherLocation.Id, thirdLocation.Id, lot.Id, otherLot.Id);
    }

    private async Task<Guid> DraftAsync(Seed seed, params Guid[] locations)
    {
        var count = CycleCount.CreateDraft(Guid.NewGuid(), locations, seed.Actor, DateTimeOffset.UtcNow);
        await using var database = fixture.ApplicationDatabase();
        database.Add(count);
        await database.SaveChangesAsync();
        return count.Id;
    }

    private async Task<Guid> CountingAsync(Seed seed, Guid? extraLocation = null)
    {
        var id = extraLocation is { } extra ? await DraftAsync(seed, seed.Location, extra) : await DraftAsync(seed, seed.Location);
        await using var database = fixture.ApplicationDatabase();
        var count = await LoadAsync(database, id);
        count.Start([new(seed.Article, seed.Location, seed.Lot, Lot.UntrackedCode, 10m)], seed.Actor, DateTimeOffset.UtcNow);
        count.RecordCounted(count.Lines.Single().Id, 8m, seed.Actor, DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
        return id;
    }

    private async Task SubmitAsync(Seed seed, Guid id)
    {
        await using var database = fixture.ApplicationDatabase();
        var count = await LoadAsync(database, id);
        count.Submit(seed.Actor, DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
    }

    /// <summary>Movimiento mínimo del ajuste para la FK; T009 lo creará con StockCore.</summary>
    private async Task<Guid> MovementAsync(Seed seed, Guid countId)
    {
        var movement = Guid.NewGuid();
        var operation = Guid.NewGuid();
        await using var database = fixture.ApplicationDatabase();
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO dbo.OperationResults (OperationId, ActorId, RequestHash, Method, Resource, RequiredPermission, ResultStatus, "
            + $"ResultHeaders, SourceDocumentId, RecordedAt) VALUES ('{operation}', '{seed.Actor}', REPLICATE('0', 64), N'POST', "
            + $"N'/api/v1/cycle-counts/{countId}/confirm', N'Inventory.Adjust', 200, NCHAR(123) + NCHAR(125), '{countId}', SYSDATETIMEOFFSET())");
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO dbo.StockMovements (Id, Kind, SourceDocumentType, SourceDocumentId, Reason, ActorId, OccurredAt, RecordedAt, "
            + "OperationId, SupportReference, CounterpartyNotApplicableReason) VALUES "
            + $"('{movement}', N'Adjustment', N'cycle-count', '{countId}', N'Ajuste por conteo cíclico', '{seed.Actor}', "
            + $"SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), '{operation}', N'{CycleCount.CodeFor(countId)}', "
            + "N'Ajuste de inventario: sin contraparte externa')");
        return movement;
    }

    private static Task<CycleCount> LoadAsync(InventoryDbContext database, Guid id) =>
        database.Set<CycleCount>().Include(value => value.Locations).Include(value => value.Lines).SingleAsync(value => value.Id == id);

    private static Task<Guid> LineAsync(InventoryDbContext database, Guid countId) =>
        database.Set<CountLine>().Where(value => value.CountId == countId).Select(value => value.Id).SingleAsync();

    private static async Task AssertSqlErrorAsync(InventoryDbContext database, int expected, string sql)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(sql));
        Assert.True(error.Number == expected, $"Se esperaba el error {expected} y llegó {error.Number}: {error.Message}");
        Assert.Equal(1, await database.Database.SqlQueryRaw<int>("SELECT 1 AS [Value]").SingleAsync());
    }

    private static async Task<ArticleUsage> UsageAsync(ApiFactory factory, Guid article)
    {
        using var scope = factory.Services.CreateScope();
        var contributor = scope.ServiceProvider.GetServices<IArticleUsageContributor>()
            .Single(value => value.ReferencingEntityTypes.Contains(typeof(CountLine)));
        return await contributor.ReadAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), article, CancellationToken.None);
    }

    private static async Task<HashSet<string>> Names(InventoryDbContext database, string sql) =>
        (await database.Database.SqlQueryRaw<string>(sql).ToListAsync()).ToHashSet(StringComparer.Ordinal);
}
