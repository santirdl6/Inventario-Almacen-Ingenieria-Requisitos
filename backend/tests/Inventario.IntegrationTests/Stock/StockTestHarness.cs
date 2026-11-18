using System.Security.Claims;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
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
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// Ejecuta el núcleo de stock como lo hará cada consumidor (HU-01/03/04/05): dentro del
// InventoryOperationExecutor de A, con un principal real de Identity y el mismo patrón
// que Operations/OperationProtocolTests. Las existencias se preparan siempre por el núcleo.
internal sealed class StockTestHarness(IdentityFixture fixture, IServiceProvider services)
{
    public const string Receive = "Receiving.Record";
    public const string Reserve = "Reservations.Manage";
    public const string Ship = "Shipping.Record";

    public IdentityFixture Fixture => fixture;

    public async Task<(Guid Id, ClaimsPrincipal Principal)> ActorAsync(params string[] roles)
    {
        var email = await fixture.CreateUserAsync(roles.Length == 0 ? ["Operario", "ResponsableAlmacen"] : roles);
        using var scope = fixture.BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
        return (user.Id, await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal));
    }

    public async Task<(Article Article, Location Location)> SeedAsync(bool allowsFractional = false, bool tracksLot = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var article = await fixture.NewArticleAsync("STK-" + suffix, "Artículo de stock", "UD", allowsFractional, tracksLot);
        var location = new Location("L-" + suffix, "A" + suffix[..8], "E01", "N1", LocationKind.Storage);
        await using var database = fixture.ApplicationDatabase();
        database.Articles.Add(article);
        database.Add(location);
        await database.SaveChangesAsync();
        return (article, location);
    }

    public static StockCommandContext Context(string type, Guid? document = null) =>
        new(new StockDocumentRef(type, document ?? Guid.NewGuid()), DateTimeOffset.UtcNow.AddMinutes(-1),
            "Prueba del núcleo H0", SupportNotApplicableReason: "Fixture de integración");

    public static string Permission(StockOperation operation) => operation switch
    {
        StockOperation.Receive => Receive,
        StockOperation.Ship => Ship,
        _ => Reserve
    };

    /// <summary>Ejecuta un comando de stock dentro del protocolo común de A.</summary>
    public async Task<StockOperationOutcome> RunAsync(ClaimsPrincipal principal, Guid operationId, StockCommand command)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = principal };
        using var scope = services.CreateScope();
        try
        {
            var request = new OperationRequest(operationId, "POST",
                $"/api/v1/stock-probes/{command.SourceType}/{command.SourceId:D}/{command.Operation.ToString().ToLowerInvariant()}",
                Permission(command.Operation), command.NormalizedBody);
            var response = await scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteAsync(request,
                async (context, token) => StockCore.Response(
                    await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token)));
            return StockCore.Outcome(response);
        }
        finally { accessor.HttpContext = null; }
    }

    public Task<StockOperationOutcome> RunAsync(ClaimsPrincipal principal, StockCommand command) =>
        RunAsync(principal, Guid.NewGuid(), command);

    /// <summary>
    /// Factoría cuyo contexto falla justo después de que el ejecutor de A guarde (diario, saldos,
    /// primera referencia y OperationResult ya escritos en la transacción) y antes del commit.
    /// El ejecutor no ofrece otro punto de inyección y la FK al resultado impide guardar antes.
    /// </summary>
    public static ApiFactory FailingFactory(IdentityFixture fixture, FailAfterSaveInterceptor interceptor) =>
        new(fixture.Settings, "Development", services => services.ConfigureDbContext<InventoryDbContext>(
            (_, options) => options.AddInterceptors(interceptor)));

    public async Task<StockBalance?> BalanceAsync(Guid article, Guid location)
    {
        await using var database = fixture.ApplicationDatabase();
        return await database.Set<StockBalance>().AsNoTracking()
            .SingleOrDefaultAsync(balance => balance.ArticleId == article && balance.LocationId == location);
    }

    public async Task AssertJournalMatchesBalanceAsync(Guid article, Guid location)
    {
        await using var database = fixture.ApplicationDatabase();
        var balances = database.Set<StockBalance>().AsNoTracking()
            .Where(value => value.ArticleId == article && value.LocationId == location);
        var entries = database.Set<StockEntry>().AsNoTracking()
            .Where(entry => entry.ArticleId == article && entry.LocationId == location);
        Assert.Equal(await balances.SumAsync(value => value.Physical), await entries.SumAsync(entry => entry.PhysicalDelta));
        Assert.Equal(await balances.SumAsync(value => value.Reserved), await entries.SumAsync(entry => entry.ReservedDelta));
        foreach (var balance in await balances.ToListAsync())
            Assert.InRange(balance.Reserved, 0m, balance.Physical);
    }
}
