namespace PraxisFloeckinger.Api.Authentication;

public sealed record LogoutRequest(string? RefreshToken = null);
