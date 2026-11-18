using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("StockBalances", table =>
        {
            table.HasCheckConstraint("CK_StockBalances_Physical", "[Physical] >= 0");
            table.HasCheckConstraint("CK_StockBalances_Reserved", "[Reserved] >= 0 AND [Reserved] <= [Physical]");
        });
        builder.HasKey(balance => new { balance.ArticleId, balance.LocationId, balance.LotId });
        builder.HasIndex(balance => new { balance.LocationId, balance.ArticleId });
        builder.Property(balance => balance.Physical).HasColumnType(StockColumns.Quantity);
        builder.Property(balance => balance.Reserved).HasColumnType(StockColumns.Quantity);
        builder.Ignore(balance => balance.Available);
        builder.Property(balance => balance.Version).IsRowVersion();
        builder.HasOne<Article>().WithMany().HasForeignKey(balance => balance.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany().HasForeignKey(balance => balance.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lot>().WithMany()
            .HasForeignKey(balance => new { balance.ArticleId, balance.LotId })
            .HasPrincipalKey(lot => new { lot.ArticleId, lot.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
