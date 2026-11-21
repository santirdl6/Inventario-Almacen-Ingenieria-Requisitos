using Inventario.Api.Domain.Receiving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptIncidentConfiguration : IEntityTypeConfiguration<ReceiptIncident>
{
    public void Configure(EntityTypeBuilder<ReceiptIncident> builder)
    {
        builder.ToTable("ReceiptIncidents", table => table.HasCheckConstraint("CK_ReceiptIncidents_Quantity", "[Quantity] IS NULL OR [Quantity] > 0"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Kind).HasMaxLength(40);
        builder.Property(value => value.ObservedDescription).HasMaxLength(500);
        builder.Property(value => value.Quantity).HasPrecision(18, 3);
        builder.Property(value => value.ObservedUnit).HasMaxLength(32);
        builder.Property(value => value.Reason).HasMaxLength(500);
        builder.HasOne<ReceiptLine>().WithMany().HasForeignKey(value => value.ReceiptLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
