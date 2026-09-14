using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Manages additive authorization role assignments with audit logging, self-removal guardrails, and TPS sync.
/// </summary>
public class UserRoleService(
    IUserRoleRepository repository,
    IAuditService auditService,
    LeapDbContext context) : IUserRoleService
{
    /// <summary>Returns the role names assigned to the given user.</summary>
    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId)
    {
        var roles = await repository.GetByEdjeIdAsync(edjeId);
        return roles.Select(r => r.Role).ToList().AsReadOnly();
    }

    /// <summary>Returns <c>true</c> if the user has the given role assigned.</summary>
    public async Task<bool> HasRoleAsync(Guid edjeId, string role) =>
        await repository.FindAsync(edjeId, role) is not null;

    /// <summary>Returns <c>true</c> if the user has any of the given roles assigned.</summary>
    public async Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles)
    {
        var userRoles = await repository.GetByEdjeIdAsync(edjeId);
        return userRoles.Any(ur => roles.Contains(ur.Role));
    }

    /// <summary>
    /// Assigns a role to a user (idempotent) with audit trail logging.
    /// </summary>
    /// <remarks>
    /// Idempotent under concurrency, not just in sequence. The
    /// <see cref="IUserRoleRepository.FindAsync"/> guard below is a check-then-act: a concurrent caller
    /// can commit the same (EdjeId, Role) between that read and the INSERT, and the unique index
    /// <c>ix_user_roles_edje_id_role</c> then rejects ours with SQLSTATE 23505. Unhandled, that
    /// surfaced as a <see cref="DbUpdateException"/> and a bare HTTP 500 out of
    /// <c>POST /api/admin/user-roles</c>. The losing writer now takes the same benign path as the
    /// "already assigned" branch.
    /// </remarks>
    public async Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason)
    {
        // Compass authority MUST derive from Google Compass groups alone (FR-010, FR-014) -- this
        // endpoint is Timesheet-owned and gated by a TIMESHEET role, so admin-assigning a Compass
        // role here would let a Timesheet SuperAdmin mint standing Compass authority for anyone,
        // bypassing Google group membership and SAML sign-in entirely.
        if (KnownRoles.Compass.Contains(role))
        {
            throw new InvalidOperationException(
                $"'{role}' is a Compass role and cannot be assigned through this endpoint.");
        }

        var existing = await repository.FindAsync(edjeId, role);
        if (existing is not null)
        {
            return; // Idempotent -- already assigned
        }

        var userRole = new UserRole
        {
            EdjeId = edjeId,
            Role = role,
            CreatedBy = actor,
            UpdatedBy = actor
        };

        try
        {
            await repository.AddAsync(userRole);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            // A concurrent request already committed this exact (EdjeId, Role). The desired state is
            // in place and this call changed nothing, so return without auditing, identical to the
            // "already assigned" branch above. Detach the rejected entity first: the context is
            // request-scoped and shared with AuditService, so a doomed INSERT left in the change
            // tracker would be replayed by the next SaveChangesAsync on this request.
            context.Entry(userRole).State = EntityState.Detached;
            return;
        }

        await auditService.LogAsync(new AuditEntry(
            EntityType: "UserRole",
            EntityId: $"{edjeId}:{role}",
            Action: "Assign",
            Actor: actor,
            TriggeredBy: "admin",
            Reason: reason,
            Changes: [new FieldChange("Role", null, role)]));
    }

    /// <summary>
    /// Removes a role from a user with audit trail logging; prevents SuperAdmin from removing their own SuperAdmin role.
    /// </summary>
    public async Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason)
    {
        // Self-removal guardrail: SuperAdmin cannot remove their own SuperAdmin role
        if (string.Equals(role, RolePolicy.SuperAdmin, StringComparison.Ordinal) &&
            string.Equals(actor, edjeId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SuperAdmin cannot remove their own SuperAdmin role.");
        }

        var existing = await repository.FindAsync(edjeId, role);
        if (existing is null)
        {
            return; // No-op -- role not assigned
        }

        await repository.RemoveAsync(existing);
        await context.SaveChangesAsync();

        await auditService.LogAsync(new AuditEntry(
            EntityType: "UserRole",
            EntityId: $"{edjeId}:{role}",
            Action: "Remove",
            Actor: actor,
            TriggeredBy: "admin",
            Reason: reason,
            Changes: [new FieldChange("Role", role, null)]));
    }

    /// <summary>Returns every <see cref="UserRole"/> row assigned the given role.</summary>
    public async Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role) =>
        await repository.GetByRoleAsync(role);

    /// <summary>
    /// Ensures all employees have the base EDJEr role, typically called during TPS sync.
    /// </summary>
    public async Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees)
    {
        var existingEdjeIds = (await repository.GetByRoleAsync(RolePolicy.EDJEr))
            .Select(r => r.EdjeId)
            .ToHashSet();

        var added = 0;
        foreach (var (edjeId, _) in employees)
        {
            if (existingEdjeIds.Contains(edjeId))
            {
                continue;
            }

            await repository.AddAsync(new UserRole
            {
                EdjeId = edjeId,
                Role = RolePolicy.EDJEr,
                CreatedBy = "tps-sync",
                UpdatedBy = "tps-sync"
            });
            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync();
        }

        return added;
    }

    /// <summary>
    /// Synchronizes a user's local roles to match a set of privileges from the IdP profile, adding missing and removing extra roles.
    /// </summary>
    /// <remarks>
    /// Concurrent invocations for the same EdjeId could otherwise lose writes through a read-then-write
    /// window: both observe the same stale baseline and one call's DELETE undoes another's fresh insert.
    /// On real Postgres this issues per-role <c>INSERT ... ON CONFLICT DO NOTHING</c> via raw
    /// <c>ExecuteSqlAsync</c> against <c>ix_user_roles_edje_id_role</c>, re-reads the current set with
    /// <see cref="IUserRoleRepository.GetByEdjeIdAsync"/> after those inserts commit, then deletes the
    /// remaining extras with parameterized per-row raw SQL, so a delete that raced an insert removes
    /// zero rows instead of rolling back unrelated work. On a non-relational provider it falls back to
    /// repository-mediated EF change tracking; the catch handlers below stay as defence in depth.
    /// </remarks>
    public async Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync")
    {
        // Compass authority is derived per session and must NEVER be persisted (BR-12/FR-013).
        // CompositeAuthorizationResolver's DB fallback means a persisted row here would be a live
        // grant, independent of the deriving session -- not inert data. The session's claims
        // (stamped by SignInService.BuildIdentity) still carry the Compass role for the lifetime
        // of the session; only the DB write is excluded here.
        var desiredRoles = privileges
            .Where(role => !KnownRoles.Compass.Contains(role))
            .ToHashSet(StringComparer.Ordinal);

        if (context.Database.IsRelational())
        {
            await SyncRelationalAsync(edjeId, desiredRoles, actor);
            return;
        }

        await SyncInMemoryAsync(edjeId, desiredRoles, actor);
    }

    private async Task SyncRelationalAsync(Guid edjeId, HashSet<string> desiredRoles, string actor)
    {
        // Step 1: idempotent INSERT ... ON CONFLICT DO NOTHING per desired role.
        // The unique index ix_user_roles_edje_id_role is the conflict target, so
        // a concurrent duplicate insert is absorbed atomically by Postgres — no
        // read-then-write race window, no error, and no rollback of prior inserts.
        var now = DateTime.UtcNow;
        foreach (var role in desiredRoles)
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO user_roles (edje_id, role, created_at, updated_at, created_by, updated_by) VALUES ({edjeId}, {role}, {now}, {now}, {actor}, {actor}) ON CONFLICT (edje_id, role) DO NOTHING");
        }

        // Step 2: re-read the CURRENT state of user_roles for this EdjeId
        // (post-insert). The repository uses AsNoTracking, so this is a fresh
        // server-side read that reflects any concurrently-committed rows.
        var currentRoles = await repository.GetByEdjeIdAsync(edjeId);

        // Step 3: targeted DELETE per row that is now extra. Each DELETE is
        // its own implicit transaction; a 0-row-affected outcome is benign
        // (a concurrent caller already removed the row).
        foreach (var extra in currentRoles.Where(r => !desiredRoles.Contains(r.Role)))
        {
            await context.Database.ExecuteSqlAsync(
                $"DELETE FROM user_roles WHERE edje_id = {edjeId} AND role = {extra.Role}");
        }
    }

    private async Task SyncInMemoryAsync(Guid edjeId, HashSet<string> desiredRoles, string actor)
    {
        var currentRoles = await repository.GetByEdjeIdAsync(edjeId);
        var currentRoleNames = currentRoles.Select(r => r.Role).ToHashSet(StringComparer.Ordinal);

        // Add missing roles. The catch handler stays as defense-in-depth in
        // case a test double simulates the duplicate-key race (see
        // ThrowOnAddUserRoleRepository in UserRoleServiceTests).
        foreach (var role in desiredRoles.Except(currentRoleNames))
        {
            try
            {
                await repository.AddAsync(new UserRole
                {
                    EdjeId = edjeId,
                    Role = role,
                    CreatedBy = actor,
                    UpdatedBy = actor
                });
            }
            catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
            {
                // Another concurrent request already inserted this role -- benign.
            }
        }

        // Remove extras only when the desired set is the authoritative view. The InMemory test fixture
        // controls timing deterministically, and
        // SyncFromProfileAsync_ConcurrentSyncesForSameEdjeId_ConvergeWithoutLoss asserts against a
        // stale-baseline delete. Re-read after inserts so a row a concurrent caller just committed is
        // not deleted.
        var afterInserts = await repository.GetByEdjeIdAsync(edjeId);
        foreach (var existing in afterInserts.Where(r => !desiredRoles.Contains(r.Role)))
        {
            await repository.RemoveAsync(existing);
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent SyncFromProfileAsync already removed the row(s) we
            // were trying to remove. Desired state is already in place.
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            // SaveChanges raced with another sync on an INSERT; the desired
            // state is already in place because the other request committed first.
        }
    }

    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        // Walk the inner-exception chain looking for a Postgres unique_violation.
        // Npgsql surfaces the underlying error as a PostgresException with
        // SqlState "23505"; EF Core wraps it in a DbUpdateException. Matching on
        // the SqlState code (not message text) is locale-independent and robust.
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg && pg.SqlState == "23505")
            {
                return true;
            }
        }
        return false;
    }
}
