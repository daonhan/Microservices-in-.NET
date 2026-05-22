using Auth.Service.Domain;

namespace Auth.Service.Domain.Abstractions;

internal interface IAuthStore
{
    Task<User?> FindByUsernameAsync(string username);
}
