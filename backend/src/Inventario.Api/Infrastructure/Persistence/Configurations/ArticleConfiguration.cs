using Inventario.Api.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("Articles", table =>
        {
            table.HasCheckConstraint("CK_Articles_Sku_NotBlank", "LEN(LTRIM(RTRIM([Sku]))) > 0");
            table.HasCheckConstraint("CK_Articles_Name_NotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_Articles_BaseUnit_NotBlank", "LEN(LTRIM(RTRIM([BaseUnit]))) > 0");
        });
        builder.HasKey(article => article.Id);
        builder.Property(article => article.Id).ValueGeneratedNever();
        builder.Property(article => article.Sku).HasMaxLength(64).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        builder.HasIndex(article => article.Sku).IsUnique();
        builder.Property(article => article.Name).HasMaxLength(200).IsRequired();
        builder.Property(article => article.BaseUnit).HasMaxLength(32).IsRequired();
        builder.Property(article => article.Version).IsRowVersion();
    }
}
