using Auth.Service.Domain;

namespace Auth.Service.Infrastructure.Data;

public interface IAuthStore
{
    Task<User?> VerifyUserLogin(string username, string password);
}
