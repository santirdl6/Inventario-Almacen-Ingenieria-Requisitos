using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingFixture : IAsyncLifetime
{
    public IdentityFixture Database { get; } = new();
    internal PurchasingTestSupport Support { get; private set; } = null!;
    internal MasterDataConcurrencyTests.SaveBarrier Barrier { get; } = new();
    public Task<MasterDataApi> AsAsync(string role = "Operario") => Support.SessionAsync(role);
    public Task<MasterDataApi> AsContingencyAsync() => MasterDataApi.LoginAsync(Database, Support.Factory, "Operario", "ResponsableAlmacen");

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        Support = new(Database, Barrier, configure: services =>
        {
            services.RemoveAll<IPurchaseMailTransport>();
            services.AddSingleton<IPurchaseMailTransport, PurchaseDeliveryWorkerTests.ScriptedTransport>();
        });
    }

    public async Task<(Guid Order, Guid Line, Guid Article, Guid Location)> SentOrderAsync(bool tracksLot = false)
    {
        var masters = await Support.MastersAsync();
        if (tracksLot)
        {
            using var manager = await AsAsync("GestorMaestros");
            using var created = await manager.WriteAsync(HttpMethod.Post, "/api/v1/articles",
                MasterDataArticleTests.Body(await manager.FamilyAsync(), "LOT-" + Guid.NewGuid().ToString("N"), lot: true));
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
            masters = (masters.SupplierId, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid());
        }
        using var buyer = await AsAsync("Comprador");
        var job = await PurchaseDeliveryWorkerTests.PrepareAsync(Support, buyer, masters);
        Assert.True(await Support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        var document = await buyer.Client.GetFromJsonAsync<JsonElement>(job.Path);
        Assert.Equal("Accepted", document.GetProperty("sendStatus").GetString());
        var location = new Location(Guid.NewGuid(), "R-" + Guid.NewGuid().ToString("N")[..12],
            "A-" + Guid.NewGuid().ToString("N")[..12], "E01", "N01", LocationKind.Storage);
        await using var db = Database.ApplicationDatabase();
        db.Add(location);
        await db.SaveChangesAsync();
        return (job.OrderId, document.GetProperty("lines")[0].GetProperty("id").GetGuid(), masters.ArticleId, location.Id);
    }

    public async Task DisposeAsync()
    {
        Support?.Dispose();
        await Database.DisposeAsync();
    }
}
