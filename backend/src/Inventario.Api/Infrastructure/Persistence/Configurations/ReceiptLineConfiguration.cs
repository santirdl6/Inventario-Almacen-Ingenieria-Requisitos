using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> builder)
    {
        builder.ToTable("ReceiptLines", table =>
        {
            table.HasCheckConstraint("CK_ReceiptLines_Quantities", "[AcceptedQuantity] >= 0 AND [AcceptedQuantity] <= [ReportedQuantity]");
            table.HasCheckConstraint("CK_ReceiptLines_Discrepancy", "[ReportedQuantity] = [AcceptedQuantity] OR ([DiscrepancyReason] IS NOT NULL AND LEN(LTRIM(RTRIM([DiscrepancyReason]))) > 0)");
            table.HasCheckConstraint("CK_ReceiptLines_Snapshot", "[ArticleSnapshot] IS NULL OR ISJSON([ArticleSnapshot]) = 1");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ReportedQuantity).HasPrecision(18, 3);
        builder.Property(value => value.AcceptedQuantity).HasPrecision(18, 3);
        builder.Property(value => value.OrderedAtConfirmation).HasPrecision(18, 3);
        builder.Property(value => value.ReceivedBeforeConfirmation).HasPrecision(18, 3);
        builder.Property(value => value.PendingAfterConfirmation).HasPrecision(18, 3);
        builder.Property(value => value.DiscrepancyReason).HasMaxLength(500);
        builder.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(value => value.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Allocations).WithOne().HasForeignKey(value => value.ReceiptLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(value => new { value.ReceiptId, value.PurchaseOrderLineId }).IsUnique();
    }
}
