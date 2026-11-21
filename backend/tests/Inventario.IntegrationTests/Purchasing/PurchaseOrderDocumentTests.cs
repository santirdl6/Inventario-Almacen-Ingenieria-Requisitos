using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Globalization;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseOrderDocumentTests : IAsyncLifetime
{
    private readonly IdentityFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    [Fact]
    public async Task Document_escapes_master_text_and_keeps_the_reviewed_lines_and_prices()
    {
        using var support = new PurchasingTestSupport(fixture); using var buyer = await support.SessionAsync();
        var masters = await support.MastersAsync(articleName: "<script>alert('nombre')</script>");
        var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer, masters);
        await using var db = fixture.ApplicationDatabase();
        var delivery = await db.Set<PurchaseOrderDelivery>().SingleAsync(value => value.Id == job.DeliveryId);
        var html = Encoding.UTF8.GetString(delivery.DocumentBytes);
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("10.000", html); Assert.Contains("12.50", html); Assert.Contains("EUR", html);
        Assert.Contains("2026-12-01", html);
        Assert.Equal("compras@proveedor.test", delivery.RecipientEmail);
    }

    [Fact]
    public async Task Real_smtp_acceptance_is_kept_when_the_server_closes_before_quit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var server = AcceptAndCloseAsync(listener, timeout.Token);
        try
        {
            using var support = new PurchasingTestSupport(fixture, settingsOverrides: new Dictionary<string, string?> {
                ["Smtp:Host"] = "127.0.0.1", ["Smtp:Port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture), ["Smtp:Security"] = "None"
            });
            using var buyer = await support.SessionAsync();
            var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
            Assert.True(await support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(timeout.Token));
            await server;
            await using var db = fixture.ApplicationDatabase();
            Assert.Equal(PurchaseSendStatus.Accepted, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId)).SendStatus);
        }
        finally { listener.Stop(); }
    }

    internal static async Task AcceptAndCloseAsync(TcpListener listener, CancellationToken token, bool acknowledge = true)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var stream = client.GetStream();
        using var input = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        await using var output = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        await output.WriteLineAsync("220 test.local SMTP ready");
        var data = false;
        while (await input.ReadLineAsync(token) is { } line)
        {
            if (data)
            {
                if (line != ".") continue;
                if (acknowledge) await output.WriteLineAsync("250 Message accepted");
                return; // Deliberately no QUIT response: acceptance must not be undone.
            }
            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) await output.WriteLineAsync("250-test.local\r\n250 SIZE 1000000");
            else if (line == "DATA") { data = true; await output.WriteLineAsync("354 End data with dot"); }
            else await output.WriteLineAsync("250 OK");
        }
    }
}
