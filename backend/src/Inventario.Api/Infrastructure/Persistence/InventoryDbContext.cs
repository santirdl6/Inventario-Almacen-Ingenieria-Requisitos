using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Infrastructure.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options);
