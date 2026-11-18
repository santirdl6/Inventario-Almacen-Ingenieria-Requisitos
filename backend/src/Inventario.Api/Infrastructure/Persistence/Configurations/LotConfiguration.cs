using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> builder)
    {
        builder.ToTable("Lots", table =>
        {
            table.HasCheckConstraint("CK_Lots_Code_NotBlank", "LEN(LTRIM(RTRIM([Code]))) > 0");
            // SIN-LOTE es exclusivo del bucket sin seguimiento, y viceversa.
            table.HasCheckConstraint("CK_Lots_Untracked_Code",
                "([IsUntracked] = 1 AND [Code] = 'SIN-LOTE') OR ([IsUntracked] = 0 AND [Code] <> 'SIN-LOTE')");
        });
        builder.HasKey(lot => lot.Id);
        builder.Property(lot => lot.Id).ValueGeneratedNever();
        builder.Property(lot => lot.Code).HasMaxLength(Lot.CodeMaxLength).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        builder.HasIndex(lot => new { lot.ArticleId, lot.Code }).IsUnique();
        // Destino de la FK compuesta del saldo: el lote debe pertenecer al mismo artículo.
        builder.HasAlternateKey(lot => new { lot.ArticleId, lot.Id });
        builder.HasOne<Article>().WithMany().HasForeignKey(lot => lot.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
