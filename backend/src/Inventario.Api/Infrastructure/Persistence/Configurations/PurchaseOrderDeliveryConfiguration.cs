using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderDeliveryConfiguration : IEntityTypeConfiguration<PurchaseOrderDelivery>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderDelivery> builder)
    {
        builder.ToTable("PurchaseOrderDeliveries", table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderDeliveries_Snapshot", "ISJSON([DocumentSnapshot])=1");
            table.HasCheckConstraint("CK_PurchaseOrderDeliveries_Permission", "[RequiredPermission]='Purchasing.Send'");
            table.HasCheckConstraint("CK_PurchaseOrderDeliveries_Contacts", "LEN([RecipientEmail])>0 AND LEN([SenderEmail])>0 AND CHARINDEX(CHAR(13),[RecipientEmail]+[SenderEmail])=0 AND CHARINDEX(CHAR(10),[RecipientEmail]+[SenderEmail])=0");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.HasIndex(value => value.PurchaseOrderId).IsUnique();
        builder.HasIndex(value => value.MessageId).IsUnique();
        builder.Property(value => value.RecipientEmail).HasMaxLength(256).IsRequired();
        builder.Property(value => value.SenderEmail).HasMaxLength(256).IsRequired();
        builder.Property(value => value.MessageId).HasMaxLength(255).IsRequired();
        builder.Property(value => value.RequiredPermission).HasMaxLength(64).IsRequired();
        builder.Property(value => value.DocumentName).HasMaxLength(128).IsRequired();
        builder.Property(value => value.MediaType).HasMaxLength(100).IsRequired();
        builder.Property(value => value.DocumentHash).HasColumnType("binary(32)");
        builder.Property(value => value.MessageHash).HasColumnType("binary(32)");
        builder.Property(value => value.TemplateVersion).HasMaxLength(32).IsRequired();
        builder.Property(value => value.ReviewedToken).HasMaxLength(44).IsRequired();
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(value => value.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.RequestOperationId).OnDelete(DeleteBehavior.Restrict);
    }
}
