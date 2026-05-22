using System.Security.Cryptography;
using Auth.Service.Domain;
using Auth.Service.Domain.Abstractions;
using Auth.Service.Features.IssueServiceToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auth.Tests;

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string LoginUsername = "alice";
    public const string LoginPassword = "password";
    public const string ServiceClientId = "api-gateway";
    public const string ServiceClientSecret = "s3cret";
    public const string ActiveKeyId = "test-kid";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthStore>();
            services.RemoveAll<IRsaKeyProvider>();
            services.RemoveAll<ServiceClientOptions>();

            services.AddScoped<IAuthStore>(_ => new StubAuthStore(BuildUser()));
            services.AddSingleton<IRsaKeyProvider, StubRsaKeyProvider>();
            services.AddSingleton(new ServiceClientOptions
            {
                Clients =
                [
                    new ServiceClient
                    {
                        ClientId = ServiceClientId,
                        ClientSecret = ServiceClientSecret
                    }
                ]
            });
        });
    }

    private static User BuildUser()
    {
        var user = new User
        {
            Username = LoginUsername,
            Role = "User",
            PasswordHash = string.Empty
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, LoginPassword);
        return user;
    }

    private sealed class StubAuthStore : IAuthStore
    {
        private readonly User _user;

        public StubAuthStore(User user)
        {
            _user = user;
        }

        public Task<User?> FindByUsernameAsync(string username)
            => Task.FromResult(string.Equals(username, _user.Username, StringComparison.Ordinal)
                ? _user
                : null);
    }

    private sealed class StubRsaKeyProvider : IRsaKeyProvider, IDisposable
    {
        private readonly RSA _privateKey = RSA.Create(2048);
        private readonly RSA _publicKey;

        public StubRsaKeyProvider()
        {
            _publicKey = RSA.Create();
            _publicKey.ImportParameters(_privateKey.ExportParameters(includePrivateParameters: false));
        }

        public string ActiveKeyId => AuthWebApplicationFactory.ActiveKeyId;

        public RSA GetActivePrivateKey() => _privateKey;

        public IReadOnlyList<PublishedKey> GetPublishedPublicKeys()
            => [new PublishedKey(AuthWebApplicationFactory.ActiveKeyId, _publicKey)];

        public void Dispose()
        {
            _privateKey.Dispose();
            _publicKey.Dispose();
        }
    }
}
