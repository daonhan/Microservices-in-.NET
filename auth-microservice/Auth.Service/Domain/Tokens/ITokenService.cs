using Auth.Service.Domain;

namespace Auth.Service.Domain.Tokens;

public interface ITokenService
{
    Task<AuthToken?> GenerateAuthenticationToken(User user);
}
