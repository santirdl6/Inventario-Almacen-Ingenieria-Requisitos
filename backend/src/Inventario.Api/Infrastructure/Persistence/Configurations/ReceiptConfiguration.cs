using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts", table =>
        {
            table.HasCheckConstraint("CK_Receipts_Confirmation", """
                ([State] = 'Draft' AND [RecordedAt] IS NULL AND [ActorId] IS NULL AND [OperationId] IS NULL AND [MovementId] IS NULL AND [CounterpartyReference] IS NULL)
                OR ([State] = 'Confirmed' AND [RecordedAt] IS NOT NULL AND [ActorId] IS NOT NULL AND [OperationId] IS NOT NULL AND [MovementId] IS NOT NULL AND [CounterpartyReference] IS NOT NULL AND [Reason] IS NOT NULL
                    AND (([SupplierDocumentReference] IS NULL AND [SupportNotApplicableReason] IS NOT NULL) OR ([SupplierDocumentReference] IS NOT NULL AND [SupportNotApplicableReason] IS NULL)))
                """);
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.SupplierDocumentReference).HasMaxLength(200);
        builder.Property(value => value.SupportNotApplicableReason).HasMaxLength(500);
        builder.Property(value => value.Reason).HasMaxLength(500);
        builder.Property(value => value.CounterpartyReference).HasMaxLength(200);
        builder.Property(value => value.Version).IsRowVersion();
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(value => value.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(value => value.MovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Lines).WithOne().HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Incidents).WithOne().HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(value => value.Incidents).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(value => new { value.PurchaseOrderId, value.OccurredAt, value.Id });
        builder.HasIndex(value => new { value.ActorId, value.OccurredAt });
        builder.HasIndex(value => value.OperationId).IsUnique();
        builder.HasIndex(value => value.MovementId).IsUnique();
    }
}
