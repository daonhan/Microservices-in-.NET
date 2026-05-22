using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Domain.Tokens;

public class JwtTokenService : ITokenService
{
    private const string SigningAlgorithm = SecurityAlgorithms.RsaSha256;

    private readonly IRsaKeyProvider _rsaKeyProvider;
    private readonly string _issuer;

    public JwtTokenService(IRsaKeyProvider rsaKeyProvider, AuthOptions options)
    {
        _rsaKeyProvider = rsaKeyProvider;
        _issuer = options.AuthMicroserviceBaseAddress;
    }

    public Task<AuthToken?> GenerateAuthenticationToken(User user)
    {
        var signingCredentials = BuildSigningCredentials();

        var expirationTimeStamp = DateTime.Now.AddMinutes(15);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Name, user.Username),
            new Claim("user_role", user.Role)
        };

        var tokenOptions = new JwtSecurityToken(
            issuer: _issuer,
            claims: claims,
            expires: expirationTimeStamp,
            signingCredentials: signingCredentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenOptions);

        return Task.FromResult<AuthToken?>(
            new AuthToken(tokenString, (int)expirationTimeStamp.Subtract(DateTime.Now).TotalSeconds));
    }

    private SigningCredentials BuildSigningCredentials() => SigningAlgorithm switch
    {
        SecurityAlgorithms.RsaSha256 => new SigningCredentials(
            new RsaSecurityKey(_rsaKeyProvider.GetActivePrivateKey())
            {
                KeyId = _rsaKeyProvider.ActiveKeyId
            },
            SecurityAlgorithms.RsaSha256),
        _ => throw new NotSupportedException($"Unsupported signing algorithm: {SigningAlgorithm}")
    };
}
