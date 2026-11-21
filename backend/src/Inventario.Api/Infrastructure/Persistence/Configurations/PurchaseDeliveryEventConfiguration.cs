using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseDeliveryEventConfiguration : IEntityTypeConfiguration<PurchaseDeliveryEvent>
{
    public void Configure(EntityTypeBuilder<PurchaseDeliveryEvent> builder)
    {
        builder.ToTable("PurchaseDeliveryEvents", table =>
        {
            table.HasCheckConstraint("CK_PurchaseDeliveryEvents_Kind", "[Kind] IN ('Requested','Retried','Claimed','LeaseExpired','Accepted','TransientFailure','PermanentFailure','Uncertain','LateObservation')");
            table.HasCheckConstraint("CK_PurchaseDeliveryEvents_Execution", "[ExecutionKind] IN ('Human','Worker')");
            table.HasCheckConstraint("CK_PurchaseDeliveryEvents_Outcome", "ISJSON([Outcome], VALUE)=1");
            table.HasCheckConstraint("CK_PurchaseDeliveryEvents_Generation", "[LeaseGeneration] IS NULL OR [LeaseGeneration]>=0");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(24);
        builder.Property(value => value.ExecutionKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.RecordedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasOne<PurchaseOrderDelivery>().WithMany().HasForeignKey(value => value.DeliveryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.AuthorizationOperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.OperationId, value.Kind, value.DeliveryId }).IsUnique();
        builder.HasIndex(value => new { value.DeliveryId, value.RecordedAt });
    }
}
