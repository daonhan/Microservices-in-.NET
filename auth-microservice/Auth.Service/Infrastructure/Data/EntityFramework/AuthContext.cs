using Auth.Service.Infrastructure.Data.EntityFramework.Configurations;
using Auth.Service.Models;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Identity;

namespace Auth.Service.Infrastructure.Data.EntityFramework;

public class AuthContext : DbContext, IAuthStore
{
    private static readonly string DummyHash = "AQAAAAIAAYagAAAAEKiv9rLYG18wXY3D3K6RrWw65epqos2a30M1T6sBEdTj+G08XttZqsgurhQYE5QUdQ==";
    private readonly IPasswordHasher<User> _hasher;

    public AuthContext(DbContextOptions<AuthContext> options, IPasswordHasher<User> hasher)
        : base(options)
    {
        _hasher = hasher;
    }

    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserConfiguration());
    }

    public async Task<User?> VerifyUserLogin(string username, string password)
    {
        var user = await Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user is null)
        {
            _hasher.VerifyHashedPassword(new User { Username = "", PasswordHash = DummyHash, Role = "" }, DummyHash, password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        return result is PasswordVerificationResult.Success
                      or PasswordVerificationResult.SuccessRehashNeeded
            ? user
            : null;
    }
}
