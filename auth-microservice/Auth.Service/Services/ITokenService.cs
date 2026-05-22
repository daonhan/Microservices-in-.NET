using Auth.Service.Domain;

namespace Auth.Service.Services;

public interface ITokenService
{
    Task<AuthToken?> GenerateAuthenticationToken(string username, string password);
}
