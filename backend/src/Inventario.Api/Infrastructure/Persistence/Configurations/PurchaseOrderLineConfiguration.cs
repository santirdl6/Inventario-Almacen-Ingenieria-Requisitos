using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines", table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderLines_Position", "[Position]>0");
            table.HasCheckConstraint("CK_PurchaseOrderLines_Quantity", "[OrderedQuantity]>0 AND [ReceivedQuantity]>=0 AND [ReceivedQuantity]<=[OrderedQuantity]");
            table.HasCheckConstraint("CK_PurchaseOrderLines_Price", "[UnitPrice]>=0");
            table.HasCheckConstraint("CK_PurchaseOrderLines_Snapshot", "ISJSON([ArticleSnapshot])=1");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ArticleSnapshot).IsRequired();
        builder.Property(value => value.OrderedQuantity).HasPrecision(18, 3);
        builder.Property(value => value.ReceivedQuantity).HasPrecision(18, 3).HasDefaultValue(0m);
        builder.Property(value => value.UnitPrice).HasPrecision(18, 2);
        builder.Property(value => value.ExpectedDeliveryDate).HasColumnType("date");
        builder.Ignore(value => value.PendingQuantity);
        builder.HasIndex(value => new { value.PurchaseOrderId, value.Position }).IsUnique();
        builder.HasOne<Article>().WithMany().HasForeignKey(value => value.ArticleId).OnDelete(DeleteBehavior.Restrict);
    }
}
