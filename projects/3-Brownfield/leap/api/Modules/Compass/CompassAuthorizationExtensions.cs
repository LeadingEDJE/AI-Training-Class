using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Registers the Compass module's authorization policies.
/// </summary>
public static class CompassAuthorizationExtensions
{
    /// <summary>Registers the five Compass authorization policies.</summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// These policies deliberately bind to the local development authentication scheme so that the
    /// DevBypass middleware can run before the policy does; removing the scheme name here disables
    /// local sign-in entirely. See the authorization onboarding guide for the full rationale
    /// (LEAP-1142).
    /// </remarks>
    public static IServiceCollection AddCompassAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(RolePolicy.CompassSuperAdmin, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassSuperAdminRole)));

            options.AddPolicy(RolePolicy.CompassAdmin, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassAdminRole, RolePolicy.CompassSuperAdminRole)));

            options.AddPolicy(RolePolicy.CompassOps, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole)));

            options.AddPolicy(RolePolicy.CompassSales, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassSalesRole, RolePolicy.CompassSuperAdminRole)));

            // The elevated-read tier (AC-16/FR-025) — any one of Admin, Ops, or Sales, or the root.
            // Safe to attach to a write route too: Admin and Sales already hold full write rights
            // everywhere in Compass (AC-44), so this tier grants nothing beyond what CompassAdmin does.
            options.AddPolicy(RolePolicy.CompassElevated, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassAdminRole,
                    RolePolicy.CompassOpsRole,
                    RolePolicy.CompassSalesRole,
                    RolePolicy.CompassSuperAdminRole)));

            options.AddPolicy(RolePolicy.CompassReporting, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassSalesRole,
                    RolePolicy.CompassOpsRole,
                    RolePolicy.CompassSuperAdminRole)));
        });

        return services;
    }
}
