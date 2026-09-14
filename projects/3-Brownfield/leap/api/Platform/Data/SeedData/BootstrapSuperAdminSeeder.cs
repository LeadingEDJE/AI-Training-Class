using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Data.SeedData;

/// <summary>
/// Breaks the deployed first-sign-in deadlock: for every email in <c>Bootstrap:SuperAdmins</c> it
/// ensures an active <c>people</c> row and grants it <see cref="RolePolicy.SuperAdmin"/>.
/// </summary>
/// <remarks>
/// A seeder rather than an EF migration, deliberately: <c>Up(MigrationBuilder)</c> has no
/// <see cref="IOptions{T}"/> access so it cannot express a per-environment list, migrations are
/// immutable history so later admin changes would tangle people data into schema history, and
/// migrations are scoped to schema. It is idempotent and runs on every pod start: person lookup goes
/// through <see cref="IPersonProvisioningService"/> and the grant through
/// <see cref="IUserRoleService.AssignRoleAsync"/>, which writes the explaining audit entry. An
/// empty or unset list is a complete no-op with no logging. No raw SQL, so it also runs on the
/// InMemory provider.
/// </remarks>
public class BootstrapSuperAdminSeeder(
    IPersonProvisioningService personProvisioning,
    IUserRoleService userRoleService,
    IOptions<BootstrapAdminOptions> options,
    ILogger<BootstrapSuperAdminSeeder> logger)
{
    /// <summary>Actor recorded on the audit entry <see cref="IUserRoleService.AssignRoleAsync"/> writes.</summary>
    private const string Actor = "bootstrap-superadmin-seeder";

    /// <summary>Reason recorded on the audit entry, naming the configuration surface responsible.</summary>
    private const string Reason = "Bootstrap SuperAdmin listed in Bootstrap:SuperAdmins configuration";

    private readonly BootstrapAdminOptions _options = options.Value;

    /// <summary>
    /// Provisions a person row and a <c>SuperAdmin</c> grant for every configured email.
    /// </summary>
    /// <remarks>
    /// Blank entries and case-only duplicates collapse to one, a deactivated person is skipped with a
    /// warning and never reactivated, and each email is processed independently so one bad entry
    /// cannot abort the bootstrap for the rest.
    /// </remarks>
    public async Task SeedAsync()
    {
        var configured = _options.SuperAdmins;
        if (configured is null || configured.Length == 0)
        {
            // Unset = off. Stay silent: local dev and CI must look exactly as they did before.
            return;
        }

        var emails = configured
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var email in emails)
        {
            try
            {
                await SeedOneAsync(email);
            }
            catch (Exception ex)
            {
                // One malformed/conflicting entry must not deny access to every other bootstrap admin.
                logger.LogError(
                    ex,
                    "Bootstrap SuperAdmin seeding failed for {Email}; continuing with the remaining entries",
                    LogSanitizer.Clean(email));
            }
        }
    }

    private async Task SeedOneAsync(string email)
    {
        // A name derived from the email local part rather than null, so Admin -> People does not list
        // every configured admin blank until they first sign in. EmailDerivedName returns null for
        // anything it cannot read as a person's name, so an ambiguous address stays identity-only
        // rather than acquiring a fictional one, and because the row carries a non-null provenance
        // Source, EnsureDisplayNameAsync later replaces this placeholder with the assertion name.
        var derivedName = EmailDerivedName.TryDerive(email);
        var result = await personProvisioning.EnsurePersonAsync(
            email, derivedName, PersonSource.BootstrapSeed);

        switch (result.Outcome)
        {
            case PersonProvisionOutcome.Created:
            case PersonProvisionOutcome.Existing:
                // EnsurePersonAsync only writes names on insert, so an admin whose row predates this
                // derivation would stay blank forever. The backfill covers that; it fills only a blank
                // name, so a real assertion name already on the row survives every later pod start.
                await personProvisioning.TryFillMissingDisplayNameAsync(result.EdjeId, derivedName);
                await userRoleService.AssignRoleAsync(result.EdjeId, RolePolicy.SuperAdmin, Actor, Reason);
                logger.LogInformation(
                    "Bootstrap SuperAdmin ensured for {Email} ({Outcome} person row)",
                    LogSanitizer.Clean(email),
                    LogSanitizer.Clean(result.Outcome.ToString()));
                break;

            case PersonProvisionOutcome.Inactive:
                // Configuration does not re-admit a deliberately deactivated person.
                logger.LogWarning(
                    "Bootstrap SuperAdmin skipped for {Email}: the person row is deactivated; no role granted and no reactivation performed",
                    LogSanitizer.Clean(email));
                break;

            default:
                logger.LogWarning(
                    "Bootstrap SuperAdmin skipped for {Email}: the configured entry could not be provisioned",
                    LogSanitizer.Clean(email));
                break;
        }
    }
}
