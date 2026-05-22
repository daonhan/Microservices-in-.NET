using Auth.Service.Domain;

namespace Auth.Service.Services;

public interface IServiceTokenService
{
    AuthToken? GenerateServiceToken(string clientId, string clientSecret);
}
