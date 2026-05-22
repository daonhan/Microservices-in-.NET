using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Auth.Service.Infrastructure.Data.EntityFramework;

internal sealed class EfAuthStore : IAuthStore
{
    private readonly AuthContext _context;

    public EfAuthStore(AuthContext context)
    {
        _context = context;
    }

    public Task<User?> FindByUsernameAsync(string username)
        => _context.Users.FirstOrDefaultAsync(u => u.Username == username);
}
