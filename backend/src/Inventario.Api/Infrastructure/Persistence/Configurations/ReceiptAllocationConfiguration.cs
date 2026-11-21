using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptAllocationConfiguration : IEntityTypeConfiguration<ReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<ReceiptAllocation> builder)
    {
        builder.ToTable("ReceiptAllocations", table =>
        {
            table.HasCheckConstraint("CK_ReceiptAllocations_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_ReceiptAllocations_Snapshot", "([LotId] IS NULL AND [LocationSnapshot] IS NULL) OR ([LotId] IS NOT NULL AND [LotCode] IS NOT NULL AND [LocationSnapshot] IS NOT NULL AND ISJSON([LocationSnapshot]) = 1)");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.LotCode).HasMaxLength(64);
        builder.Property(value => value.Quantity).HasPrecision(18, 3);
        builder.HasOne<Location>().WithMany().HasForeignKey(value => value.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lot>().WithMany().HasForeignKey(value => value.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}
