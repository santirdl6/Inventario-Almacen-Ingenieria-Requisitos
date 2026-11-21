using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-05: cantidad comprometida en un bucket; Remaining = Allocated − Consumed − Released ≥ 0.
internal sealed class ReservationAllocationConfiguration : IEntityTypeConfiguration<ReservationAllocation>
{
    private const string Remaining = "[AllocatedQuantity] - [ConsumedQuantity] - [ReleasedQuantity]";

    public void Configure(EntityTypeBuilder<ReservationAllocation> builder)
    {
        builder.ToTable("ReservationAllocations", table =>
        {
            table.HasCheckConstraint("CK_ReservationAllocations_Allocated", "[AllocatedQuantity] > 0");
            table.HasCheckConstraint("CK_ReservationAllocations_Consumed", "[ConsumedQuantity] >= 0");
            table.HasCheckConstraint("CK_ReservationAllocations_Released", "[ReleasedQuantity] >= 0");
            table.HasCheckConstraint("CK_ReservationAllocations_Remaining", Remaining + " >= 0");
        });
        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.Id).ValueGeneratedNever();
        builder.Property(allocation => allocation.AllocatedQuantity).HasColumnType(StockColumns.Quantity);
        builder.Property(allocation => allocation.ConsumedQuantity).HasColumnType(StockColumns.Quantity);
        builder.Property(allocation => allocation.ReleasedQuantity).HasColumnType(StockColumns.Quantity);
        // Columna calculada persistida: SQL Server es la fuente; el dominio la mantiene igual en
        // memoria para sus reglas locales y EF no envía su valor.
        var remaining = builder.Property(allocation => allocation.RemainingQuantity)
            .HasColumnType(StockColumns.Quantity).HasComputedColumnSql(Remaining, stored: true);
        remaining.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        remaining.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // P-02: una asignación por bucket y reserva.
        builder.HasIndex(allocation => new { allocation.ReservationId, allocation.ArticleId, allocation.LocationId, allocation.LotId })
            .IsUnique().HasDatabaseName("UX_ReservationAllocations_Reservation_Bucket");
        builder.HasIndex(allocation => new { allocation.ArticleId, allocation.LocationId, allocation.LotId })
            .HasDatabaseName("IX_ReservationAllocations_Bucket")
            .IncludeProperties(allocation => allocation.RemainingQuantity);

        builder.HasOne<Article>().WithMany().HasForeignKey(allocation => allocation.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany().HasForeignKey(allocation => allocation.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        // Como StockBalance: el lote pertenece al artículo de la asignación.
        builder.HasOne<Lot>().WithMany()
            .HasForeignKey(allocation => new { allocation.ArticleId, allocation.LotId })
            .HasPrincipalKey(lot => new { lot.ArticleId, lot.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
