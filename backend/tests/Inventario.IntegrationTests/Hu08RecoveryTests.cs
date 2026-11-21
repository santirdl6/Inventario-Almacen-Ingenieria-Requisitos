using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Purchasing;
using Inventario.IntegrationTests.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Inventario.IntegrationTests;

public sealed class Hu08RecoveryTests(IdentityFixture fixture, ITestOutputHelper output) : IClassFixture<IdentityFixture>
{
    [RecoveryFact]
    public async Task V14_restores_full_and_logs_to_a_separate_instance_and_reconciles_a_later_receipt()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(fixture.Settings).Build();
        var database = DatabaseConfiguration.Identifier(fixture.Settings["Database:Name"]!);
        var login = DatabaseConfiguration.Identifier(fixture.Settings["Database:User"]!);
        var sourceHost = fixture.Settings["Database:Host"];
        Assert.Equal("sqlserver", sourceHost);
        var restoredSettings = new Dictionary<string, string?>(fixture.Settings) { ["Database:Host"] = "sqlrestore" };
        restoredSettings["PurchaseDelivery:Enabled"] = "false";
        var restoredConfiguration = new ConfigurationBuilder().AddInMemoryCollection(restoredSettings).Build();
        await using var source = new SqlConnection(DatabaseConfiguration.AdministratorConnection(configuration));
        await using var target = new SqlConnection(DatabaseConfiguration.AdministratorConnection(restoredConfiguration));
        await source.OpenAsync();
        await target.OpenAsync();
        Assert.NotEqual(await ScalarAsync(source, "SELECT @@SERVERNAME"), await ScalarAsync(target, "SELECT @@SERVERNAME"));

