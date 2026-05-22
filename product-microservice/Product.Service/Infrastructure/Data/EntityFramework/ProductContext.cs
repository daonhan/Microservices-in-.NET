using Microsoft.EntityFrameworkCore;
using Product.Service.Domain;

namespace Product.Service.Infrastructure.Data.EntityFramework;

internal class ProductContext : DbContext
{
    public ProductContext(DbContextOptions<ProductContext> options)
        : base(options)
    {
    }

    public DbSet<Domain.Product> Products { get; set; } = null!;
    public DbSet<ProductType> ProductTypes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductTypeConfiguration());
    }
}
