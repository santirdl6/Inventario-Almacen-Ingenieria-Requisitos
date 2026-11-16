using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_Reason_NotBlank", "LEN(LTRIM(RTRIM([Reason]))) > 0");
            table.HasCheckConstraint("CK_StockMovements_Support",
                "[SupportReference] IS NOT NULL OR [SupportNotApplicableReason] IS NOT NULL");
        });
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Id).ValueGeneratedNever();
        builder.Property(movement => movement.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(movement => movement.SourceDocumentType)
            .HasMaxLength(StockMovement.DocumentTypeMaxLength).IsRequired();
        builder.Property(movement => movement.Reason).HasMaxLength(StockMovement.ReasonMaxLength).IsRequired();
        builder.Property(movement => movement.SupportReference).HasMaxLength(StockMovement.ReferenceMaxLength);
        builder.Property(movement => movement.SupportNotApplicableReason).HasMaxLength(StockMovement.ReasonMaxLength);
        builder.Property(movement => movement.CounterpartyReference).HasMaxLength(StockMovement.ReferenceMaxLength);
        // Un mismo hecho documental (confirmación, reserva, liberación) se aplica una sola vez.
        builder.HasIndex(movement => new { movement.SourceDocumentType, movement.SourceDocumentId, movement.Kind })
            .IsUnique();
        builder.HasIndex(movement => movement.OperationId);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(movement => movement.OriginalMovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(movement => movement.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(movement => movement.Entries).WithOne().HasForeignKey(entry => entry.MovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(movement => movement.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
