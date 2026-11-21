using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-05: reserva de una línea de pedido; estado derivado recalculado por cada escritor.
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", table =>
            table.HasCheckConstraint("CK_Reservations_State", "[State] IN (N'Active', N'Consumed', N'Released')"));
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();
        builder.Property(reservation => reservation.State).HasConversion<string>().HasMaxLength(16);
        // ETag de la reserva: cada liberación (y el consumo de HU-03) marca State como modificado.
        builder.Property(reservation => reservation.Version).IsRowVersion();
        builder.HasOne<CustomerOrderLine>().WithMany().HasForeignKey(reservation => reservation.CustomerOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(reservation => reservation.Allocations).WithOne().HasForeignKey(allocation => allocation.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(reservation => reservation.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
