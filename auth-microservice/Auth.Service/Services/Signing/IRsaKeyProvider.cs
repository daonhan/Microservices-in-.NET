using System.Security.Cryptography;

namespace Auth.Service.Services.Signing;

public interface IRsaKeyProvider
{
    string ActiveKeyId { get; }
    RSA GetActivePrivateKey();
    IReadOnlyList<PublishedKey> GetPublishedPublicKeys();
}

public record PublishedKey(string KeyId, RSA PublicKey);
