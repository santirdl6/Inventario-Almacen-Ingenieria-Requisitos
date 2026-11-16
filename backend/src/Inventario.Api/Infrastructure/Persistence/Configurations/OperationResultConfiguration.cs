using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

internal sealed class OperationResultConfiguration : IEntityTypeConfiguration<OperationResult>
{
    public void Configure(EntityTypeBuilder<OperationResult> builder)
    {
        builder.ToTable("OperationResults");
        builder.HasKey(result => result.OperationId);
        builder.Property(result => result.OperationId).ValueGeneratedNever();
        builder.Property(result => result.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(result => result.Method).HasMaxLength(OperationResult.MethodMaxLength).IsRequired();
        builder.Property(result => result.Resource).HasMaxLength(OperationResult.ResourceMaxLength).IsRequired();
        builder.Property(result => result.ResultBody).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(result => result.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
