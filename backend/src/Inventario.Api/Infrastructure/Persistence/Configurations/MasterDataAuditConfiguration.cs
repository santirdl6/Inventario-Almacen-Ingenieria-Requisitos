using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class MasterDataAuditConfiguration : IEntityTypeConfiguration<MasterDataAudit>
{
    public void Configure(EntityTypeBuilder<MasterDataAudit> builder)
    {
        builder.ToTable("MasterDataAudit", table =>
        {
            table.HasCheckConstraint("CK_MasterDataAudit_Before", "[Before] IS NULL OR ISJSON([Before], VALUE) = 1");
            table.HasCheckConstraint("CK_MasterDataAudit_After", "[After] IS NULL OR ISJSON([After], VALUE) = 1");
            table.HasCheckConstraint("CK_MasterDataAudit_Action", "[Action] IN ('Create','Update','Deactivate','Reactivate','Delete')");
            table.HasCheckConstraint("CK_MasterDataAudit_Entity", "[EntityType] IN ('Family','Supplier','Article','Location')");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.EntityType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(value => value.Action).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationResult>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.EntityType, value.EntityId, value.RecordedAt });
        builder.HasIndex(value => new { value.OperationId, value.EntityType, value.EntityId, value.Action }).IsUnique();
    }
}
