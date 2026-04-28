namespace PraxisFloeckinger.Api.Authentication;

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
