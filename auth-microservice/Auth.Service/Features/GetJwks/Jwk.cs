namespace Auth.Service.Features.GetJwks;

public record Jwk(string kty, string use, string alg, string kid, string n, string e);
