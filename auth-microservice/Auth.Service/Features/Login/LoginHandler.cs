using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Domain.Tokens;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Identity;

namespace Auth.Service.Features.Login;

internal sealed class LoginHandler
{
    private const string DummyHash = "AQAAAAIAAYagAAAAEKiv9rLYG18wXY3D3K6RrWw65epqos2a30M1T6sBEdTj+G08XttZqsgurhQYE5QUdQ==";

    private readonly IAuthStore _authStore;
    private readonly IPasswordHasher<User> _hasher;
    private readonly JwtTokenService _tokenService;
    private readonly MetricFactory _metricFactory;

    public LoginHandler(IAuthStore authStore, IPasswordHasher<User> hasher,
        JwtTokenService tokenService, MetricFactory metricFactory)
    {
        _authStore = authStore;
        _hasher = hasher;
        _tokenService = tokenService;
        _metricFactory = metricFactory;
    }

    public async Task<AuthToken?> HandleAsync(string username, string password)
    {
        var token = await AuthenticateAsync(username, password);

        _metricFactory.Counter(token is null ? "login-failure" : "login-success", "logins").Add(1);

        return token;
    }

    private async Task<AuthToken?> AuthenticateAsync(string username, string password)
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
