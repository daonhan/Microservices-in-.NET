using Auth.Service.Models;

namespace Auth.Service.Services;

public interface IServiceTokenService
{
    AuthToken? GenerateServiceToken(string clientId, string clientSecret);
}
