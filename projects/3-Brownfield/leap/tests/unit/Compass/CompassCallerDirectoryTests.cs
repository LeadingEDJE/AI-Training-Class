using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Unit tests for the Compass implementation of the Platform-owned caller-resolution port. Uses an
/// in-memory test double for the repository rather than a mocking framework, per the project's test
/// rules.
/// </summary>
/// <remarks>
/// <para>
/// The double is modelled on the one inside <see cref="CompassDirectoryServiceTests"/> and keeps both
/// of that one's load-bearing properties: <c>EmailLookupCount</c>, so "did this even touch the store"
/// is assertable rather than assumed, and a <c>MatchKey</c> duplicated from the production rule
/// rather than shared with it, so a case/whitespace test is an assertion and not a tautology.
/// </para>
/// <para>
/// Everything this port does NOT need throws from the double. The port is specified to reach exactly
/// one repository operation, so a change that reached a second one should fail loudly here rather
/// than pass against a convenient empty list.
/// </para>
/// </remarks>
public class CompassCallerDirectoryTests
{
    private static readonly Guid CallerEdjeId = new("0000042c-0000-0000-0000-00000000042c");

    private sealed class InMemoryCompassDirectoryRepository : ICompassDirectoryRepository
    {
        private readonly List<Employee> _employees = [];

        public void Seed(Employee employee) => _employees.Add(employee);

        /// <summary>How many times the email-keyed read was CALLED on this double.</summary>
        /// <remarks>
        /// <para>
        /// Exists so the blank-email case is assertable rather than assumed. An implementation that
        /// asked the store for nothing would satisfy a value-only assertion and still be wrong: the
        /// contract is that a caller with no address never reaches the directory at all.
        /// </para>
        /// <para>
        /// Counted on ENTRY, which differs from the sibling double in
        /// <see cref="CompassDirectoryServiceTests"/> — deliberately, and it was measured. That
        /// one increments only once the key survives normalisation, so it counts QUERIES. Under that
        /// semantics this assertion is vacuous here: the real repository ALSO fails closed on a blank
        /// key, so deleting the service's own guard leaves the count at zero and the test still
        /// passes. Verified by perturbation — with the guard removed, an entry-counted double goes
        /// red and a query-counted one does not. The subject here is the SERVICE's guard, so the
        /// thing to count is the call.
        /// </para>
        /// </remarks>
        public int EmailLookupCount { get; private set; }

        public Task<Employee?> GetEmployeeByEmailAsync(
            string? email, CancellationToken cancellationToken)
        {
            EmailLookupCount++;

            var key = MatchKey(email);
            if (key is null)
            {
                return Task.FromResult<Employee?>(null);
            }

            return Task.FromResult(_employees.FirstOrDefault(e => MatchKey(e.Email) == key));
        }

