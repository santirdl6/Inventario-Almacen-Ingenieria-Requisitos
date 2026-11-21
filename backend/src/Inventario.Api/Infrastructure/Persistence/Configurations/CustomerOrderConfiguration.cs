using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-05: pedido de cliente mínimo (specs/004-consultar-existencias/data-model.md).
internal sealed class CustomerOrderConfiguration : IEntityTypeConfiguration<CustomerOrder>
{
    public void Configure(EntityTypeBuilder<CustomerOrder> builder)
    {
        builder.ToTable("CustomerOrders", table =>
        {
            table.HasCheckConstraint("CK_CustomerOrders_State", "[State] IN (N'Open', N'Fulfilled', N'Cancelled')");
            table.HasCheckConstraint("CK_CustomerOrders_ExternalReference_NotBlank", "LEN(LTRIM(RTRIM([ExternalReference]))) > 0");
            table.HasCheckConstraint("CK_CustomerOrders_CustomerReference_NotBlank", "LEN(LTRIM(RTRIM([CustomerReference]))) > 0");
        });
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        // Única sin distinguir mayúsculas, como Sku; los espacios extremos los recorta el dominio.
        builder.Property(order => order.ExternalReference).HasMaxLength(CustomerOrder.ExternalReferenceMaxLength)
            .IsRequired().UseCollation("Latin1_General_100_CI_AS");
        builder.HasIndex(order => order.ExternalReference).IsUnique().HasDatabaseName("UX_CustomerOrders_ExternalReference");
        builder.Property(order => order.CustomerReference).HasMaxLength(CustomerOrder.CustomerReferenceMaxLength).IsRequired();
        builder.Property(order => order.State).HasConversion<string>().HasMaxLength(16);
        // ETag del pedido: todo escritor de reservas o servido marca State como modificado (D-07).
        builder.Property(order => order.Version).IsRowVersion();
        builder.HasMany(order => order.Lines).WithOne().HasForeignKey(line => line.CustomerOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(order => order.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
