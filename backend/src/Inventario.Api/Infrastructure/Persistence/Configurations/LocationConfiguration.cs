using Inventario.Api.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations", table =>
        {
            table.HasCheckConstraint("CK_Locations_Code_NotBlank", "LEN(LTRIM(RTRIM([Code]))) > 0");
            table.HasCheckConstraint("CK_Locations_Aisle_NotBlank", "LEN(LTRIM(RTRIM([Aisle]))) > 0");
            table.HasCheckConstraint("CK_Locations_Rack_NotBlank", "LEN(LTRIM(RTRIM([Rack]))) > 0");
            table.HasCheckConstraint("CK_Locations_Level_NotBlank", "LEN(LTRIM(RTRIM([Level]))) > 0");
            table.HasCheckConstraint("CK_Locations_Kind", "[Kind] IN ('Storage', 'Receiving')");
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
