using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class StockAlertConfiguration : IEntityTypeConfiguration<StockAlert>
{
    public void Configure(EntityTypeBuilder<StockAlert> builder)
    {
        builder.ToTable("StockAlerts", table =>
        {
            table.HasCheckConstraint("CK_StockAlerts_State", "[State] IN ('Active', 'Resolved')");
            table.HasCheckConstraint("CK_StockAlerts_ResolvedAt", "([State] = 'Active' AND [ResolvedAt] IS NULL) OR ([State] = 'Resolved' AND [ResolvedAt] IS NOT NULL)");
            table.HasCheckConstraint("CK_StockAlerts_SuggestedQuantity", "[SuggestedQuantity] >= 0");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.HasOne<Article>().WithMany().HasForeignKey(value => value.ArticleId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(value => value.State).HasMaxLength(16).IsRequired();
        builder.Property(value => value.PhysicalSnapshot).HasPrecision(18, 3);
        builder.Property(value => value.ReservedSnapshot).HasPrecision(18, 3);
        builder.Property(value => value.AvailableSnapshot).HasPrecision(18, 3);
        builder.Property(value => value.MinimumSnapshot).HasPrecision(18, 3);
        builder.Property(value => value.SuggestedQuantity).HasPrecision(18, 3);
        builder.HasIndex(value => value.ArticleId).IsUnique().HasFilter("[State] = 'Active'").HasDatabaseName("IX_StockAlerts_ArticleId_Active");
        builder.HasIndex(value => new { value.State, value.OpenedAt }).IsDescending(false, true)
            .IncludeProperties(value => new { value.ArticleId, value.AvailableSnapshot, value.MinimumSnapshot, value.SuggestedQuantity });
        builder.HasIndex(value => new { value.ArticleId, value.OpenedAt }).IsDescending(false, true);
    }
}
