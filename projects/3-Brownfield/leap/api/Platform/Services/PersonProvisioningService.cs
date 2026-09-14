using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// The single correctness-critical find-or-create core for app-minted <c>people</c> rows, shared by
/// <c>BootstrapSuperAdminSeeder</c> (startup) and <c>SignInService</c> (SAML auto-create).
/// </summary>
/// <remarks>
/// Queries <c>context.People</c> directly rather than through a directory lookup, which would return
/// <c>null</c> both for "no row exists" and for "a row exists whose EdjeId is null", including an
/// inactive row, so a caller cannot tell "create a person" from "deny a deactivated person". Reading
/// <c>People</c> here keeps deactivation irreversible and stops a blind insert violating the
/// <c>LOWER(email)</c> unique index. Queries are tracked because the active-row-with-null-EdjeId case
/// updates in place, and the service owns <c>SaveChangesAsync</c>.
/// </remarks>
public class PersonProvisioningService(
    LeapDbContext context,
    ILogger<PersonProvisioningService> logger) : IPersonProvisioningService
{
    /// <inheritdoc />
    public async Task<PersonProvisionResult> EnsurePersonAsync(string? email, string? displayName, string source)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "Person provisioning rejected: no email supplied (source {Source})",
                LogSanitizer.Clean(source));
            return PersonProvisionResult.ForRejected();
        }

        var trimmedEmail = email.Trim();
        var normalizedEmail = trimmedEmail.ToLower();

        var existing = await context.People
            .FirstOrDefaultAsync(p => p.Email != null && p.Email.ToLower() == normalizedEmail);

        if (existing is not null)
        {
            // D-06: an explicitly deactivated person is NEVER reactivated and NEVER duplicated. This
            // fires even when the row's EdjeId is null (the case the directory lookup collapses to
            // null), which is precisely why the deny decision lives here and not at the call site.
            if (!existing.IsActive)
            {
                logger.LogWarning(
                    "Person provisioning denied for {Email}: the existing person row is deactivated; deactivation is never undone",
                    LogSanitizer.Clean(trimmedEmail));
                return PersonProvisionResult.ForInactive(trimmedEmail);
            }

            if (existing.EdjeId is null)
            {
                // Mint in place using the row's own Id — the same fallback TpsImportService.MintEdjeIds
                // applies for a person with no employee record. Updating beats inserting: a second row
                // would be rejected by the LOWER(email) unique index anyway.
                existing.EdjeId = existing.Id;
                await context.SaveChangesAsync();
                logger.LogInformation(
                    "Minted EdjeId for existing person {Email} (row had a null EdjeId)",
                    LogSanitizer.Clean(trimmedEmail));
            }

            return PersonProvisionResult.ForExisting(
                existing.EdjeId.Value,
                existing.Email ?? trimmedEmail,
                ResolveDisplayName(existing.FirstName, existing.LastName, trimmedEmail));
        }

        var (firstName, lastName) = SplitDisplayName(displayName);
        var id = Guid.CreateVersion7();
        var person = new Person
        {
            Id = id,
            // Every session must carry a REAL EdjeId (ICurrentUserContext.EdjeId throws on a missing
            // claim, which would 500 /api/me), so mint eagerly from the row's own Id.
            EdjeId = id,
            Email = trimmedEmail,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            Source = source,
        };

        context.People.Add(person);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Created person row for {Email} with provenance source {Source}",
            LogSanitizer.Clean(trimmedEmail),
            LogSanitizer.Clean(source));

        return PersonProvisionResult.ForCreated(
            id, trimmedEmail, ResolveDisplayName(firstName, lastName, trimmedEmail));
    }

    /// <inheritdoc />
    public async Task<string> EnsureDisplayNameAsync(
        Guid edjeId, string? assertionName, string resolvedDisplayName)
    {
        var trimmedAssertionName = assertionName?.Trim();

        // Tracked (no AsNoTracking): the row is updated in place when a name is missing or provisional.
        var person = await context.People.FirstOrDefaultAsync(p => p.EdjeId == edjeId);
        if (person is null)
        {
            // No row to reconcile against, so there is nothing to persist and nothing better to say
            // than what the caller already resolved from the directory. Returning the caller's value
            // (rather than preferring the assertion) keeps this a strict no-op for any identity source
            // that is not backed by a `people` row — which is exactly the shape the SAML integration
            // tests use, where the directory is a stub.
            return FirstNonBlank(resolvedDisplayName, trimmedAssertionName);
        }

        var storedName = ResolveStoredName(person);

        // D-06: a deactivated row is inert. Sign-in denies these anyway; never write to one.
        if (!person.IsActive || string.IsNullOrWhiteSpace(trimmedAssertionName))
        {
            return FirstNonBlank(storedName, trimmedAssertionName, resolvedDisplayName);
        }

        // Filling a BLANK name is additive and allowed on any row. Replacing an EXISTING name is only
        // allowed when the app minted it (non-null Source) — a bootstrap-seeded name can be an
        // email-derived placeholder, and the assertion is authoritative. An HR-owned row is left alone.
        var mayWrite = string.IsNullOrWhiteSpace(storedName)
            || (person.Source is not null && !string.Equals(storedName, trimmedAssertionName, StringComparison.Ordinal));

        if (!mayWrite)
        {
            return FirstNonBlank(storedName, resolvedDisplayName);
        }

        var (firstName, lastName) = SplitDisplayName(trimmedAssertionName);
        person.FirstName = firstName;
        person.LastName = lastName;
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Persisted display name from the sign-in assertion for person {EdjeId} (row provenance {Source})",
            edjeId,
            LogSanitizer.Clean(person.Source ?? "directory"));

        return FirstNonBlank(ResolveStoredName(person), trimmedAssertionName, resolvedDisplayName);
    }

    /// <inheritdoc />
    public async Task TryFillMissingDisplayNameAsync(Guid edjeId, string? candidateName)
    {
        var trimmedCandidate = candidateName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedCandidate))
        {
            return;
        }

        var person = await context.People.FirstOrDefaultAsync(p => p.EdjeId == edjeId);

        // Fill ONLY a blank name on an ACTIVE row (D-06 keeps deactivated rows inert). Never replace:
        // the candidate is an email-derived guess and this runs on every pod start.
        if (person is null || !person.IsActive || !string.IsNullOrWhiteSpace(ResolveStoredName(person)))
        {
            return;
        }

        var (firstName, lastName) = SplitDisplayName(trimmedCandidate);
        person.FirstName = firstName;
        person.LastName = lastName;
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Backfilled a derived display name onto person {EdjeId}, which had none",
            edjeId);
    }

    private static string ResolveStoredName(Person person) =>
        $"{person.FirstName} {person.LastName}".Trim();

    private static string FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

    // "Ada Lovelace" -> ("Ada", "Lovelace"); "Ada" -> ("Ada", null); "Ada B. Lovelace" -> ("Ada",
    // "B. Lovelace"). Blank -> (null, null): identity only (D-08), never a fabricated name.
    private static (string? FirstName, string? LastName) SplitDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (null, null);
        }

        var parts = displayName.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1])
            ? (parts[0], null)
            : (parts[0], parts[1]);
    }

    // Same fallback chain EmployeeDirectoryService uses so callers can always stamp a non-empty
    // display-name claim even for a row that carries identity only.
    private static string ResolveDisplayName(string? firstName, string? lastName, string email)
    {
        var displayName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName) ? email : displayName;
    }
}