        /// <summary>
        /// The same normalisation the real repository applies — <c>lower(btrim(...))</c> on both
        /// sides, blank meaning "nothing to look up".
        /// </summary>
        /// <remarks>
        /// Duplicated rather than shared with production, for the reason
        /// <see cref="CompassDirectoryServiceTests"/>'s copy records: a double that reused the
        /// production helper would agree with it by construction and could not detect the case this
        /// port turns on — matching a stored value that carries stray whitespace or unexpected
        /// casing.
        /// </remarks>
        private static string? MatchKey(string? email)
            => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        public Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<Employee>> GetEmployeesByEmailAsync(
            IReadOnlyCollection<string?> emails, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<InvoiceFrequencyType>> GetInvoiceFrequenciesAsync(
            CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<(Client Client, string Status)?> GetClientAsync(
            int clientId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<(Client Client, string Status)>> GetClientsAsync(
            CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<BillableTimeCategory>> GetBillableCategoriesAsync(
            int clientId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<ClientAssignment?> GetAssignmentAsync(
            int assignmentId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByEmployeeAsync(
            int employeeId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByClientAsync(
            int clientId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        public Task<IReadOnlyList<Sow>> GetSowsByAssignmentAsync(
            int clientAssignmentId, CancellationToken cancellationToken)
            => throw new NotSupportedException(Unreached);

        private const string Unreached =
            "caller resolution reaches exactly one repository operation — the email-keyed read";
    }

    /// <summary>An authenticated caller carrying the given email claim.</summary>
    /// <remarks>
    /// Holds no privilege, deliberately. This port projects no tier-gated member and must not consult
    /// <see cref="ICurrentUserContext.Privileges"/> at all; a stub that granted something would hide
    /// an implementation that did.
    /// </remarks>
    private sealed class StubCurrentUser(string email) : ICurrentUserContext
    {
        public Guid EdjeId => CallerEdjeId;

        public string Email => email;

        public string TpsEmployeeId => throw new NotSupportedException(Unreached);

        public IReadOnlyList<string> Privileges => throw new NotSupportedException(Unreached);

        public bool HasPrivilege(string privilege) => throw new NotSupportedException(Unreached);

        private const string Unreached =
            "caller resolution reads Email (and, on the blank branch, EdjeId) and nothing else";
    }

    /// <summary>
    /// Reproduces the REAL <c>CurrentUserContext</c>'s behaviour outside an HTTP request: every
    /// member throws, because the real implementation reads <c>HttpContext.User</c> and there is
    /// none.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT named <c>NoHttpContextCurrentUser</c>, for the reason
    /// <see cref="CompassDirectoryServiceTests"/> already records: the integration project owns that
    /// name for the OPPOSITE behaviour — the safe double a bare-DI-scope caller should use.
    /// </remarks>
    private sealed class ThrowingCurrentUserContext : ICurrentUserContext
    {
        public Guid EdjeId => throw new InvalidOperationException("No HTTP context");
        public string Email => throw new InvalidOperationException("No HTTP context");
        public string TpsEmployeeId => throw new InvalidOperationException("No HTTP context");
        public IReadOnlyList<string> Privileges => throw new InvalidOperationException("No HTTP context");
        public bool HasPrivilege(string privilege) => throw new InvalidOperationException("No HTTP context");
    }

    /// <summary>Minimal logger capturing level + rendered message so log assertions are possible.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static Employee BuildEmployee(
        int id, string first, string last, string email, bool isActive = true)
        => new()
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Email = email,
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = 1,
            IsActive = isActive,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };

    private static CompassCallerDirectory CreateSubject(
        ICompassDirectoryRepository repository,
        ICurrentUserContext currentUser,
        ILogger<CompassCallerDirectory>? logger = null)
        => new(repository, currentUser, logger ?? new CapturingLogger<CompassCallerDirectory>());

    [Fact]
    public async Task ResolveCallerAsync_WhenTheCallerHasACompassRecord_ResolvesAllThreeMembers()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(7, "Ada", "Lovelace", "ada.lovelace@example.test"));
        var subject = CreateSubject(repository, new StubCurrentUser("ada.lovelace@example.test"));

        // Act
        var result = await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.DirectoryId.ShouldBe(7);
        result.DisplayName.ShouldBe("Ada Lovelace");
        result.Email.ShouldBe("ada.lovelace@example.test");
    }

    /// <summary>
    /// The stored address is what comes back, not the claim — so a caller signing in with a
    /// differently-cased address does not silently change the value a consumer correlates on.
    /// </summary>
    [Theory]
    [InlineData("ADA.LOVELACE@EXAMPLE.TEST")]
    [InlineData("  ada.lovelace@example.test  ")]
    [InlineData("\tAda.Lovelace@Example.Test\n")]
    public async Task ResolveCallerAsync_WhenTheEmailClaimDiffersInCaseOrWhitespace_StillResolves(
        string claimValue)
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(7, "Ada", "Lovelace", "ada.lovelace@example.test"));
        var subject = CreateSubject(repository, new StubCurrentUser(claimValue));

        // Act
        var result = await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.DirectoryId.ShouldBe(7);
        result.Email.ShouldBe("ada.lovelace@example.test");
    }

    [Fact]
    public async Task ResolveCallerAsync_WhenNoCompassRecordMatches_ReturnsNull()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(7, "Ada", "Lovelace", "ada.lovelace@example.test"));
        var subject = CreateSubject(repository, new StubCurrentUser("grace.hopper@example.test"));