        var mail = new PurchaseDeliveryWorkerTests.ScriptedTransport();
        using var support = new PurchasingTestSupport(fixture, configure: services =>
        {
            services.RemoveAll<IPurchaseMailTransport>();
            services.AddSingleton<IPurchaseMailTransport>(mail);
        });
        var factory = support.Factory;
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (actorId, actor) = await stock.ActorAsync();
        var family = await api.FamilyAsync();
        using var articleCreated = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles",
            MasterDataArticleTests.Body(family, "V14-ARTICLE", lot: false));
        Assert.Equal(HttpStatusCode.Created, articleCreated.StatusCode);
        var article = await ReadIdAsync(articleCreated);
        using var supplierCreated = await api.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", new { code = "V14-SUPPLIER", name = "Proveedor anterior", email = "v14@proveedor.test" });
        Assert.Equal(HttpStatusCode.Created, supplierCreated.StatusCode);
        var supplier = await ReadIdAsync(supplierCreated);
        var location = new Location("V14-LOCATION", "A1", "E1", "N1", LocationKind.Storage);
        await using (var setup = fixture.ApplicationDatabase())
        {
            setup.Add(location);
            await setup.SaveChangesAsync();
        }

        StockCommand Receipt(decimal quantity, string reference) => StockCore.Prepare(StockOperation.Receive,
            StockTestHarness.Context("receipt") with { SupportReference = reference, SupportNotApplicableReason = null },
            [new(article, location.Id, null, quantity)]);

        var full = $"/v14/{database}.bak";
        var firstLog = $"/v14/{database}-1.trn";
        var lastLog = $"/v14/{database}-2.trn";
        await ExecuteAsync(source, $"ALTER DATABASE [{database}] SET RECOVERY FULL");
        await stock.RunAsync(actor, Receipt(10m, "V14-INITIAL"));
        await stock.RunAsync(actor, StockCore.Prepare(StockOperation.Reserve,
            StockTestHarness.Context("reservation"), [new(article, location.Id, null, 3m)]));
        using var buyer = await support.SessionAsync();
        var purchase = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer, (supplier, article));
        PurchaseOrderDelivery frozen;
        await using (var before = fixture.ApplicationDatabase())
            frozen = await before.Set<PurchaseOrderDelivery>().SingleAsync(value => value.Id == purchase.DeliveryId);
        await ExecuteAsync(source, $"BACKUP DATABASE [{database}] TO DISK = N'{full}' WITH INIT, CHECKSUM");

        var later = await stock.RunAsync(actor, Receipt(2m, "V14-LOG-1"));
        await ExecuteAsync(source, $"BACKUP LOG [{database}] TO DISK = N'{firstLog}' WITH INIT, CHECKSUM");
        var supplierOperation = Guid.NewGuid();
        using var updated = await api.WriteAsync(HttpMethod.Put, $"/api/v1/suppliers/{supplier:D}",
            new { code = "V14-SUPPLIER", name = "Proveedor recuperado", isActive = true },
            supplierOperation, supplierCreated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var checkpoint = (DateTimeOffset)(await ScalarAsync(source, "SELECT SYSDATETIMEOFFSET()"))!;
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.True(await factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(frozen.MimeBytes, Assert.Single(mail.Control.Messages));
        await using (var afterSend = fixture.ApplicationDatabase())
            Assert.Equal(PurchaseSendStatus.Accepted, (await afterSend.Set<PurchaseOrder>()
                .SingleAsync(value => value.Id == purchase.OrderId)).SendStatus);
        var contingencyKey = Guid.NewGuid();
        var contingency = Receipt(4m, "COMPROBANTE-V14-001");
        await stock.RunAsync(actor, contingencyKey, contingency);
        var failureTime = (DateTimeOffset)(await ScalarAsync(source, "SELECT SYSDATETIMEOFFSET()"))!;
        var watch = Stopwatch.StartNew();
        await ExecuteAsync(source, $"BACKUP LOG [{database}] TO DISK = N'{lastLog}' WITH INIT, CHECKSUM");
        var point = checkpoint.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
        try
        {
            await ExecuteAsync(target, $"RESTORE DATABASE [{database}] FROM DISK = N'{full}' WITH NORECOVERY, CHECKSUM");
            await ExecuteAsync(target, $"RESTORE LOG [{database}] FROM DISK = N'{firstLog}' WITH NORECOVERY, CHECKSUM");
            await ExecuteAsync(target, $"RESTORE LOG [{database}] FROM DISK = N'{lastLog}' WITH STOPAT = '{point}', RECOVERY, CHECKSUM");
            await AssertCheckDbAsync(target, database);
            await ExecuteAsync(target, $"""
                CREATE LOGIN [{login}] WITH PASSWORD = '{IdentityFixture.Password}';
                USE [{database}];
                ALTER USER [{login}] WITH LOGIN = [{login}];
                USE [master];
                """);
            await using (var restored = MasterDataTestData.ApplicationDatabase(restoredConfiguration))
            {
                Assert.True(await restored.Set<Family>().AnyAsync(value => value.Id == family));
                var restoredArticle = await restored.Articles.SingleAsync(value => value.Id == article);
                Assert.Equal(family, restoredArticle.FamilyId);
                Assert.True(restoredArticle.IsDefinitionLocked);
                Assert.Equal(0m, (await restored.Set<StockPolicy>().SingleAsync(value => value.ArticleId == article)).MinimumAvailable);
                Assert.Equal("Proveedor recuperado", (await restored.Set<Supplier>().SingleAsync(value => value.Id == supplier)).Name);
                Assert.Equal(2, await restored.Set<MasterDataAudit>().CountAsync(value => value.EntityId == supplier));
                Assert.True(await restored.Set<MasterDataAudit>().AnyAsync(value => value.OperationId == supplierOperation));
                Assert.True(await restored.OperationResults.AnyAsync(value => value.OperationId == supplierOperation));
                Assert.True(await restored.OperationResults.AnyAsync(value => value.OperationId == later.OperationId));
                Assert.False(await restored.OperationResults.AnyAsync(value => value.OperationId == contingencyKey));
                Assert.True(await restored.Set<StockMovement>().AnyAsync(value => value.Id == later.MovementId && value.ActorId == actorId));
                await AssertBalanceAsync(restored, article, 12m, 3m, 3);
                var denied = await Assert.ThrowsAsync<SqlException>(() => restored.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM StockMovements WHERE Id = {later.MovementId}"));
                Assert.Equal(229, denied.Number);
                var recoveredOrder = await restored.Set<PurchaseOrder>().SingleAsync(value => value.Id == purchase.OrderId);
                Assert.Equal(PurchaseSendStatus.Pending, recoveredOrder.SendStatus);
                Assert.False(recoveredOrder.IsEditable);
                var recoveredMessage = await restored.Set<PurchaseOrderDelivery>().SingleAsync(value => value.Id == purchase.DeliveryId);
                Assert.Equal(frozen.MessageId, recoveredMessage.MessageId);
                Assert.Equal(frozen.MimeBytes, recoveredMessage.MimeBytes);
                Assert.Equal(frozen.DocumentBytes, recoveredMessage.DocumentBytes);
                Assert.Equal(202, (await restored.OperationResults.SingleAsync(value => value.OperationId == purchase.Authorization)).ResultStatus);
                Assert.False(await restored.Set<PurchaseDeliveryEvent>().AnyAsync(value =>
                    value.DeliveryId == purchase.DeliveryId && value.Kind == PurchaseDeliveryEventKind.Accepted));
            }

            using var restoredFactory = new ApiFactory(restoredSettings, "Development", services =>
            {
                services.RemoveAll<IPurchaseMailTransport>();
                services.AddSingleton<IPurchaseMailTransport>(mail);
            });
            var restoredStock = new StockTestHarness(fixture, restoredFactory.Services);
            var reconciled = await restoredStock.RunAsync(actor, contingencyKey, contingency);
            var duplicate = await restoredStock.RunAsync(actor, contingencyKey, contingency);
            Assert.Equal(reconciled, duplicate);
            await using (var restored = MasterDataTestData.ApplicationDatabase(restoredConfiguration))
            {
                await AssertBalanceAsync(restored, article, 16m, 3m, 4);
                Assert.Equal(1, await restored.Set<StockMovement>().CountAsync(value => value.SupportReference == "COMPROBANTE-V14-001"));
                Assert.Equal(1, await restored.OperationResults.CountAsync(value => value.OperationId == contingencyKey));
                var outbox = await restored.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == purchase.DeliveryId);
                Assert.Equal(PurchaseOutboxState.Pending, outbox.State);
                Assert.Equal(0, outbox.Attempts);
                Assert.Equal(purchase.Authorization, outbox.ActiveAuthorizationOperationId);
                Assert.Single(mail.Control.Messages);
            }
            watch.Stop();
            var rpo = failureTime - checkpoint;
            Assert.InRange(rpo, TimeSpan.Zero, TimeSpan.FromMinutes(15));
            Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromHours(4));
            output.WriteLine($"Punto recuperado: {checkpoint:O}. Incidente simulado: {failureTime:O}.");
            output.WriteLine($"RPO técnico: {rpo.TotalSeconds:F3} s. RTO técnico, hasta conciliación: {watch.Elapsed.TotalSeconds:F3} s.");
            output.WriteLine("DBCC CHECKDB sin errores. Fuente y destino con identificadores de servidor distintos.");
            output.WriteLine("Compra y MIME restaurados al punto Pending. Se observó una salida posterior al corte; el host restaurado permaneció con correo pausado.");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(target, $"""
                IF DB_ID(N'{database}') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{database}' AND state_desc = N'ONLINE')
                        ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{database}];
                END;
                IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];
                """);
        }
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();

    private static async Task AssertBalanceAsync(InventoryDbContext db, Guid article, decimal physical, decimal reserved, int entryCount)
    {
        var balance = await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article);
        Assert.Equal(physical, balance.Physical);
        Assert.Equal(reserved, balance.Reserved);
        var entries = await db.Set<StockEntry>().Where(value => value.ArticleId == article).ToListAsync();
        Assert.Equal(entryCount, entries.Count);
        Assert.Equal(physical, entries.Sum(value => value.PhysicalDelta));
        Assert.Equal(reserved, entries.Sum(value => value.ReservedDelta));
    }

    private static async Task AssertCheckDbAsync(SqlConnection connection, string database)
    {
        var messages = new List<SqlError>();
        void Record(object sender, SqlInfoMessageEventArgs args) => messages.AddRange(args.Errors.Cast<SqlError>());
        connection.InfoMessage += Record;
        try { await ExecuteAsync(connection, $"DBCC CHECKDB ([{database}]) WITH NO_INFOMSGS, ALL_ERRORMSGS"); }
        finally { connection.InfoMessage -= Record; }
        Assert.Empty(messages);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 180 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}

public sealed class RecoveryFactAttribute : FactAttribute
{
    public RecoveryFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("Testing__RecoveryRestoreHost") != "sqlrestore")
            Skip = "Requiere las dos instancias SQL aisladas de scripts/v14.compose.yml.";
    }
}
