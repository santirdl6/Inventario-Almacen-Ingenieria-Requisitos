using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class StockPolicyConfiguration : IEntityTypeConfiguration<StockPolicy>
{
    public void Configure(EntityTypeBuilder<StockPolicy> builder)
    {
        builder.ToTable("StockPolicies", table => table.HasCheckConstraint("CK_StockPolicies_NonNegative", "[MinimumAvailable] >= 0"));
        builder.HasKey(value => value.ArticleId);
        builder.Property(value => value.ArticleId).ValueGeneratedNever();
        builder.Property(value => value.MinimumAvailable).HasPrecision(18, 3);
        builder.Property(value => value.Version).IsRowVersion();
    }
}
