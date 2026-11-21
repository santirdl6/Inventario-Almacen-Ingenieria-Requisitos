using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_State", "[State] IN ('Pending','Processing','Sent','Failed') AND [Type]='PurchaseOrderEmail'");
            table.HasCheckConstraint("CK_OutboxMessages_Counters", "[Cycle]>=1 AND [CycleAttempts] BETWEEN 0 AND 5 AND [Attempts]>=[CycleAttempts] AND [LeaseGeneration]>=0");
            table.HasCheckConstraint("CK_OutboxMessages_Lease", "([State]='Processing' AND [LeaseOwner] IS NOT NULL AND [LeaseUntil] IS NOT NULL AND [CurrentAttemptId] IS NOT NULL AND [FinishOperationId] IS NOT NULL) OR ([State]<>'Processing' AND [LeaseOwner] IS NULL AND [LeaseUntil] IS NULL AND [CurrentAttemptId] IS NULL AND [FinishOperationId] IS NULL)");
            table.HasCheckConstraint("CK_OutboxMessages_Accepted", "([State]='Sent' AND [SentAt] IS NOT NULL) OR ([State]<>'Sent' AND [SentAt] IS NULL)");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Type).HasMaxLength(32).IsRequired();
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.LastErrorCode).HasMaxLength(64);
        builder.Property(value => value.LastErrorSummary).HasMaxLength(500);
        builder.Property(value => value.Version).IsRowVersion();
        builder.HasIndex(value => value.DeliveryId).IsUnique();
        builder.HasIndex(value => new { value.State, value.NextAttemptAt });
        builder.HasIndex(value => new { value.State, value.LeaseUntil });
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(value => value.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseOrderDelivery>().WithOne().HasForeignKey<OutboxMessage>(value => value.DeliveryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.AuthorizedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.ActiveAuthorizationOperationId).OnDelete(DeleteBehavior.Restrict);
    }
}
