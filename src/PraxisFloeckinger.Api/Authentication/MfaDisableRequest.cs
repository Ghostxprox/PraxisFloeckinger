namespace PraxisFloeckinger.Api.Authentication;

public sealed record MfaDisableRequest(string Password, string Code);
