namespace Auth.Service.Features.GetOpenIdConfiguration;

public record OpenIdConfigurationDocument(
    string issuer,
    string jwks_uri,
    string[] id_token_signing_alg_values_supported);
