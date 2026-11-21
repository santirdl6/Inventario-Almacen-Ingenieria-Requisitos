using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderAuditConfiguration : IEntityTypeConfiguration<PurchaseOrderAudit>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderAudit> builder)
    {
        builder.ToTable("PurchaseOrderAudit", table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderAudit_Action", "[Action] IN ('Create','Update','RequestSend','RetrySend')");
            table.HasCheckConstraint("CK_PurchaseOrderAudit_Before", "[Before] IS NULL OR ISJSON([Before], VALUE)=1");
            table.HasCheckConstraint("CK_PurchaseOrderAudit_After", "[After] IS NULL OR ISJSON([After], VALUE)=1");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Action).HasConversion<string>().HasMaxLength(24);
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(value => value.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.PurchaseOrderId, value.RecordedAt });
        builder.HasIndex(value => new { value.OperationId, value.PurchaseOrderId, value.Action }).IsUnique();
    }
}
