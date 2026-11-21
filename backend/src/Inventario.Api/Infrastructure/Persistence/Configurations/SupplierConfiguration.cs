using Inventario.Api.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", table =>
        {
            table.HasCheckConstraint("CK_Suppliers_Code_NotBlank", "LEN(LTRIM(RTRIM([Code]))) > 0");
            table.HasCheckConstraint("CK_Suppliers_Name_NotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Code).HasMaxLength(64).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        builder.HasIndex(value => value.Code).IsUnique();
        builder.Property(value => value.Name).HasMaxLength(200).IsRequired();
        builder.Property(value => value.Email).HasMaxLength(256);
        builder.Property(value => value.TaxIdentifier).HasMaxLength(64);
        builder.Property(value => value.Version).IsRowVersion();
    }
}
