using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-03 (P-02, D-08): consumo de una asignación de reserva por una línea de salida. Append-only
// (DENY UPDATE, DELETE a inventory_runtime) y ligado a la operación que confirma:
// TR_ShipmentReservationConsumptions_ConfirmingOperation rechaza insertarlo si la cabecera ya está
// confirmada con otra operación (D-06, iteración del 2026-11-21).
internal sealed class ShipmentReservationConsumptionConfiguration : IEntityTypeConfiguration<ShipmentReservationConsumption>
{
    public void Configure(EntityTypeBuilder<ShipmentReservationConsumption> builder)
    {
        builder.ToTable("ShipmentReservationConsumptions", table =>
        {
            table.HasCheckConstraint("CK_ShipmentReservationConsumptions_Quantity", "[Quantity] > 0");
            table.HasTrigger("TR_ShipmentReservationConsumptions_ConfirmingOperation");
        });
        builder.HasKey(consumption => consumption.Id);
        builder.Property(consumption => consumption.Id).ValueGeneratedNever();
        builder.Property(consumption => consumption.Quantity).HasColumnType(StockColumns.Quantity);
        builder.HasIndex(consumption => new { consumption.ShipmentLineId, consumption.ReservationAllocationId })
            .IsUnique().HasDatabaseName("UX_ShipmentReservationConsumptions_Line_Allocation");

        builder.HasOne<ShipmentLine>().WithMany().HasForeignKey(consumption => consumption.ShipmentLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReservationAllocation>().WithMany().HasForeignKey(consumption => consumption.ReservationAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
