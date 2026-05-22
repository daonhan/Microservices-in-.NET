namespace Auth.Service.Domain;

public record AuthToken(string Token, int ExpiresIn);
