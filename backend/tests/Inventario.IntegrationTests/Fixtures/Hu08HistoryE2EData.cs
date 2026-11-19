using System.Globalization;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Inventario.IntegrationTests.Fixtures;

// Auxiliar exclusivo del entorno E2E: no añade rutas ni escritores a la aplicación.
public static class Hu08HistoryE2EData
{
    public static async Task<object> RunAsync(string mode, Guid articleId, string database, string host,
        string runtimeUser, string runtimePassword, IReadOnlyList<E2EAccount> accounts)
    {
        MasterDataTestData.ValidateE2ERequest(database, host, accounts);
        if (mode is not ("prepare" or "snapshot") || articleId == Guid.Empty)
            throw new ArgumentException("Se requiere prepare/snapshot y el artículo E2E.");
        var keys = Path.Combine(Path.GetTempPath(), "hu08-history-" + Guid.NewGuid());
        Directory.CreateDirectory(keys);
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:Host"] = host, ["Database:Name"] = database,
                ["Database:User"] = runtimeUser, ["Database:Password"] = runtimePassword,
                ["Database:TrustServerCertificate"] = "true", ["DataProtection:KeyDirectory"] = keys,
                ["initialize-database"] = "false"
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            await using var services = MasterDataTestData.BootstrapServices(configuration,
                DatabaseConfiguration.ApplicationConnection(configuration), keys, registrations =>
                {
                    registrations.AddInventoryOperations();
                    registrations.AddStockCore();
                    registrations.AddScoped<ArticleReferenceTracker>();
                });
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var article = await db.Articles.SingleAsync(value => value.Id == articleId);
            if (!article.Sku.StartsWith("LIFE-STOCK-", StringComparison.Ordinal) || !article.TracksLot || !article.AllowsFractional)
                throw new InvalidOperationException("El auxiliar solo admite el artículo específico del recorrido poblado.");
            var account = accounts.Single(value => value.Role == "Operario");
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(account.Email)
                ?? throw new InvalidOperationException("Prepara primero las cuentas de esta ejecución E2E.");
            if (!await users.CheckPasswordAsync(user, account.Password)) throw new InvalidOperationException("Cuenta E2E ajena.");
            if (mode == "prepare")
            {
                if (!article.IsActive || await db.Set<StockEntry>().AnyAsync(value => value.ArticleId == articleId))
                    throw new InvalidOperationException("La preparación solo se ejecuta una vez sobre un artículo nuevo activo.");
                var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
                var first = new Location("E2E-A-" + suffix, "A1", "E1", "N1", LocationKind.Storage);
                var second = new Location("E2E-B-" + suffix, "A2", "E2", "N2", LocationKind.Storage);
                db.AddRange(first, second); await db.SaveChangesAsync();
                var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
                principal = await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);
                var accessor = services.GetRequiredService<IHttpContextAccessor>();
                accessor.HttpContext = new DefaultHttpContext { User = principal };
                try
                {
                    StockCommandContext Context(string type) => new(new(type, Guid.NewGuid()), DateTimeOffset.UtcNow.AddMinutes(-1),
                        "Recorrido de historial HU-08", SupportReference: "ALB-E2E", CounterpartyReference: "Contraparte E2E");
                    var receipt = StockCore.Prepare(StockOperation.Receive, Context("receipt"),
                        [new(articleId, first.Id, "L-001", 2.125m), new(articleId, second.Id, "L-002", 3m)]);
                    var shipment = StockCore.PrepareShipment(Context("shipment"), [new(articleId, first.Id, "L-001", 1m, 0m)]);
                    foreach (var command in new[] { receipt, shipment })
                    {
                        using var attempt = services.CreateScope();
                        await attempt.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteAsync(
                            new(Guid.NewGuid(), "POST", $"/api/v1/stock-probes/{command.SourceId:D}",
                                command.Operation == StockOperation.Receive ? "Receiving.Record" : "Shipping.Record", command.NormalizedBody),
                            async (context, token) => StockCore.Response(await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token)));
                    }
                }
                finally { accessor.HttpContext = null; }
            }
            var balances = await db.Set<StockBalance>().AsNoTracking().Where(value => value.ArticleId == articleId)
                .OrderBy(value => value.LocationId).ThenBy(value => value.LotId).ToListAsync();
            var entries = await db.Set<StockEntry>().AsNoTracking().Where(value => value.ArticleId == articleId)
                .OrderBy(value => value.Id).ToListAsync();
            return new { actorId = user.Id, actorName = user.UserName,
                balances = balances.Select(value => new { value.LocationId, value.LotId,
                    physical = value.Physical.ToString("F3", CultureInfo.InvariantCulture), reserved = value.Reserved.ToString("F3", CultureInfo.InvariantCulture) }),
                entries = entries.Select(value => new { value.Id, value.MovementId, value.BaseUnitSnapshot,
                    quantity = value.Quantity.ToString("F3", CultureInfo.InvariantCulture) }) };
        }
        finally { Directory.Delete(keys, recursive: true); }
    }
}
