
namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// The Compass directory boundary — one logical contract, two transports.
/// </summary>
/// <remarks>
/// A consumer reads directory data through this interface rather than the database context, and the
/// versioned HTTP endpoint returns the same <see cref="CompassEmployeeDto"/> it does. Three methods have
/// no HTTP twin, and <c>CompassTransportParityTests</c> fails on a contract method with neither a handler
/// nor an allowlist entry saying why. Every method traces to one of AC-NFR-1's seven read families;
/// nothing is speculative, and the frozen legacy directory tables are never read or used as a fallback.
///
/// The caller's tier is resolved via <c>ICurrentUserContext.Privileges</c> only after the record is
/// found, so a caller exercising only the not-found path can look safe and is not: outside an HTTP
/// request, supply one tolerating that absence — <c>tests/integration/Support/NoHttpContextCurrentUser.cs</c>.
/// </remarks>
public interface IDirectory
{
    /// <summary>
    /// Returns the directory record for the given employee identifier, or <c>null</c> when no such
    /// employee exists.
    /// </summary>
    /// <param name="employeeId">
    /// The Compass employee's identifier — <c>compass.employee.employee_id</c>, an <c>int</c>.
    /// This is the platform-wide reference form for a Compass record (owner decision D-2): a module
    /// holding a directory reference stores this integer and resolves it here.
    /// <para>
    /// It is sequential and therefore enumerable, which matters because this same contract is
    /// exposed over HTTP. Authorization is enforced per record on both transports and MUST NOT lean
    /// on identifiers being unguessable.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CompassEmployeeDto?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the directory record for the employee with the given email address, or <c>null</c>
    /// when no such employee exists.
    /// </summary>
    /// <remarks>
    /// A pre-Compass consumer holds a legacy <c>Guid</c> while this contract answers on <c>employee_id</c>;
    /// email is the one attribute both hold (FR-8.4). It claims nothing about the caller's own identity.
    ///
    /// Matched on <c>lower(btrim(email))</c> both sides, as the functional unique index
    /// <c>ux_employee_email_ci</c> enforces: stricter would make an existing record read as missing,
    /// looser could match two, and a blank key fails closed rather than acting as a wildcard. No HTTP
    /// twin (recorded deviation D-3): an address in a URL is personal data in every access, proxy and
    /// referrer log, there is no out-of-monolith consumer while OAuth2 client-credentials (ADR-005) is
    /// unbuilt, and publishing later is additive while publishing now could not be withdrawn.
    /// </remarks>
    /// <param name="email">
    /// The candidate address. Neither trimmed nor cased by the caller — normalisation belongs to the
    /// match, so every caller cannot get it subtly different.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CompassEmployeeDto?> GetEmployeeByEmailAsync(
        string? email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the directory record for each of <paramref name="emails"/> that resolves to an
    /// employee. An email resolving to nothing is absent from the result, never a null entry.
    /// </summary>
    /// <remarks>
    /// The read a consumer resolving a list must use. Calling <see cref="GetEmployeeByEmailAsync"/>
    /// per row turns one directory read into one per employee — around a hundred round trips for
    /// OOTO's directory, each carrying two <c>Include</c>s and a viewer-tier resolution.
    ///
    /// Absent rather than null, and the distinction is load-bearing: a consumer has to tell "this
    /// person has no Compass record" apart from "this person's record says nothing", and a null
    /// placeholder would collapse the two. Duplicate spellings of one address collapse to one entry,
    /// since the match key is <c>lower(btrim(...))</c>; an empty or all-blank collection returns an
    /// empty list without a query. Same no-HTTP-twin note as <see cref="GetEmployeeByEmailAsync"/>.
    /// </remarks>
    /// <param name="emails">The candidate addresses, in any casing, trimmed or not.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassEmployeeDto>> GetEmployeesByEmailAsync(
        IReadOnlyCollection<string?> emails,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every ACTIVE Compass employee, ordered by family then given name.
    /// </summary>
    /// <remarks>
    /// The unpaged directory list OOTO walks in-process for its accessible-employee and report reads,
    /// once it owns those reads from Compass rather than the frozen legacy tables. Active only, matching
    /// TPS's <c>Db.ActiveEmployees()</c>; the caller narrows by scope and status after. Same
    /// no-HTTP-twin note as <see cref="GetEmployeesByEmailAsync"/> — the only consumer resolves the
    /// whole directory inside the process, and an unpaged read of every employee does not belong on the
    /// published contract.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassEmployeeDto>> GetEmployeesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns every active invoice frequency type (FR-010). Filtering happens in the repository's
    /// query, which is also why <see cref="CompassInvoiceFrequencyDto"/> publishes no <c>IsActive</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassInvoiceFrequencyDto>> GetInvoiceFrequenciesAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the client with the given identifier, or <c>null</c> when no such client exists
    /// (FR-005).
    /// </summary>
    /// <remarks>
    /// <see cref="CompassClientDto.Status"/> is derived at read time from the client's assignments —
    /// never stored, never null — and a client with zero assignments is Inactive and still fully
    /// returnable (FR-034). There is no tier gating on this payload.
    /// </remarks>
    /// <param name="clientId">The Compass client's identifier — <c>compass.client.client_id</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CompassClientDto?> GetClientAsync(int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every client. No not-found case and no paging: the collection-read shape
    /// <see cref="GetInvoiceFrequenciesAsync"/> established, so an empty store answers an empty list.
    /// </summary>
    /// <remarks>
    /// A client picker has no id to start from, so <see cref="GetClientAsync"/> cannot answer it. No HTTP
    /// route, deliberately: its one consumer is in-process and <c>/api/compass/v1</c> is a versioned
    /// contract gated by <c>scripts/check-openapi-contract.sh</c>. The exception sits in
    /// <c>CompassTransportParityTests</c>' allowlist — if a consumer over the wire ever appears, add the
    /// route and DELETE the entry; that test also fails on an allowlisted method that has a handler.
    ///
    /// Unfiltered, Inactive and Former clients included: status gates nothing (FR-034), and filtering here
    /// would make a status value mean "not selectable". No legacy identifier is published (ADR-010): a
    /// <c>Guid</c>-based consumer derives one from the <c>int</c> id, as a native client has no TPS id.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassClientDto>> GetClientsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns every billable time category for the given client — active and inactive, each carrying
    /// its own <see cref="CompassBillableCategoryDto.IsActive"/> flag (FR-009).
    /// </summary>
    /// <remarks>
    /// <see cref="CompassBillableCategoryDto"/>'s remarks explain why this is the opposite filtering
    /// policy from <see cref="GetInvoiceFrequenciesAsync"/>. There is no not-found case: an unknown
    /// <paramref name="clientId"/> simply has zero categories, so this returns an empty list rather
    /// than <c>null</c> — a client's existence is not being asserted by this read, only its
    /// categories.
    /// </remarks>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassBillableCategoryDto>> GetBillableCategoriesAsync(
        int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the assignment with the given identifier, or <c>null</c> when no such assignment
    /// exists (FR-006).
    /// </summary>
    /// <remarks>
    /// <see cref="CompassAssignmentDto.EffectiveInvoiceFrequency"/> is derived here from the assignment's
    /// own override and the client's default (FR-012) — never stored, never a third value. This is the
    /// same shape <see cref="GetClientAsync"/> uses for its own derived <c>Status</c>: a repository that
    /// returns the raw entity graph, and a service that resolves the derived value at read time.
    /// </remarks>
    /// <param name="assignmentId">
    /// The assignment's identifier — <c>compass.client_assignment.client_assignment_id</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CompassAssignmentDto?> GetAssignmentAsync(int assignmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every assignment held by the given employee (FR-006). No not-found case: an unknown or
    /// assignment-less employee simply has zero assignments, matching the collection-GET precedent.
    /// </summary>
    /// <param name="employeeId">The employee's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassAssignmentDto>> GetAssignmentsByEmployeeAsync(
        int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every assignment at the given client (FR-006). No not-found case: an unknown or
    /// assignment-less client simply has zero assignments.
    /// </summary>
    /// <param name="clientId">The client's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassAssignmentDto>> GetAssignmentsByClientAsync(
        int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every SOW under the given assignment (FR-007). No not-found case: an unknown or
    /// SOW-less assignment simply has zero SOWs, matching the collection-GET precedent.
    /// </summary>
    /// <remarks>
    /// <see cref="CompassDirectorySowDto.RateIncrease"/> and <see cref="CompassDirectorySowDto.Note"/>
    /// are tier-gated exactly like <see cref="GetEmployeeAsync"/>'s <c>TimeTracking</c> gating — the
    /// caller's tier is resolved via <c>ICurrentUserContext.Privileges</c>, same as every other tiered
    /// read on this boundary.
    /// </remarks>
    /// <param name="clientAssignmentId">The owning assignment's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompassDirectorySowDto>> GetSowsByAssignmentAsync(
        int clientAssignmentId, CancellationToken cancellationToken);
}
