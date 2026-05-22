using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Domain.Tokens;

public class ServiceTokenService : IServiceTokenService
{
    private const string ServiceRole = "service";
    private const string RoleClaimType = "user_role";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private readonly IRsaKeyProvider _rsaKeyProvider;
    private readonly string _issuer;

    public ServiceTokenService(IRsaKeyProvider rsaKeyProvider, AuthOptions options)
    {
        _rsaKeyProvider = rsaKeyProvider;
        _issuer = options.AuthMicroserviceBaseAddress;
    }

    public AuthToken GenerateServiceToken(string clientId, string clientSecret)
    {
        _ = clientSecret;

        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(_rsaKeyProvider.GetActivePrivateKey())
            {
                KeyId = _rsaKeyProvider.ActiveKeyId
            },
            SecurityAlgorithms.RsaSha256);

        var expirationTimeStamp = DateTime.UtcNow.Add(TokenLifetime);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
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
}
