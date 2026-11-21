using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-05: una línea por artículo (P-01); lo servido solo lo cambia HU-03.
internal sealed class CustomerOrderLineConfiguration : IEntityTypeConfiguration<CustomerOrderLine>
{
    public void Configure(EntityTypeBuilder<CustomerOrderLine> builder)
    {
        builder.ToTable("CustomerOrderLines", table =>
        {
            table.HasCheckConstraint("CK_CustomerOrderLines_Ordered", "[OrderedQuantity] > 0");
            table.HasCheckConstraint("CK_CustomerOrderLines_Shipped",
                "[ShippedQuantity] >= 0 AND [ShippedQuantity] <= [OrderedQuantity]");
        });
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.OrderedQuantity).HasColumnType(StockColumns.Quantity);
        builder.Property(line => line.ShippedQuantity).HasColumnType(StockColumns.Quantity);
        builder.Ignore(line => line.PendingQuantity);
        builder.HasIndex(line => new { line.CustomerOrderId, line.ArticleId }).IsUnique()
            .HasDatabaseName("UX_CustomerOrderLines_Order_Article");
        builder.HasOne<Article>().WithMany().HasForeignKey(line => line.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
