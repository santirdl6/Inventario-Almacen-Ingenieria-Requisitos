using Inventario.Api.Domain.Receiving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptCorrectionLineConfiguration : IEntityTypeConfiguration<ReceiptCorrectionLine>
{
    public void Configure(EntityTypeBuilder<ReceiptCorrectionLine> builder)
    {
        builder.ToTable("ReceiptCorrectionLines", table => table.HasCheckConstraint("CK_ReceiptCorrectionLines_Quantity", "[Quantity] > 0"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Quantity).HasPrecision(18, 3);
        builder.HasOne<ReceiptCorrection>().WithMany().HasForeignKey(value => value.CorrectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReceiptAllocation>().WithMany().HasForeignKey(value => value.ReceiptAllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.CorrectionId, value.ReceiptAllocationId }).IsUnique();
    }
}
