using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Inventario.Api.Features.Stock.Core;
using Inventario.IntegrationTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Inventario.IntegrationTests.Stock;

public sealed class StockAlertBenchmarkAttribute : FactAttribute
{
    public StockAlertBenchmarkAttribute()
    {
        if (Environment.GetEnvironmentVariable("HU06_BENCHMARK") is not ("full" or "smoke"))
            Skip = "Perfil de carga HU-06 desactivado.";
    }
}

public sealed class StockAlertPerformanceTests(IdentityFixture fixture, ITestOutputHelper output) : IClassFixture<IdentityFixture>
{
    [StockAlertBenchmark]
    public async Task Evaluator_overhead_on_confirmed_stock_writes()
    {
        var smoke = Environment.GetEnvironmentVariable("HU06_BENCHMARK") == "smoke";
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory, "GestorMaestros");
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (_, location) = await stock.SeedAsync();
        var family = await api.FamilyAsync();
        var articles = new Guid[smoke ? 20 : 10_000];
        for (var i = 0; i < articles.Length; i++)
        {
            using var response = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles",
                MasterDataArticleTests.Body(family, $"BENCH-{i:D5}", lot: false));
            response.EnsureSuccessStatusCode();
            articles[i] = Guid.Parse(response.Headers.Location!.OriginalString.Split('/')[^1]);
        }

        Task<StockOperationOutcome> Receive(IEnumerable<Guid> ids) => stock.RunAsync(principal,
            StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"),
                ids.Select(id => new StockAllocation(id, location.Id, null, 1m)).ToArray()));
        for (var pass = 0; pass < (smoke ? 1 : 100); pass++)
            foreach (var batch in articles.Chunk(200)) await Receive(batch);

        var sample = new ConcurrentQueue<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == "Inventario.Replenishment" && instrument.Name == "stock.alert.evaluate.duration")
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => sample.Enqueue(value));
        listener.Start();

        async Task RunWindow(int lines, TimeSpan window)
        {
            var timer = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, smoke ? 2 : 25).Select(worker => Task.Run(async () =>
            {
                var random = new Random(6006 + worker);
                do
                {
                    var offset = random.Next(articles.Length / lines) * lines;
                    await Receive(articles.Skip(offset).Take(lines));
                } while (timer.Elapsed < window);
            })));
        }

        foreach (var lines in new[] { 1, 20 })
        {
            await RunWindow(lines, TimeSpan.FromSeconds(smoke ? 1 : 120));
            sample.Clear();
            await RunWindow(lines, TimeSpan.FromSeconds(smoke ? 1 : 600));
            var sorted = sample.Order().ToArray();
            Assert.NotEmpty(sorted);
            var p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
            var profile = smoke ? "smoke" : "full";
            output.WriteLine(FormattableString.Invariant($"HU06 {profile} líneas={lines} muestras={sorted.Length} p95={p95:F3} ms"));
            if (!smoke)
            {
                Assert.True(sorted.Length >= 1000, $"Ensayo inconcluso: {sorted.Length} muestras.");
                Assert.True(p95 < 25, $"SC-004: p95 de {p95:F3} ms con {lines} líneas.");
            }
        }
    }
}
