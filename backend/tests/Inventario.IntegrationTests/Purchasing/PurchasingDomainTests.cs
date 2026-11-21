using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Features.MasterData;
using Inventario.Api.Features.Purchasing;
using Inventario.Api.Features.Stock.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingDomainTests
{
    [Fact]
    public void Duplicate_articles_have_distinct_lines_and_frozen_orders_cannot_be_edited()
    {
        var id = Guid.NewGuid();
        var article = new PurchaseArticleSnapshot(Guid.NewGuid(), "ART", "Artículo", "UD", false, false);
        var supplier = new PurchaseSupplierSnapshot(Guid.NewGuid(), "PROV", "Proveedor", null, null);
        var date = new DateOnly(2026, 12, 1);
        var one = PurchaseOrderLine.Create(Guid.NewGuid(), id, 1, article, 10m, 0m, date);
        var two = PurchaseOrderLine.Create(Guid.NewGuid(), id, 2, article, 3m, 4.50m, date);
        var order = PurchaseOrder.Create(id, supplier, "EUR", date, Guid.NewGuid(), [one, two]);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(10m, one.PendingQuantity);
        var version = order.RevisionStamp;
        order.RequestSend();
        Assert.NotEqual(version, order.RevisionStamp);
        Assert.Equal(PurchaseOrderState.Draft, order.State);
        Assert.Throws<InvalidOperationException>(() => order.Edit(supplier, date, [one]));
        order.FailSend();
        Assert.False(order.IsEditable);
        order.RetrySend(); order.AcceptSend(DateTimeOffset.UtcNow);
        Assert.Equal(PurchaseOrderState.Sent, order.State);
        one.SetReceivedQuantity(6m); order.DeriveReceiptState();
        Assert.Equal(PurchaseOrderState.PartiallyReceived, order.State);
        one.SetReceivedQuantity(10m); two.SetReceivedQuantity(3m); order.DeriveReceiptState();
        order.AcceptSend(DateTimeOffset.UtcNow);
        Assert.Equal(PurchaseOrderState.Received, order.State);
        Assert.Throws<ArgumentOutOfRangeException>(() => one.SetReceivedQuantity(11m));
    }

    [Fact]
    public void Model_keeps_exact_precision_versions_and_restrictive_article_references()
    {
        using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused;Database=metadata;Integrated Security=true").Options);
        var order = db.Model.FindEntityType(typeof(PurchaseOrder))!;
        var line = db.Model.FindEntityType(typeof(PurchaseOrderLine))!;
        Assert.Equal(32, order.FindProperty(nameof(PurchaseOrder.Number))!.GetMaxLength());
        Assert.True(order.FindProperty(nameof(PurchaseOrder.Version))!.IsConcurrencyToken);
        Assert.Equal(3, line.FindProperty(nameof(PurchaseOrderLine.OrderedQuantity))!.GetScale());
        Assert.Equal(2, line.FindProperty(nameof(PurchaseOrderLine.UnitPrice))!.GetScale());
        Assert.All(line.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
    }

    [Fact]
    public void Delivery_is_unique_and_evidence_has_restrictive_actor_and_operation_links()
    {
        using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused;Database=metadata;Integrated Security=true").Options);
        var delivery = db.Model.FindEntityType(typeof(PurchaseOrderDelivery))!;
        Assert.Contains(delivery.GetIndexes(), index => index.IsUnique
            && index.Properties.Single().Name == nameof(PurchaseOrderDelivery.PurchaseOrderId));
        Assert.Contains(delivery.GetIndexes(), index => index.IsUnique
            && index.Properties.Single().Name == nameof(PurchaseOrderDelivery.MessageId));
        Assert.Equal(256, delivery.FindProperty(nameof(PurchaseOrderDelivery.RecipientEmail))!.GetMaxLength());
        Assert.Equal("binary(32)", delivery.FindProperty(nameof(PurchaseOrderDelivery.MessageHash))!.GetColumnType());
        foreach (var type in new[] { typeof(PurchaseOrderDelivery), typeof(OutboxMessage), typeof(PurchaseOrderAudit), typeof(PurchaseDeliveryEvent) })
            Assert.All(db.Model.FindEntityType(type)!.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        var outbox = db.Model.FindEntityType(typeof(OutboxMessage))!;
        Assert.True(outbox.FindProperty(nameof(OutboxMessage.Version))!.IsConcurrencyToken);
        Assert.Equal(500, outbox.FindProperty(nameof(OutboxMessage.LastErrorSummary))!.GetMaxLength());
    }

    [Fact]
    public void Purchasing_registration_covers_the_new_real_article_foreign_key_and_first_writer()
    {
        var registrations = new ServiceCollection();
        registrations.AddDbContext<InventoryDbContext>(options => options.UseSqlServer("Server=unused;Database=metadata;Integrated Security=true"));
        registrations.AddMasterData(); registrations.AddStockCore(); registrations.AddPurchasing();
        using var provider = registrations.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
        Assert.Contains(scope.ServiceProvider.GetServices<IArticleUsageContributor>(), contributor =>
            contributor.ReferencingEntityTypes.Contains(typeof(PurchaseOrderLine)));
    }
}
