using System.Security.Cryptography;

namespace Auth.Service.Domain.Abstractions;

public interface IRsaKeyProvider
{
    string ActiveKeyId { get; }
    RSA GetActivePrivateKey();
    IReadOnlyList<PublishedKey> GetPublishedPublicKeys();
}

public record PublishedKey(string KeyId, RSA PublicKey);
