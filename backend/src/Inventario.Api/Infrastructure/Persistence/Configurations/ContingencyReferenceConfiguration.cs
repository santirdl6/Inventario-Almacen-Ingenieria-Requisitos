using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ContingencyReferenceConfiguration : IEntityTypeConfiguration<ContingencyReference>
{
    public void Configure(EntityTypeBuilder<ContingencyReference> builder)
    {
        builder.ToTable("ContingencyReferences", table =>
        {
            table.HasCheckConstraint("CK_ContingencyReferences_Type", "[Type] = 'Receive'");
            table.HasCheckConstraint("CK_ContingencyReferences_Data", "ISJSON([NormalizedData]) = 1");
        });
        builder.HasKey(value => value.VoucherId);
        builder.Property(value => value.VoucherId).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        builder.Property(value => value.Type).HasMaxLength(16);
        builder.Property(value => value.ResponsibleReference).HasMaxLength(200).IsRequired();
        builder.Property(value => value.NormalizedData).IsRequired();
        builder.HasOne<Receipt>().WithMany().HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.ReconciledOperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.ImportedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => value.ReceiptId).IsUnique();
        builder.HasIndex(value => value.ReconciledOperationId).IsUnique();
    }
}
