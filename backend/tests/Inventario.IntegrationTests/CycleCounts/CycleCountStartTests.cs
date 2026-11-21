using System.Net;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T007 (C-04, C-05, US3.5; FR-002, FR-003; research D-03–D-05, INT-HU04-01). Iniciar congela todo
// el alcance y crea una línea esperada por saldo con físico > 0, con su teórico, sin tocar existencias
// y marcando la primera referencia; una ubicación ya congelada por otro conteo → 409 CountScopeFrozen
// con su código; un borrador no congela ni bloquea el inicio de otro conteo.
public sealed class CycleCountStartTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Starting_freezes_the_scope_and_captures_the_theoretical_without_changing_stock()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var u2 = await support.LocationAsync("U2-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        var (b, _) = await support.ArticleAsync();
        var (c, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 10m);
        await support.ReceiveAsync(b, u1, 5m);
        // Saldo con físico 0: no genera línea (D-05).
        await support.ReceiveAsync(c, u1, 3m);
        await support.ShipAsync(c, u1, 3m);

        var (id, etag) = await CreateAsync(operario, u1, u2);
        using (var missing = await StartAsync(operario, id, null))
            Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using var started = await StartAsync(operario, id, etag);
        Assert.True(HttpStatusCode.OK == started.StatusCode, await started.Content.ReadAsStringAsync());
        var (detail, detailTag) = await DetailAsync(operario.Client, id);
        Assert.Equal(started.Headers.ETag!.ToString(), detailTag);
        Assert.NotEqual(etag, detailTag);

        Assert.Equal("Counting", detail.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("startedAt").ValueKind);
        Assert.All(detail.GetProperty("locations").EnumerateArray(), location => Assert.True(location.GetProperty("isFrozen").GetBoolean()));
        Assert.Equal(2, detail.GetProperty("lines").GetArrayLength());
        var lineA = Line(detail, a, u1);
        Assert.Equal(("10.000", false, Lot.UntrackedCode), (lineA.GetProperty("theoreticalPhysical").GetString(),
            lineA.GetProperty("isFound").GetBoolean(), lineA.GetProperty("lotCode").GetString()));
        Assert.Equal(JsonValueKind.Null, lineA.GetProperty("countedPhysical").ValueKind);
        Assert.Equal("5.000", Line(detail, b, u1).GetProperty("theoreticalPhysical").GetString());

        // Existencias intactas, referencia del artículo registrada y contexto con artículos y reservado.
        Assert.Equal(10m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
        Assert.Equal(5m, (await support.Stock.BalanceAsync(b, u1))!.Physical);
        Assert.True((await UsageAsync(factory, a)).HasReferences);
        Assert.False((await UsageAsync(factory, c)).HasReferences);
        await using (var database = fixture.ApplicationDatabase())
            Assert.True((await database.Articles.AsNoTracking().SingleAsync(value => value.Id == a)).IsDefinitionLocked);
        var context = await ContextAsync(operario.Client, id);
        Assert.Equal(detailTag, context.GetProperty("countVersion").GetString());
        Assert.Equal(2, context.GetProperty("articles").GetArrayLength());
        Assert.Equal(2, context.GetProperty("reservedByBucket").GetArrayLength());

        // Iniciar de nuevo no corresponde al estado.
        using (var again = await StartAsync(operario, id, detailTag))
            await StockApiSupport.ProblemAsync(again, HttpStatusCode.Conflict, "CountStateConflict");
        // Ni se edita el alcance.
        using (var edit = await PutAsync(operario, id, detailTag, u1))
            await StockApiSupport.ProblemAsync(edit, HttpStatusCode.Conflict, "CountStateConflict");
    }

    [Fact]
    public async Task A_location_in_another_open_count_rejects_the_start_naming_that_count()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var u2 = await support.LocationAsync("U2-" + StockApiSupport.Unique(6));

        // Un borrador sobre U1 no congela ni impide iniciar otro conteo sobre U1 (US3.5).
        var (draft, draftTag) = await CreateAsync(operario, u1, u2);
        var (first, firstDetail, _) = await StartedAsync(operario, u1);
        var code = firstDetail.GetProperty("countCode").GetString()!;

        using (var rejected = await StartAsync(operario, draft, draftTag))
        {
            var problem = await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "CountScopeFrozen");
            Assert.Contains(code, problem.GetProperty("detail").GetString());
        }
        var (after, afterTag) = await DetailAsync(operario.Client, draft);
        Assert.Equal(draftTag, afterTag);
        Assert.Equal("Draft", after.GetProperty("state").GetString());
        Assert.All(after.GetProperty("locations").EnumerateArray(), location => Assert.False(location.GetProperty("isFrozen").GetBoolean()));
        await using var database = fixture.ApplicationDatabase();
        Assert.Equal(first, await database.Set<CountLocation>().Where(value => value.LocationId == u1 && value.IsFrozen)
            .Select(value => value.CountId).SingleAsync());
    }

    [Fact]
    public async Task A_location_deactivated_after_the_draft_rejects_the_start()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var u2 = await support.LocationAsync("U2-" + StockApiSupport.Unique(6));
        var (id, etag) = await CreateAsync(operario, u1, u2);
        await StockApiSupport.DeactivateLocationAsync(operario, u2);

        using var rejected = await StartAsync(operario, id, etag);
        var problem = await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        Assert.NotEmpty(ErrorFields(problem));
        Assert.Equal("Draft", (await DetailAsync(operario.Client, id)).Body.GetProperty("state").GetString());
    }

    private static async Task<ArticleUsage> UsageAsync(ApiFactory factory, Guid article)
    {
        using var scope = factory.Services.CreateScope();
        var contributor = scope.ServiceProvider.GetServices<IArticleUsageContributor>()
            .Single(value => value.ReferencingEntityTypes.Contains(typeof(CountLine)));
        return await contributor.ReadAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), article, CancellationToken.None);
    }
}
