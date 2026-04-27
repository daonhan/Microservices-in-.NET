namespace Auth.Service.Services.Signing;

public class SigningOptions
{
    public const string SectionName = "Authentication:Signing";

    public string PrivateKeyPath { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public List<PreviousKey> PreviousKeys { get; set; } = new();
}

public class PreviousKey
{
    public string KeyId { get; set; } = string.Empty;
    public string PublicKeyPath { get; set; } = string.Empty;
}
