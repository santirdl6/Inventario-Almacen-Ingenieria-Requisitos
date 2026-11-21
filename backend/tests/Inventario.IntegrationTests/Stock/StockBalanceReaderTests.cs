using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.Queries;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T006: StockBalanceReader, el agregado único por artículo (research D-02, D-03) que
// comparten GET /stock y, desde T020, GET /articles/{id}/locations de HU-02.
public sealed class StockBalanceReaderTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task An_unknown_article_has_no_aggregate()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<StockBalanceReader>();
        Assert.Null(await reader.ReadAsync(Guid.NewGuid(), null, null, false, Paging.From(null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Buckets_are_ordered_paged_and_totalled_over_the_whole_filter()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var (article, _) = await support.ArticleAsync(tracksLot: true);
        var prefix = "B" + StockApiSupport.Unique(8);
        var n01 = await support.LocationAsync(prefix + "-N01");
        var n02 = await support.LocationAsync(prefix + "-N02");
        await support.ReceiveAsync(article, n02, 4m, "L-002");
        await support.ReceiveAsync(article, n02, 3m, "L-001");
        await support.ReceiveAsync(article, n01, 5m, "L-001");
        await support.ReserveAsync(article, n01, 2m, "L-001");
        await support.ShipAsync(article, n02, 3m, "L-001");     // N02/L-001 queda a cero

        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<StockBalanceReader>();

        var visible = (await reader.ReadAsync(article, null, null, false, Paging.From(null, null), CancellationToken.None))!;
        Assert.Equal(article, visible.Article.Id);
        Assert.Equal([(prefix + "-N01", "L-001"), (prefix + "-N02", "L-002")],
            visible.Items.Select(item => (item.Location.Code, item.Lot.Code)));
        Assert.Equal(2, visible.TotalCount);
        Assert.Equal((9m, 2m, 7m), (visible.Totals.Physical, visible.Totals.Reserved, visible.Totals.Available));
        Assert.Equal(3m, visible.Items[0].Available);

        // includeEmpty muestra la fila a cero en su orden (código de ubicación y de lote) y no
        // cambia los totales.
        var all = (await reader.ReadAsync(article, null, null, true, Paging.From(null, null), CancellationToken.None))!;
        Assert.Equal([(prefix + "-N01", "L-001"), (prefix + "-N02", "L-001"), (prefix + "-N02", "L-002")],
            all.Items.Select(item => (item.Location.Code, item.Lot.Code)));
        Assert.Equal(0m, all.Items[1].Physical);
        Assert.Equal(visible.Totals, all.Totals);

        // Página 2 de tamaño 1 y filtros: los totales siguen siendo del filtro, no de la página.
        var page = (await reader.ReadAsync(article, null, null, false, Paging.From(2, 1), CancellationToken.None))!;
        Assert.Equal(prefix + "-N02", Assert.Single(page.Items).Location.Code);
        Assert.Equal((2, 1, 2), (page.Page, page.PageSize, page.TotalCount));
        Assert.Equal(visible.Totals, page.Totals);
        var location = (await reader.ReadAsync(article, n02, null, true, Paging.From(null, null), CancellationToken.None))!;
        // N02: 4 de L-002 más L-001 a cero tras enviar sus 3; la fila vacía no suma.
        Assert.Equal((4m, 0m), (location.Totals.Physical, location.Totals.Reserved));
        Assert.Equal(2, location.TotalCount);
        var lot = visible.Items[0].Lot.Id;
        var byLot = (await reader.ReadAsync(article, null, lot, true, Paging.From(null, null), CancellationToken.None))!;
        Assert.All(byLot.Items, item => Assert.Equal("L-001", item.Lot.Code));
        Assert.Equal((5m, 2m), (byLot.Totals.Physical, byLot.Totals.Reserved));
    }

    [Fact]
    public async Task Active_locations_only_filters_rows_and_totals_for_hu02()
    {
        // T020: GET /articles/{id}/locations (HU-02) delega aquí con includeInactiveLocations=false.
        // Una ubicación inactiva con saldo solo se da con datos preparados: HU-02 exige vaciarla
        // antes de desactivarla. Aquí se desactiva directamente para cubrir el filtro.
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var (article, _) = await support.ArticleAsync();
        var prefix = "A" + StockApiSupport.Unique(8);
        var active = await support.LocationAsync(prefix + "-N01");
        var inactive = await support.LocationAsync(prefix + "-N02");
        await support.ReceiveAsync(article, active, 2m);
        await support.ReceiveAsync(article, inactive, 5m);
        await using (var db = fixture.ApplicationDatabase())
        {
            var location = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                db.Set<Inventario.Api.Domain.Locations.Location>(), value => value.Id == inactive);
            location.Deactivate();
            await db.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<StockBalanceReader>();
        var all = (await reader.ReadAsync(article, null, null, false, Paging.From(null, null), CancellationToken.None))!;
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(7m, all.Totals.Physical);

        var onlyActive = (await reader.ReadAsync(article, null, null, false, Paging.From(null, null), CancellationToken.None,
            activeLocationsOnly: true))!;
        Assert.Equal(active, Assert.Single(onlyActive.Items).Location.Id);
        Assert.Equal(1, onlyActive.TotalCount);
        Assert.Equal((2m, 0m), (onlyActive.Totals.Physical, onlyActive.Totals.Reserved));
    }
}
