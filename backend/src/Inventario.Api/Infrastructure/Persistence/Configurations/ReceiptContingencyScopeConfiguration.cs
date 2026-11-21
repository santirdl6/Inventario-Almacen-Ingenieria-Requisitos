using Inventario.Api.Domain.Receiving;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptContingencyScopeConfiguration : IEntityTypeConfiguration<ReceiptContingencyScope>
{
    public void Configure(EntityTypeBuilder<ReceiptContingencyScope> builder)
    {
        builder.ToTable("ReceiptContingencyScopes", table =>
        {
            table.UseSqlOutputClause(false); // AFTER UPDATE trigger protects authorized evidence.
            table.HasCheckConstraint("CK_ReceiptContingencyScopes_Json", "ISJSON([PurchaseOrders]) = 1 AND ISJSON([Locations]) = 1 AND ISJSON([ExpectedVouchers]) = 1 AND ISJSON([Differences]) = 1");
            table.HasCheckConstraint("CK_ReceiptContingencyScopes_State", "([State] = 'Suspended' AND [AuthorizedBy] IS NULL AND [AuthorizedAt] IS NULL AND [AuthorizationOperationId] IS NULL) OR ([State] = 'ReopeningAuthorized' AND [AuthorizedBy] IS NOT NULL AND [AuthorizedAt] IS NOT NULL AND [AuthorizationOperationId] IS NOT NULL AND [JournalEvidence] IS NOT NULL AND [StockEvidence] IS NOT NULL AND [PurchaseEvidence] IS NOT NULL)");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(value => value.JournalEvidence).HasMaxLength(500);
        builder.Property(value => value.StockEvidence).HasMaxLength(500);
        builder.Property(value => value.PurchaseEvidence).HasMaxLength(500);
        builder.Property(value => value.Version).IsRowVersion();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.AuthorizedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.AuthorizationOperationId).OnDelete(DeleteBehavior.Restrict);
    }
}
