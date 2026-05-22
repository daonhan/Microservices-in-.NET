using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Domain.Tokens;
using Microsoft.AspNetCore.Identity;

namespace Auth.Service.Services;

internal sealed class LoginHandler
{
    private const string DummyHash = "AQAAAAIAAYagAAAAEKiv9rLYG18wXY3D3K6RrWw65epqos2a30M1T6sBEdTj+G08XttZqsgurhQYE5QUdQ==";

    private readonly IAuthStore _authStore;
    private readonly IPasswordHasher<User> _hasher;
    private readonly JwtTokenService _tokenService;

    public LoginHandler(IAuthStore authStore, IPasswordHasher<User> hasher, JwtTokenService tokenService)
    {
        _authStore = authStore;
        _hasher = hasher;
        _tokenService = tokenService;
    }

    public async Task<AuthToken?> HandleAsync(string username, string password)
    {
        var user = await _authStore.FindByUsernameAsync(username);

        if (user is null)
        {
            _hasher.VerifyHashedPassword(new User { Username = "", PasswordHash = DummyHash, Role = "" }, DummyHash, password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        return result is PasswordVerificationResult.Success
                      or PasswordVerificationResult.SuccessRehashNeeded
            ? await _tokenService.GenerateAuthenticationToken(user)
            : null;
    }
}
