using Auth.Service.Domain;

namespace Auth.Service.Domain.Tokens;

public interface IServiceTokenService
{
    AuthToken GenerateServiceToken(string clientId);
}