        // Act
        var result = await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert — a normal outcome, not an error: not every signed-in caller is a Compass EDJEr
        result.ShouldBeNull();
    }

    /// <summary>
    /// A blank email claim never becomes a directory query. The value assertion alone would pass on
    /// an implementation that asked the store for nothing, so the call count is the real assertion.
    /// </summary>
    /// <remarks>
    /// Genuinely reachable, not defensive: <c>CurrentUserContext.Email</c> answers
    /// <c>string.Empty</c> when the claim is missing, and the migration principal mints no email
    /// claim at all.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n  ")]
    public async Task ResolveCallerAsync_WhenTheEmailClaimIsBlank_ReturnsNullWithoutTouchingTheStore(
        string claimValue)
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(7, "Ada", "Lovelace", "ada.lovelace@example.test"));
        var logger = new CapturingLogger<CompassCallerDirectory>();
        var subject = CreateSubject(repository, new StubCurrentUser(claimValue), logger);

        // Act
        var result = await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
        repository.EmailLookupCount.ShouldBe(0);
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    /// <summary>
    /// The warning identifies the caller by EDJE identity and never by address — the address is the
    /// caller-supplied value, and this branch exists precisely because it is untrustworthy.
    /// </summary>
    [Fact]
    public async Task ResolveCallerAsync_WhenTheEmailClaimIsBlank_LogsTheEdjeIdAndNotTheAddress()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var logger = new CapturingLogger<CompassCallerDirectory>();
        var subject = CreateSubject(repository, new StubCurrentUser("   "), logger);

        // Act
        await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert
        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(CallerEdjeId.ToString());
    }

    /// <summary>
    /// An inactive EDJEr still resolves. There is no <c>IsActive</c> filter here, matching the
    /// resolver this port replaces; adding one would deny a caller their own identity, which is a
    /// separate decision and out of scope.
    /// </summary>
    [Fact]
    public async Task ResolveCallerAsync_WhenTheMatchedRecordIsInactive_ResolvesItAnyway()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(
            11, "Grace", "Hopper", "grace.hopper@example.test", isActive: false));
        var subject = CreateSubject(repository, new StubCurrentUser("grace.hopper@example.test"));

        // Act
        var result = await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.DirectoryId.ShouldBe(11);
    }

    /// <summary>
    /// The display name comes from the module's own rule, not a second copy of it.
    /// </summary>
    /// <remarks>
    /// Seeded with a blank given name on purpose. A record with both parts populated would agree
    /// with a hand-rolled <c>first + " " + last</c>, so it could not tell the two apart; a blank part
    /// is exactly where they diverge, because the shared rule omits it rather than leaving a dangling
    /// separator.
    /// </remarks>
    [Fact]
    public async Task ResolveCallerAsync_DisplayName_ComesFromTheModulesSharedNameRule()
    {
        // Arrange
        var employee = BuildEmployee(13, string.Empty, "Hamilton", "hamilton@example.test");
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(employee);
        var subject = CreateSubject(repository, new StubCurrentUser("hamilton@example.test"));

        // Act
        var result = await subject.ResolveCallerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.DisplayName.ShouldBe(CompassDisplayName.For(employee));
        result.DisplayName.ShouldBe("Hamilton");
    }

    /// <summary>
    /// With no HTTP context there is no caller, and that must never be reported as "no match".
    /// The exception propagates; catching it is the defect this test exists to prevent.
    /// </summary>
    [Fact]
    public async Task ResolveCallerAsync_WhenThereIsNoHttpContext_LetsTheExceptionPropagate()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var subject = CreateSubject(repository, new ThrowingCurrentUserContext());

        // Act / Assert
        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => subject.ResolveCallerAsync(TestContext.Current.CancellationToken));

        thrown.Message.ShouldBe("No HTTP context");
        repository.EmailLookupCount.ShouldBe(0);
    }
}
