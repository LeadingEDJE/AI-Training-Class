using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Registers the Compass module's authorization policies.
/// </summary>
/// <remarks>
/// Compass inherits nothing, not even root: every policy below requires only Compass role strings. A
/// timesheet SuperAdmin is deliberately not a Compass root user, and no Compass role grants anything
/// in timesheet or OOTO, so adding a timesheet or OOTO role to one of these lists reverses an owner
/// decision. The OOTO policies use a compound "module role or SuperAdmin" shape; do not copy it here,
/// it is exactly what Compass must not do.
///
/// The Compass-internal root (<see cref="RolePolicy.CompassSuperAdminRole"/>) does satisfy the other
/// three Compass policies. That is Compass's own root, inside the module, and it is not our
/// <see cref="RolePolicy.SuperAdmin"/>.
/// </remarks>
public static class CompassAuthorizationExtensions
{
    /// <summary>Registers the five Compass authorization policies.</summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// These policies deliberately name no authentication scheme, and that is load-bearing. A policy
    /// that names one makes authorization re-authenticate through it, discarding whatever
    /// <c>HttpContext.User</c> already held — which silently breaks the local DevBypass middleware,
    /// because DevBypass sets the user directly rather than being a scheme. With a migration token
    /// configured, naming schemes here made every Compass route answer 401 for a DevBypass developer.
    ///
    /// The migration principal instead authenticates through the default scheme, which the composition
    /// root turns into a forwarding policy scheme when a migration token is configured.
    /// </remarks>
    public static IServiceCollection AddCompassAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // The Compass root — its own role string only.
            options.AddPolicy(RolePolicy.CompassSuperAdmin, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassSuperAdminRole)));

            // The remaining three accept their own role OR the Compass root. No timesheet role.
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
            // Never attach this to a write route: Admin and Sales hold no write rights anywhere in
            // Compass (AC-44), so doing so would be the same escalation CompassAdmin would be there.
            options.AddPolicy(RolePolicy.CompassElevated, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassAdminRole,
                    RolePolicy.CompassOpsRole,
                    RolePolicy.CompassSalesRole,
                    RolePolicy.CompassSuperAdminRole)));

            // The reporting audience: Sales OR Ops OR the root. Compass role strings only — adding
            // Compass Admin, or any timesheet or OOTO role, reverses an owner decision (FR-018,
            // FR-019). Deliberately one role narrower than CompassElevated above, which admits
            // CompassAdminRole: FR-019/AC-44 keep Compass Admin out of the dashboard. Do not fold the
            // two together; CompassReportingPolicyTests and the E2E refusal spec both fail if you do.
            options.AddPolicy(RolePolicy.CompassReporting, p =>
                p.RequireAssertion(RoleAssertions.RequireLocalRole(
                    RolePolicy.CompassSalesRole,
                    RolePolicy.CompassOpsRole,
                    RolePolicy.CompassSuperAdminRole)));
        });

        return services;
    }
}
