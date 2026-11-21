using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-03: línea de salida. Sin columna OwnReservationConsumed: se deriva de los consumos (D-06, D-08).
// TR_ShipmentLines_ConfirmedImmutable impide insertar, cambiar o borrar líneas de una confirmada.
internal sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        builder.ToTable("ShipmentLines", table =>
        {
            table.HasCheckConstraint("CK_ShipmentLines_Quantity", "[Quantity] > 0");
            table.HasTrigger("TR_ShipmentLines_ConfirmedImmutable");
        });
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Quantity).HasColumnType(StockColumns.Quantity);
        // Spec FR-002: una línea por línea de pedido, ubicación y lote dentro de la salida.
        builder.HasIndex(line => new { line.ShipmentId, line.CustomerOrderLineId, line.LocationId, line.LotId })
            .IsUnique().HasDatabaseName("UX_ShipmentLines_Shipment_Bucket");

        builder.HasOne<CustomerOrderLine>().WithMany().HasForeignKey(line => line.CustomerOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Article>().WithMany().HasForeignKey(line => line.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany().HasForeignKey(line => line.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        // Como StockBalance y ReservationAllocation: el lote pertenece al artículo de la línea.
        builder.HasOne<Lot>().WithMany()
            .HasForeignKey(line => new { line.ArticleId, line.LotId })
            .HasPrincipalKey(lot => new { lot.ArticleId, lot.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
