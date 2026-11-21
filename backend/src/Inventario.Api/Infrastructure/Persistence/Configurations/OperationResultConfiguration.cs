using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class OperationResultConfiguration : IEntityTypeConfiguration<OperationResult>
{
    public void Configure(EntityTypeBuilder<OperationResult> builder)
    {
        builder.ToTable("OperationResults", table =>
        {
            table.HasCheckConstraint("CK_OperationResults_Success", "[ResultStatus] >= 200 AND [ResultStatus] < 300");
            table.HasCheckConstraint("CK_OperationResults_Body", "[ResultBody] IS NULL OR ISJSON([ResultBody], VALUE) = 1");
            table.HasCheckConstraint("CK_OperationResults_Headers", "ISJSON([ResultHeaders], OBJECT) = 1");
        });
        builder.HasKey(result => result.OperationId);
        builder.Property(result => result.OperationId).ValueGeneratedNever();
        builder.Property(result => result.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(result => result.Method).HasMaxLength(8).IsRequired();
        builder.Property(result => result.Resource).HasMaxLength(512).IsRequired();
        builder.Property(result => result.RequiredPermission).HasMaxLength(64).IsRequired();
        builder.Property(result => result.ResultHeaders).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(result => result.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(result => new { result.ActorId, result.RecordedAt });
    }
}
