using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptCorrectionConfiguration : IEntityTypeConfiguration<ReceiptCorrection>
{
    public void Configure(EntityTypeBuilder<ReceiptCorrection> builder)
    {
        builder.ToTable("ReceiptCorrections");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Reason).HasMaxLength(500).IsRequired();
        builder.Property(value => value.Reference).HasMaxLength(200);
        builder.HasOne<Receipt>().WithMany().HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(value => value.OriginalMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(value => value.MovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => value.MovementId).IsUnique();
        builder.HasIndex(value => value.OperationId).IsUnique();
    }
}
