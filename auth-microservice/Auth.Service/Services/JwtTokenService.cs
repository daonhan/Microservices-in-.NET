using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Auth.Service.Infrastructure.Data;
using Auth.Service.Models;
using Auth.Service.Services.Signing;
using ECommerce.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Service.Services;

public class JwtTokenService : ITokenService
{
    private const string SigningAlgorithm = SecurityAlgorithms.HmacSha256;

    private readonly IAuthStore _authStore;
    private readonly IRsaKeyProvider _rsaKeyProvider;
    private readonly string _issuer;

    public JwtTokenService(IAuthStore authStore, IRsaKeyProvider rsaKeyProvider, AuthOptions options)
    {
        _authStore = authStore;
        _rsaKeyProvider = rsaKeyProvider;
        _issuer = options.AuthMicroserviceBaseAddress;
    }

    public async Task<AuthToken?> GenerateAuthenticationToken(string username, string password)
    {
        var user = await _authStore.VerifyUserLogin(username, password);

        if (user is null)
        {
            return null;
        }

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

        return new AuthToken(tokenString, (int)expirationTimeStamp.Subtract(DateTime.Now).TotalSeconds);
    }

    private SigningCredentials BuildSigningCredentials() => SigningAlgorithm switch
    {
        SecurityAlgorithms.RsaSha256 => new SigningCredentials(
            new RsaSecurityKey(_rsaKeyProvider.GetActivePrivateKey())
            {
                KeyId = _rsaKeyProvider.ActiveKeyId
            },
            SecurityAlgorithms.RsaSha256),
        SecurityAlgorithms.HmacSha256 => new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationExtensions.SecurityKey)),
            SecurityAlgorithms.HmacSha256),
        _ => throw new NotSupportedException($"Unsupported signing algorithm: {SigningAlgorithm}")
    };
}
