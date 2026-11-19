using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class StockEntryConfiguration : IEntityTypeConfiguration<StockEntry>
{
    public void Configure(EntityTypeBuilder<StockEntry> builder)
    {
        builder.ToTable("StockEntries", table =>
        {
            table.HasCheckConstraint("CK_StockEntries_Quantity", "[Quantity] > 0");
            // Defensa adicional: los deltas solo pueden ser los de la tabla de efectos del modelo.
            table.HasCheckConstraint("CK_StockEntries_Effect", """
                ([Effect] IN ('Receive', 'AdjustUp') AND [PhysicalDelta] = [Quantity] AND [ReservedDelta] = 0)
                OR ([Effect] IN ('ShipAvailable', 'AdjustDown') AND [PhysicalDelta] = -[Quantity] AND [ReservedDelta] = 0)
                OR ([Effect] = 'Reserve' AND [PhysicalDelta] = 0 AND [ReservedDelta] = [Quantity])
                OR ([Effect] = 'Release' AND [PhysicalDelta] = 0 AND [ReservedDelta] = -[Quantity])
                OR ([Effect] = 'ShipReserved' AND [PhysicalDelta] = -[Quantity] AND [ReservedDelta] = -[Quantity])
                """);
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Quantity).HasColumnType(StockColumns.Quantity);
        builder.Property(entry => entry.PhysicalDelta).HasColumnType(StockColumns.Quantity);
        builder.Property(entry => entry.ReservedDelta).HasColumnType(StockColumns.Quantity);
        builder.Property(entry => entry.BaseUnitSnapshot).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.Effect).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(entry => new { entry.ArticleId, entry.LocationId, entry.LotId });
        builder.HasOne<Article>().WithMany().HasForeignKey(entry => entry.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany().HasForeignKey(entry => entry.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lot>().WithMany()
            .HasForeignKey(entry => new { entry.ArticleId, entry.LotId })
            .HasPrincipalKey(lot => new { lot.ArticleId, lot.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
