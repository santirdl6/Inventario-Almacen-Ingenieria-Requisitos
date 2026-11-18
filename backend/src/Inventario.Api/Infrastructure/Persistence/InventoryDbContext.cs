using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Infrastructure.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<OperationResult> OperationResults => Set<OperationResult>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}
