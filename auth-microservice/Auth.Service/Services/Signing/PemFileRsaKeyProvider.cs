using System.Security.Cryptography;

namespace Auth.Service.Services.Signing;

public class PemFileRsaKeyProvider : IRsaKeyProvider, IDisposable
{
    private readonly RSA _privateKey;
    private readonly List<PublishedKey> _publishedKeys;
    private readonly string _activeKeyId;

    public PemFileRsaKeyProvider(SigningOptions options, IHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(options.KeyId))
        {
            throw new InvalidOperationException(
                $"{SigningOptions.SectionName}:KeyId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            throw new InvalidOperationException(
                $"{SigningOptions.SectionName}:PrivateKeyPath must be configured.");
        }

        _activeKeyId = options.KeyId;

        var privateKeyPath = ResolvePath(options.PrivateKeyPath, env);
        _privateKey = LoadRsaFromPem(privateKeyPath);

        var activePublic = RSA.Create();
        activePublic.ImportParameters(_privateKey.ExportParameters(includePrivateParameters: false));

        _publishedKeys = new List<PublishedKey> { new(options.KeyId, activePublic) };

        foreach (var prev in options.PreviousKeys)
        {
            if (string.IsNullOrWhiteSpace(prev.KeyId) || string.IsNullOrWhiteSpace(prev.PublicKeyPath))
            {
                continue;
            }

            var prevKey = LoadRsaFromPem(ResolvePath(prev.PublicKeyPath, env));
            _publishedKeys.Add(new PublishedKey(prev.KeyId, prevKey));
        }
    }

    public string ActiveKeyId => _activeKeyId;

    public RSA GetActivePrivateKey() => _privateKey;

    public IReadOnlyList<PublishedKey> GetPublishedPublicKeys() => _publishedKeys;

    public void Dispose()
    {
        _privateKey.Dispose();
        foreach (var key in _publishedKeys)
        {
            key.PublicKey.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private static string ResolvePath(string configured, IHostEnvironment env)
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    private static RSA LoadRsaFromPem(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"RSA key file not found: {path}", path);
        }

        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
