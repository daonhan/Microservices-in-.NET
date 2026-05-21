using Microsoft.EntityFrameworkCore;
using Product.Service.Domain;
using Product.Service.Domain.Abstractions;

namespace Product.Service.Infrastructure.Data.EntityFramework;

internal class ProductContext : DbContext, IProductStore
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

    public async Task<Domain.Product?> GetById(int id)
    {
        return await Products
            .Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task CreateProduct(Domain.Product product)
    {
        Products.Add(product);

        await SaveChangesAsync();
    }

    public async Task UpdateProduct(Domain.Product product)
    {
        var existingProduct = await FindAsync<Domain.Product>(product.Id);

        if (existingProduct is not null)
        {
            await SaveChangesAsync(acceptAllChangesOnSuccess: false);
        }
    }
}
