namespace FinCore.Identity.Application.Common;

public record AuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
