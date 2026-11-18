using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Maintenance;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataMigrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Existing_h0_rows_require_an_authenticated_atomic_preparation_before_closing_the_schema()
    {
        using var normalFactory = fixture.Factory();
        using var normal = await MasterDataApi.LoginAsync(fixture, normalFactory);
        using var scope = fixture.BootstrapServices.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var migrator = admin.GetService<IMigrator>();
        // Reaplicar desde H0 sin maestros confirma también el camino de una base vacía.
        await migrator.MigrateAsync("20261112105657_H0ArticleReference");
        await migrator.MigrateAsync();
        Assert.Empty(await admin.Articles.ToListAsync());
        var family = await normal.FamilyAsync();
        await migrator.MigrateAsync(ArticleSchema.Expand);
        var assembly = admin.GetService<IMigrationsAssembly>();
        var expand = assembly.CreateMigration(assembly.Migrations[ArticleSchema.Expand], admin.Database.ProviderName!);
        Assert.True(expand.TargetModel.FindEntityType(typeof(Article).FullName!)!.FindProperty("FamilyId")!.IsNullable);
        var id = Guid.NewGuid(); var sku = "LEGACY-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        await admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Articles (Id,Sku,Name,BaseUnit,AllowsFractional,TracksLot,IsActive,IsDefinitionLocked,FamilyId)
            VALUES ({id},{sku},N'Artículo H0',N'UD',0,1,1,0,NULL)
            """);
        // FK técnica de H0: demuestra conservación de UUID referenciado, no integración de stock.
        await admin.Database.ExecuteSqlRawAsync("CREATE TABLE dbo.__H0Reference (ArticleId uniqueidentifier NOT NULL REFERENCES dbo.Articles(Id))");
        await admin.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.__H0Reference (ArticleId) VALUES ({id})");
        Assert.Equal(51002, (await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(ArticleSchema.Final))).Number);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await normal.Client.GetAsync($"/api/v1/articles/{id:D}")).StatusCode);

        var settings = new Dictionary<string, string?>(fixture.Settings) { ["Maintenance:Hu08FamilyPreparation"] = "true" };
        using var maintenanceFactory = new ApiFactory(settings, "Development");
        using var manager = await MasterDataApi.LoginAsync(fixture, maintenanceFactory);
        using var reader = await MasterDataApi.LoginAsync(fixture, maintenanceFactory, "Operario");
        const string path = "/api/v1/maintenance/hu08/family-map";
        var map = new { entries = new[] { new { articleId = id, familyId = family } } };
        using var anonymous = maintenanceFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, map)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.WriteAsync(HttpMethod.Post, path, map)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.Client.GetAsync("/api/v1/families")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await manager.Client.GetAsync("/api/v1/health")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.Client.PostAsJsonAsync(path, map)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.WriteAsync(HttpMethod.Post, path,
            new { entries = map.entries, actorId = Guid.NewGuid() })).StatusCode);

        var invalid = new { entries = new[] { new { articleId = id, familyId = family },
            new { articleId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), familyId = family } } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.WriteAsync(HttpMethod.Post, path, invalid)).StatusCode);
        Assert.Null((await admin.Database.SqlQuery<Hu08FamilyPreparationSnapshot>($"""
            SELECT Id,Sku,Name,FamilyId,BaseUnit,AllowsFractional,TracksLot,IsActive,IsDefinitionLocked
            FROM dbo.Articles WHERE Id={id}
            """).SingleAsync()).FamilyId);
        Assert.False(await admin.Set<StockPolicy>().AnyAsync(value => value.ArticleId == id));

        var key = Guid.NewGuid();
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER dbo.__PreparationAuditFailure ON dbo.MasterDataAudit AFTER INSERT AS
                THROW 51001, N'Fallo de auditoría de mantenimiento inducido', 1;
            """);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await manager.WriteAsync(HttpMethod.Post, path, map, key)).StatusCode); }
        finally { await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.__PreparationAuditFailure"); }
        Assert.Null((await admin.Database.SqlQuery<Hu08FamilyPreparationSnapshot>($"""
            SELECT Id,Sku,Name,FamilyId,BaseUnit,AllowsFractional,TracksLot,IsActive,IsDefinitionLocked
            FROM dbo.Articles WHERE Id={id}
            """).SingleAsync()).FamilyId);
        Assert.False(await admin.Set<StockPolicy>().AnyAsync(value => value.ArticleId == id));
        Assert.False(await admin.OperationResults.AnyAsync(value => value.OperationId == key));
        var prepared = await manager.WriteAsync(HttpMethod.Post, path, map, key);
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var replay = await manager.WriteAsync(HttpMethod.Post, path, map, key);
        Assert.Equal(await prepared.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var me = await manager.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var audit = await admin.Set<MasterDataAudit>().SingleAsync(value => value.OperationId == key);
        Assert.Equal(me.GetProperty("id").GetGuid(), audit.ActorId);
        Assert.Contains("\"familyId\":null", audit.Before!);
        Assert.Equal(0m, (await admin.Set<StockPolicy>().SingleAsync(value => value.ArticleId == id)).MinimumAvailable);

        await migrator.MigrateAsync();
        var final = await normal.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{id:D}");
        Assert.Equal(id, final.GetProperty("id").GetGuid()); Assert.Equal(sku, final.GetProperty("sku").GetString());
        Assert.Equal(family, final.GetProperty("familyId").GetGuid());
        Assert.Equal(id, await admin.Database.SqlQuery<Guid>($"SELECT ArticleId AS Value FROM dbo.__H0Reference WHERE ArticleId={id}").SingleAsync());
        await using var runtime = fixture.ApplicationDatabase();
        Assert.Equal(0, await runtime.Database.SqlQueryRaw<int>("SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') AS Value").SingleAsync());
        using var finalMaintenance = new ApiFactory(settings, "Development");
        using var finalClient = await MasterDataApi.LoginAsync(fixture, finalMaintenance);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await finalClient.Client.GetAsync("/api/v1/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await finalClient.WriteAsync(HttpMethod.Post, path, map)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await normal.WriteAsync(HttpMethod.Post, path, map)).StatusCode);
    }
}
