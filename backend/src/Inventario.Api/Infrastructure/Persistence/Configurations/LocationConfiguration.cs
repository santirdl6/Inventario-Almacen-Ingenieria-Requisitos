using Inventario.Api.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    /// <summary>Condición SQL de la regla de caracteres; la reutiliza la migración que la introduce.</summary>
    public static string CharactersCheck(string column) =>
        $"[{column}] COLLATE Latin1_General_BIN2 NOT LIKE N'%[^-0-9A-Z]%'";

    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations", table =>
        {
            table.HasCheckConstraint("CK_Locations_Code_NotBlank", "LEN(LTRIM(RTRIM([Code]))) > 0");
            table.HasCheckConstraint("CK_Locations_Aisle_NotBlank", "LEN(LTRIM(RTRIM([Aisle]))) > 0");
            table.HasCheckConstraint("CK_Locations_Rack_NotBlank", "LEN(LTRIM(RTRIM([Rack]))) > 0");
            table.HasCheckConstraint("CK_Locations_Level_NotBlank", "LEN(LTRIM(RTRIM([Level]))) > 0");
            table.HasCheckConstraint("CK_Locations_Kind", "[Kind] IN ('Storage', 'Receiving')");
            // HU-02 FR-002/FR-003: solo A-Z, 0-9 y guion, ya en mayúsculas. Collation binaria y clase
            // explícita: con Latin1_General_100_CI_AS, «Á» o «Ñ» entrarían en el rango A-Z.
            foreach (var column in new[] { "Code", "Aisle", "Rack", "Level" })
                table.HasCheckConstraint($"CK_Locations_{column}_Chars", CharactersCheck(column));
        });
        builder.HasKey(location => location.Id);
        builder.Property(location => location.Id).ValueGeneratedNever();
        builder.Property(location => location.Code).HasMaxLength(Location.CodeMaxLength).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        builder.HasIndex(location => location.Code).IsUnique();
        builder.Property(location => location.Aisle).HasMaxLength(Location.PartMaxLength).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        builder.Property(location => location.Rack).HasMaxLength(Location.PartMaxLength).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        builder.Property(location => location.Level).HasMaxLength(Location.PartMaxLength).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        // HU-02 FR-003: un lugar físico por ubicación, también frente a las inactivas (sin filtro).
        builder.HasIndex(location => new { location.Aisle, location.Rack, location.Level }).IsUnique()
            .HasDatabaseName("UX_Locations_Aisle_Rack_Level");
        builder.Property(location => location.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(location => location.Version).IsRowVersion();
    }
}
