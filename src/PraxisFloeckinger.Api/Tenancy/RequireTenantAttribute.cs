namespace PraxisFloeckinger.Api.Tenancy;

/// <summary>
/// Markiert einen Endpoint als tenant-pflichtig.
/// Zusammen mit <see cref="RequireTenantFilter"/> verwendet: liefert 400, wenn kein
/// <c>ITenantContext</c> für den Request aufgelöst werden konnte.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireTenantAttribute : Attribute;
