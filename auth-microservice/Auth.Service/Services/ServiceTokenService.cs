using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Auth.Service.Models;
using Auth.Service.Services.Signing;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Services;

public class ServiceTokenService : IServiceTokenService
{
    private const string ServiceRole = "service";
    private const string RoleClaimType = "user_role";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private readonly ServiceClientOptions _serviceClients;
    private readonly IRsaKeyProvider _rsaKeyProvider;
    private readonly string _issuer;

    public ServiceTokenService(
        ServiceClientOptions serviceClients,
        IRsaKeyProvider rsaKeyProvider,
        AuthOptions options)
    {
        _serviceClients = serviceClients;
        _rsaKeyProvider = rsaKeyProvider;
        _issuer = options.AuthMicroserviceBaseAddress;
    }

    public AuthToken? GenerateServiceToken(string clientId, string clientSecret)
    {
        var client = _serviceClients.Clients
            .FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal));

        if (client is null || !SecretsMatch(client.ClientSecret, clientSecret))
        {
            return null;
        }

        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(_rsaKeyProvider.GetActivePrivateKey())
            {
                KeyId = _rsaKeyProvider.ActiveKeyId
            },
            SecurityAlgorithms.RsaSha256);

        var expirationTimeStamp = DateTime.UtcNow.Add(TokenLifetime);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, client.ClientId),
            new Claim(RoleClaimType, ServiceRole)
        };

        var jwt = new JwtSecurityToken(
            issuer: _issuer,
            claims: claims,
            expires: expirationTimeStamp,
            signingCredentials: signingCredentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(jwt);

        return new AuthToken(tokenString, (int)TokenLifetime.TotalSeconds);
    }

    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
