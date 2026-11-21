using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Metadata.Model.AddSequence("PurchaseOrderNumbers").StartValue = 1;
        builder.ToTable("PurchaseOrders", table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrders_State", "[State] IN ('Draft','Sent','PartiallyReceived','Received')");
            table.HasCheckConstraint("CK_PurchaseOrders_SendStatus", "[SendStatus] IN ('None','Pending','Accepted','Failed')");
            table.HasCheckConstraint("CK_PurchaseOrders_StateSend", "([SendStatus] IN ('None','Pending','Failed') AND [State]='Draft' AND [SentAt] IS NULL) OR ([SendStatus]='Accepted' AND [State] IN ('Sent','PartiallyReceived','Received') AND [SentAt] IS NOT NULL)");
            table.HasCheckConstraint("CK_PurchaseOrders_Snapshot", "ISJSON([SupplierSnapshot])=1");
            table.HasCheckConstraint("CK_PurchaseOrders_Currency", "LEN([Currency])=3 AND [Currency] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Number).HasMaxLength(32).IsRequired()
            .HasDefaultValueSql("CONCAT('PO-', FORMAT(NEXT VALUE FOR [PurchaseOrderNumbers], '00000000'))");
        builder.HasIndex(value => value.Number).IsUnique();
        builder.Property(value => value.SupplierSnapshot).IsRequired();
        builder.Property(value => value.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(value => value.ExpectedDeliveryDate).HasColumnType("date");
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(24);
        builder.Property(value => value.SendStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.Version).IsRowVersion();
        builder.Ignore(value => value.IsEditable);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(value => value.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Lines).WithOne().HasForeignKey(value => value.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(value => new { value.CreatedAt, value.Number, value.Id });
        builder.HasIndex(value => new { value.SupplierId, value.State });
        builder.HasIndex(value => new { value.State, value.CreatedAt, value.Number, value.Id });
    }
}
