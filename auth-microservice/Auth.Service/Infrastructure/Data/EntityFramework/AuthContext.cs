using Auth.Service.Domain;
using Auth.Service.Infrastructure.Data.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Auth.Service.Infrastructure.Data.EntityFramework;

public class AuthContext : DbContext
{
    public AuthContext(DbContextOptions<AuthContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserConfiguration());
    }
}
