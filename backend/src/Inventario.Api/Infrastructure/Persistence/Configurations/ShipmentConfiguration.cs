using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-03: salida de mercancía (specs/005-registrar-salida-mercancia/data-model.md). Inmutable una vez
// confirmada: lo impide TR_Shipments_ConfirmedImmutable, creado a mano en la migración (D-06).
internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments", table =>
        {
            table.HasCheckConstraint("CK_Shipments_State", "[State] IN (N'Draft', N'Confirmed')");
            // Convergence C1: los totales declarados acompañan a la operación de la confirmación.
            table.HasCheckConstraint("CK_Shipments_OperationId",
                "([State] = N'Confirmed' AND [OperationId] IS NOT NULL AND [DeclaredConsumptionCount] IS NOT NULL "
                + "AND [DeclaredConsumedQuantity] IS NOT NULL) OR ([State] = N'Draft' AND [OperationId] IS NULL "
                + "AND [DeclaredConsumptionCount] IS NULL AND [DeclaredConsumedQuantity] IS NULL)");
            table.HasCheckConstraint("CK_Shipments_Declared",
                "([DeclaredConsumptionCount] IS NULL OR [DeclaredConsumptionCount] >= 0) "
                + "AND ([DeclaredConsumedQuantity] IS NULL OR [DeclaredConsumedQuantity] >= 0)");
            // EF no puede usar OUTPUT sin INTO en tablas con triggers (plan, riesgos).
            table.HasTrigger("TR_Shipments_ConfirmedImmutable");
        });
        builder.HasKey(shipment => shipment.Id);
        builder.Property(shipment => shipment.Id).ValueGeneratedNever();
        builder.Property(shipment => shipment.State).HasConversion<string>().HasMaxLength(16);
        // ETag de la salida (D-05): crear, editar y confirmar la renuevan.
        builder.Property(shipment => shipment.Version).IsRowVersion();
        builder.Property(shipment => shipment.DeclaredConsumedQuantity).HasColumnType(StockColumns.Quantity);
        // Sin FK, como OperationResult.SourceDocumentId: es la operación que confirma.
        builder.HasIndex(shipment => shipment.OperationId).IsUnique()
            .HasFilter("[OperationId] IS NOT NULL").HasDatabaseName("UX_Shipments_OperationId");
        builder.HasIndex(shipment => new { shipment.CustomerOrderId, shipment.RecordedAt, shipment.Id })
            .IsDescending(false, true, false).HasDatabaseName("IX_Shipments_CustomerOrder");

        builder.HasOne<CustomerOrder>().WithMany().HasForeignKey(shipment => shipment.CustomerOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        // Cascade solo es efectivo al eliminar un borrador: el trigger impide borrar confirmadas.
        builder.HasMany(shipment => shipment.Lines).WithOne().HasForeignKey(line => line.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(shipment => shipment.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
