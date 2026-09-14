using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Unit tests for the Compass directory boundary. Uses an in-memory test double for the repository
/// rather than a mocking framework, per the project's test rules.
/// </summary>
/// <remarks>
/// The double and the builder now produce Compass's own <see cref="Employee"/>. They previously built
/// a Timesheet <c>Employee</c> with a nested <c>Person</c>, because the boundary resolved against
/// <c>public.employees</c>; feature 003 re-pointed it at <c>compass.employee</c>, which carries both
/// name parts directly.
/// </remarks>
public class CompassDirectoryServiceTests
{
    private sealed class InMemoryCompassDirectoryRepository : ICompassDirectoryRepository
    {
        private readonly List<Employee> _employees = [];
        private readonly List<InvoiceFrequencyType> _invoiceFrequencies = [];
        private readonly Dictionary<int, (Client Client, string Status)> _clients = [];
        private readonly List<BillableTimeCategory> _billableCategories = [];
        private readonly List<ClientAssignment> _assignments = [];
        private readonly List<Sow> _sows = [];

        public void Seed(Employee employee) => _employees.Add(employee);

        public void Seed(InvoiceFrequencyType invoiceFrequencyType)
            => _invoiceFrequencies.Add(invoiceFrequencyType);

        /// <summary>
        /// Seeds a client with its status ALREADY decided by the caller — this double stands in for
        /// the repository's own real derivation, so the service tests it backs only prove the
        /// projection, not the derivation itself (that is <c>ClientStatusDerivationTests</c>' job).
        /// </summary>
        public void Seed(Client client, string status) => _clients[client.Id] = (client, status);

        public void Seed(BillableTimeCategory category) => _billableCategories.Add(category);

        public void Seed(ClientAssignment assignment) => _assignments.Add(assignment);

        public void Seed(Sow sow) => _sows.Add(sow);

        public Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
            => Task.FromResult(_employees.FirstOrDefault(e => e.Id == employeeId));

        public Task<IReadOnlyList<(Client Client, string Status)>> GetClientsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(Client Client, string Status)>>(
                [.. _clients.Values.OrderBy(c => c.Client.ClientName, StringComparer.Ordinal)]);

        /// <summary>How many times an email-keyed read reached this double.</summary>
        /// <remarks>
        /// Exists so contract case C-4 — an empty email collection must not touch the store — is
        /// assertable rather than assumed. A repository that returned an empty list by querying for
        /// nothing would satisfy the value assertion and still be wrong.
        /// </remarks>
        public int EmailLookupCount { get; private set; }

        public Task<Employee?> GetEmployeeByEmailAsync(
            string? email, CancellationToken cancellationToken)
        {
            var key = MatchKey(email);
            if (key is null)
            {
                return Task.FromResult<Employee?>(null);
            }

            EmailLookupCount++;
            return Task.FromResult(_employees.FirstOrDefault(e => MatchKey(e.Email) == key));
        }

        public Task<IReadOnlyList<Employee>> GetEmployeesByEmailAsync(
            IReadOnlyCollection<string?> emails, CancellationToken cancellationToken)
        {
            var keys = emails.Select(MatchKey).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            if (keys.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<Employee>>([]);
            }

            EmailLookupCount++;
            return Task.FromResult<IReadOnlyList<Employee>>(
                [.. _employees.Where(e => MatchKey(e.Email) is { } key && keys.Contains(key))]);
        }

        public Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Employee>>(
                [.. _employees.Where(e => e.IsActive).OrderBy(e => e.LastName).ThenBy(e => e.FirstName)]);

        /// <summary>
        /// The same normalisation the real repository applies — <c>lower(btrim(...))</c> on both
        /// sides, blank meaning "nothing to look up".
        /// </summary>
        /// <remarks>
        /// Duplicated here rather than shared, on purpose. A double that reused the production
        /// helper would agree with it by construction and so could not detect the case this
        /// boundary's contract actually turns on: matching a stored value that carries stray
        /// whitespace or unexpected casing. Two independent expressions of one rule is what makes
        /// the test an assertion rather than a tautology.
        /// </remarks>
        private static string? MatchKey(string? email)
            => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        public Task<IReadOnlyList<InvoiceFrequencyType>> GetInvoiceFrequenciesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InvoiceFrequencyType>>(
                [.. _invoiceFrequencies.Where(t => t.IsActive).OrderBy(t => t.TypeName)]);

        public Task<(Client Client, string Status)?> GetClientAsync(
            int clientId, CancellationToken cancellationToken)
            => Task.FromResult(_clients.TryGetValue(clientId, out var found)
                ? found
                : ((Client Client, string Status)?)null);

        public Task<IReadOnlyList<BillableTimeCategory>> GetBillableCategoriesAsync(
            int clientId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BillableTimeCategory>>(
                [.. _billableCategories.Where(c => c.ClientId == clientId).OrderBy(c => c.CategoryName)]);

        public Task<ClientAssignment?> GetAssignmentAsync(
            int assignmentId, CancellationToken cancellationToken)
            => Task.FromResult(_assignments.FirstOrDefault(a => a.Id == assignmentId));

        public Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByEmployeeAsync(
            int employeeId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ClientAssignment>>(
                [.. _assignments.Where(a => a.EmployeeId == employeeId).OrderBy(a => a.StartDate)]);

        public Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByClientAsync(
            int clientId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ClientAssignment>>(
                [.. _assignments.Where(a => a.ClientId == clientId).OrderBy(a => a.StartDate)]);

        public Task<IReadOnlyList<Sow>> GetSowsByAssignmentAsync(
            int clientAssignmentId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Sow>>(
                [.. _sows.Where(s => s.ClientAssignmentId == clientAssignmentId).OrderBy(s => s.SowStartDate)]);
    }

    /// <summary>An authenticated caller with no Compass role — Compass tier Baseline.</summary>
    /// <remarks>
    /// These existing tests only ever assert the three original members, none of which is tier-gated,
    /// so Baseline is the right default: it exercises the withholding path (Slice 1) rather than
    /// masking it the way a Super Admin stub would.
    /// </remarks>
    private sealed class StubCurrentUser : ICurrentUserContext
    {
        public Guid EdjeId => Guid.Empty;
        public string Email => "baseline@example.test";
        public string TpsEmployeeId => string.Empty;
        public IReadOnlyList<string> Privileges => [];
        public bool HasPrivilege(string privilege) => false;
    }

    /// <summary>A caller holding the Compass root role — Compass tier SuperAdmin.</summary>
    /// <remarks>
    /// Only needed where a test has to prove a tier-gated member is PRESENT for someone. A gate test
    /// that exercises the withholding side alone also passes against a projection that never populates
    /// the member at all, so both sides need a caller.
    /// </remarks>
    private sealed class StubCompassSuperAdmin : ICurrentUserContext
    {
        public Guid EdjeId => Guid.Empty;
        public string Email => "compass.root@example.test";
        public string TpsEmployeeId => string.Empty;
        public IReadOnlyList<string> Privileges => [RolePolicy.CompassSuperAdminRole];

        public bool HasPrivilege(string privilege) =>
            string.Equals(privilege, RolePolicy.CompassSuperAdminRole, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reproduces the REAL <c>CurrentUserContext</c>'s contract outside an HTTP request: every
    /// member throws, because the real implementation reads <c>HttpContext.User</c> and there is
    /// none.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT named <c>NoHttpContextCurrentUser</c>, even though that would read
    /// naturally here. <c>tests/integration/Support/NoHttpContextCurrentUser.cs</c> already owns
    /// that name for the OPPOSITE behavior — the safe double a bare-DI-scope caller should actually
    /// use — and <c>IDirectory</c>'s own remarks point future callers at that file by name. A second
    /// type of the same name with the reverse behavior would be exactly what a reader following that
    /// pointer could copy by mistake.
    /// </remarks>
    private sealed class ThrowingCurrentUserContext : ICurrentUserContext
    {
        public Guid EdjeId => throw new InvalidOperationException("No HTTP context");
        public string Email => throw new InvalidOperationException("No HTTP context");
        public string TpsEmployeeId => throw new InvalidOperationException("No HTTP context");
        public IReadOnlyList<string> Privileges => throw new InvalidOperationException("No HTTP context");
        public bool HasPrivilege(string privilege) => throw new InvalidOperationException("No HTTP context");
    }

    private static Employee BuildEmployee(int id, string first, string last, bool isActive)
        => new()
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Email = $"{first}.{last}@example.test".ToLowerInvariant(),
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = 1,
            IsActive = isActive,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };

    [Fact]
    public async Task GetEmployeeAsync_WhenEmployeeExists_ReturnsPopulatedDto()
    {
        // Arrange
        const int id = 1;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(id, "Ada", "Lovelace", isActive: true));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeAsync(id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.DisplayName.ShouldBe("Ada Lovelace");
        result.IsActive.ShouldBeTrue();
    }

    /// <summary>
    /// The boundary publishes the timezone STORED on the record, at the lowest authorized tier
    /// (PRD v9 FR-8.4, issue #422).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seeded value is deliberately NOT <c>UsTimeZones.Default</c>. <c>Employee.Timezone</c>
    /// initialises to Eastern, so a test seeding Eastern would pass against a projection that hard-codes
    /// it, one that reads the wrong property, and one that was never written at all.
    /// </para>
    /// <para>
    /// The service is built with <see cref="StubCurrentUser"/> — Compass tier Baseline — because the
    /// point is that this member is NOT tier-gated. The consumer FR-8.4 names holds no Compass role, so
    /// a projection that resolved the timezone the way <c>TimeTracking</c> is resolved would withhold it
    /// from the only caller asking for it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetEmployeeAsync_PublishesTheStoredTimezone_EvenAtTheBaselineTier()
    {
        // Arrange
        const int id = 4;
        var employee = BuildEmployee(id, "Katherine", "Johnson", isActive: true);
        employee.Timezone = "America/Denver";
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(employee);
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeAsync(id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Timezone.ShouldBe("America/Denver");

        // The tier really was Baseline — without this the assertion above would also hold under a
        // Super Admin stub, and the "not gated" half of the claim would be untested.
        result.TimeTracking.ShouldBeNull();
    }

    /// <summary>
    /// The boundary publishes the STORED delivery-team flag at the lowest authorized tier
    /// (FR-007, FR-008).
    /// </summary>
    /// <remarks>
    /// Both stored values are asserted, not one. <c>Timezone</c>'s trick of seeding something that
    /// is not the entity default does not transfer to a two-state field: the entity defaults to
    /// <c>true</c> and the DTO member to <c>false</c>, so a <c>false</c>-only case passes against a
    /// projection that hard-codes <c>false</c>, reads the wrong property, or was never written.
    /// Built with <see cref="StubCurrentUser"/>, Compass tier Baseline, because the point is that
    /// this member is not tier-gated.
    /// </remarks>
    [Fact]
    public async Task GetEmployeeAsync_PublishesTheStoredDeliveryTeamFlag_EvenAtTheBaselineTier()
    {
        // Arrange
        var onDelivery = BuildEmployee(5, "Mary", "Jackson", isActive: true);
        onDelivery.IsDeliveryTeam = true;
        var notOnDelivery = BuildEmployee(6, "Dorothy", "Vaughan", isActive: true);
        notOnDelivery.IsDeliveryTeam = false;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(onDelivery);
        repository.Seed(notOnDelivery);
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var delivery = await service.GetEmployeeAsync(5, TestContext.Current.CancellationToken);
        var nonDelivery = await service.GetEmployeeAsync(6, TestContext.Current.CancellationToken);

        // Assert
        delivery.ShouldNotBeNull();
        delivery.IsDeliveryTeam.ShouldBeTrue();
        nonDelivery.ShouldNotBeNull();
        nonDelivery.IsDeliveryTeam.ShouldBeFalse();

        // The tier really was Baseline — without this both assertions above would also hold under a
        // Super Admin stub, and the "not tier-gated" half of the claim would be untested.
        delivery.TimeTracking.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenEmployeeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeAsync(
            9_999, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenEmployeeIsInactive_ReturnsDtoWithInactiveFlag()
    {
        // Arrange — the boundary reports the flag; it does not filter on it. Filtering policy is a
        // Compass team decision this phase must not pre-empt.
        const int id = 2;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(id, "Grace", "Hopper", isActive: false));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeAsync(id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenANamePartIsBlank_TrimsTheDisplayName()
    {
        // Arrange — compass.employee's name columns are NOT NULL, so this is no longer the
        // null-handling case it was against the legacy person record. It is the BLANK case: the
        // columns are non-nullable, not non-empty, and an empty surname would otherwise leave a
        // trailing space in the display name.
        const int id = 3;
        var employee = BuildEmployee(id, "Solo", "Placeholder", isActive: true);
        employee.LastName = string.Empty;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(employee);
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeAsync(id, TestContext.Current.CancellationToken);

        // Assert — no dangling separator, no "Solo " with a trailing space.
        result.ShouldNotBeNull();
        result.DisplayName.ShouldBe("Solo");
    }

    /// <summary>
    /// Locks in a real constraint introduced by Slice 1 (FR-004/FR-008 tier resolution) as a
    /// documented, deliberate contract rather than a landmine a future caller discovers at runtime:
    /// resolving a FOUND employee's tier needs an <c>ICurrentUserContext</c> that tolerates the
    /// absence of an HTTP request. See <c>IDirectory</c>'s remarks and
    /// <c>tests/integration/Support/NoHttpContextCurrentUser.cs</c> for the shape such a caller
    /// needs to supply instead.
    /// </summary>
    [Fact]
    public async Task GetEmployeeAsync_WhenTheEmployeeExists_AndTheCurrentUserContextHasNoHttpContext_PropagatesTheFailure()
    {
        // Arrange
        const int id = 5;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(id, "Ada", "Lovelace", isActive: true));
        var service = new CompassDirectoryService(repository, new ThrowingCurrentUserContext());

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => service.GetEmployeeAsync(id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The other half of the constraint above, precisely: the tier is resolved only AFTER the
    /// employee is found, so a not-found lookup never touches <c>ICurrentUserContext</c> at all. A
    /// caller that only exercises the not-found path against a throwing context would see no
    /// failure and could wrongly conclude the boundary is safe to call from a bare DI scope — this
    /// pins the narrower truth so that false confidence cannot form.
    /// </summary>
    [Fact]
    public async Task GetEmployeeAsync_WhenTheEmployeeDoesNotExist_NeverTouchesTheCurrentUserContext()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new ThrowingCurrentUserContext());

        // Act
        var result = await service.GetEmployeeAsync(9_999, TestContext.Current.CancellationToken);

        // Assert — no exception, because Privileges is never read on the not-found path.
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange — the service is reachable through the boundary interface, which is the contract a
        // later out-of-process extraction would preserve.
        const int id = 4;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployee(id, "Ada", "Lovelace", isActive: true));
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetEmployeeAsync(id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<CompassEmployeeDto>();
    }

    [Fact]
    public async Task GetInvoiceFrequenciesAsync_ProjectsRepositoryEntitiesToDtos()
    {
        // Arrange -- the repository already filters IsActive (proven separately at the repository/
        // integration layers); this test locks in the service's projection, not the filtering.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true });
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetInvoiceFrequenciesAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].Id.ShouldBe(1);
        result[0].TypeName.ShouldBe("Monthly");
    }

    [Fact]
    public async Task GetInvoiceFrequenciesAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true });
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetInvoiceFrequenciesAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldAllBe(dto => dto is CompassInvoiceFrequencyDto);
    }

    [Fact]
    public async Task GetClientAsync_ProjectsRepositoryResultToDto()
    {
        // Arrange -- the repository already derives status (proven separately at the repository/
        // integration layers); this test locks in the service's projection, not the derivation.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(
            new Client
            {
                Id = 1,
                ClientName = "Acme Corp",
                MsaSignedDate = new DateOnly(2022, 1, 1),
                NdaSignedDate = new DateOnly(2022, 1, 2),
                IsInternal = false,
                InvoiceFrequencyType = new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true },
            },
            "Active");
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetClientAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(1);
        result.ClientName.ShouldBe("Acme Corp");
        result.MsaSignedDate.ShouldBe(new DateOnly(2022, 1, 1));
        result.NdaSignedDate.ShouldBe(new DateOnly(2022, 1, 2));
        result.IsInternal.ShouldBeFalse();
        result.InvoiceFrequency.ShouldBe("Monthly");
        result.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task GetClientAsync_WhenClientDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetClientAsync(9_999, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetClientAsync_WithNoInvoiceFrequencySet_ReturnsNullInvoiceFrequency()
    {
        // Arrange -- "none set" is a real state, not withheld data (data-model.md § 2).
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(
            new Client { Id = 1, ClientName = "Zero Assignments Inc", IsInternal = false },
            "Inactive");
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetClientAsync(1, TestContext.Current.CancellationToken);

        // Assert -- a client with zero assignments is still fully returnable (spec 004 FR-034).
        result.ShouldNotBeNull();
        result.InvoiceFrequency.ShouldBeNull();
        result.Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task GetClientAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false }, "Active");
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetClientAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<CompassClientDto>();
    }

    [Fact]
    public async Task GetBillableCategoriesAsync_ProjectsRepositoryEntitiesToDtos_ActiveAndInactiveAlike()
    {
        // Arrange -- the repository already scopes to the client (proven separately at the
        // repository/integration layers); this test locks in the service's projection, including the
        // deliberate asymmetry with invoice frequencies (both flags are published, nothing filtered).
        const int clientId = 1;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(new BillableTimeCategory
        {
            Id = 1,
            ClientId = clientId,
            CategoryName = "Development",
            IsActive = true,
        });
        repository.Seed(new BillableTimeCategory
        {
            Id = 2,
            ClientId = clientId,
            CategoryName = "Legacy Support",
            IsActive = false,
        });
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetBillableCategoriesAsync(
            clientId, TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(dto => dto.Id == 1 && dto.CategoryName == "Development" && dto.IsActive);
        result.ShouldContain(dto => dto.Id == 2 && dto.CategoryName == "Legacy Support" && !dto.IsActive);
        result.ShouldAllBe(dto => dto.ClientId == clientId);
    }

    [Fact]
    public async Task GetBillableCategoriesAsync_WhenClientHasNoCategories_ReturnsEmptyList_NotNull()
    {
        // Arrange -- no not-found case: an unknown/category-less client just has zero categories.
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetBillableCategoriesAsync(
            9_999, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetBillableCategoriesAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        const int clientId = 1;
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(new BillableTimeCategory
        {
            Id = 1,
            ClientId = clientId,
            CategoryName = "Development",
            IsActive = true,
        });
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetBillableCategoriesAsync(
            clientId, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldAllBe(dto => dto is CompassBillableCategoryDto);
    }

    private static ClientAssignment BuildAssignment(
        int id,
        int employeeId,
        int clientId,
        Client? client,
        InvoiceFrequencyType? ownOverride = null)
        => new()
        {
            Id = id,
            EmployeeId = employeeId,
            ClientId = clientId,
            Client = client,
            StartDate = new DateOnly(2022, 1, 1),
            EndDate = null,
            Note = "Some note",
            InvoiceFrequencyType = ownOverride,
        };

    [Fact]
    public async Task GetAssignmentAsync_ProjectsRepositoryEntityToDto()
    {
        // Arrange
        var client = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(1);
        result.EmployeeId.ShouldBe(1);
        result.ClientId.ShouldBe(1);
        result.ClientName.ShouldBe("Acme Corp");
        result.StartDate.ShouldBe(new DateOnly(2022, 1, 1));
        result.EndDate.ShouldBeNull();
        result.Note.ShouldBe("Some note");
    }

    [Fact]
    public async Task GetAssignmentAsync_WhenAssignmentDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentAsync(9_999, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAssignmentAsync_WhenAssignmentHasAnOverride_EffectiveInvoiceFrequencyIsTheOverride()
    {
        // Arrange -- FR-012 precedence case (a): the override wins over the client default.
        var client = new Client
        {
            Id = 1,
            ClientName = "Acme Corp",
            IsInternal = false,
            InvoiceFrequencyType = new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true },
        };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(
            1, employeeId: 1, clientId: 1, client,
            ownOverride: new InvoiceFrequencyType { Id = 2, TypeName = "Quarterly", IsActive = true }));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.EffectiveInvoiceFrequency.ShouldBe("Quarterly");
    }

    [Fact]
    public async Task GetAssignmentAsync_WhenNoOverride_EffectiveInvoiceFrequencyFallsThroughToTheClientDefault()
    {
        // Arrange -- FR-012 precedence case (b).
        var client = new Client
        {
            Id = 1,
            ClientName = "Acme Corp",
            IsInternal = false,
            InvoiceFrequencyType = new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true },
        };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.EffectiveInvoiceFrequency.ShouldBe("Monthly");
    }

    [Fact]
    public async Task GetAssignmentAsync_WhenNeitherIsSet_EffectiveInvoiceFrequencyIsNull()
    {
        // Arrange -- FR-012 precedence case (c): reported data, not a withheld field.
        var client = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.EffectiveInvoiceFrequency.ShouldBeNull();
    }

    [Fact]
    public async Task GetAssignmentAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var client = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetAssignmentAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<CompassAssignmentDto>();
    }

    [Fact]
    public async Task GetAssignmentsByEmployeeAsync_ReturnsOnlyThatEmployeesAssignments()
    {
        // Arrange
        var client = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        repository.Seed(BuildAssignment(2, employeeId: 2, clientId: 1, client));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentsByEmployeeAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].Id.ShouldBe(1);
        result.ShouldAllBe(dto => dto.EmployeeId == 1);
    }

    [Fact]
    public async Task GetAssignmentsByEmployeeAsync_WhenEmployeeHasNoAssignments_ReturnsEmptyList_NotNull()
    {
        // Arrange -- no not-found case: an unknown/assignment-less employee just has zero assignments.
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentsByEmployeeAsync(
            9_999, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAssignmentsByEmployeeAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var client = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetAssignmentsByEmployeeAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldAllBe(dto => dto is CompassAssignmentDto);
    }

    [Fact]
    public async Task GetAssignmentsByClientAsync_ReturnsOnlyThatClientsAssignments()
    {
        // Arrange
        var client1 = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var client2 = new Client { Id = 2, ClientName = "Globex Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client1));
        repository.Seed(BuildAssignment(2, employeeId: 1, clientId: 2, client2));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentsByClientAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].Id.ShouldBe(1);
        result.ShouldAllBe(dto => dto.ClientId == 1);
    }

    [Fact]
    public async Task GetAssignmentsByClientAsync_WhenClientHasNoAssignments_ReturnsEmptyList_NotNull()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetAssignmentsByClientAsync(
            9_999, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAssignmentsByClientAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var client = new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false };
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildAssignment(1, employeeId: 1, clientId: 1, client));
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetAssignmentsByClientAsync(1, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldAllBe(dto => dto is CompassAssignmentDto);
    }

    // ------------------------------------------------------------------ Collection reads (#430)

    /// <summary>
    /// The client LIST read returns every client, each with its derived status (issue #430).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why a list read exists at all. The boundary could previously answer "which clients are
    /// there?" only one id at a time, which a consumer populating a dropdown cannot use: it has no id
    /// to start from. This follows the collection-read shape <c>GetInvoiceFrequenciesAsync</c> already
    /// established — no not-found case, an empty list rather than <c>null</c>.
    /// </para>
    /// <para>
    /// Status is seeded per client by the double, exactly as the single-client projection test does:
    /// the derivation itself is proven at the repository and integration layers, and this test locks
    /// in the projection only.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetClientsAsync_ReturnsEveryClient_WithItsStatus()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(
            new Client
            {
                Id = 1,
                ClientName = "Acme Corp",
                IsInternal = false,
                InvoiceFrequencyType = new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true },
            },
            "Active");
        repository.Seed(
            new Client { Id = 2, ClientName = "Globex Corp", IsInternal = false },
            "Former");
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetClientsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);

        var acme = result.Single(c => c.Id == 1);
        acme.ClientName.ShouldBe("Acme Corp");
        acme.InvoiceFrequency.ShouldBe("Monthly");
        acme.Status.ShouldBe("Active");

        var globex = result.Single(c => c.Id == 2);
        globex.Status.ShouldBe("Former");
    }

    [Fact]
    public async Task GetClientsAsync_WhenThereAreNoClients_ReturnsEmptyList_NotNull()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetClientsAsync(TestContext.Current.CancellationToken);

        // Assert — the collection-read convention: no not-found case (IDirectory's own remarks).
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetClientsAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false }, "Inactive");
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetClientsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldAllBe(dto => dto is CompassClientDto);
    }

    // ------------------------------------------------------------ email-keyed reads (feature 018)
    //
    // Cases C-1 .. C-9. These exist because OOTO holds a legacy Guid and this boundary answers on a
    // Compass int, so email is the only correlation available (#424, PRD v9 FR-8.4).
    //
    // Every test here uses StubCurrentUser -- Privileges => [], i.e. the Baseline tier that EVERY
    // in-process OOTO caller resolves to. That is the tier the feature has to work at, so testing
    // it at any other one would prove the wrong thing.

    /// <summary>A Compass employee carrying an explicit timezone and email.</summary>
    /// <remarks>
    /// <see cref="BuildEmployee"/> leaves <c>Timezone</c> at the entity's <c>UsTimeZones.Default</c>
    /// initialiser, which is Eastern. Every fixture in this repository is Eastern, and that is
    /// precisely why a wrong zone stayed invisible for so long (feature 018 research D-11) -- so
    /// these tests take the zone explicitly and use a NON-Eastern one.
    /// </remarks>
    private static Employee BuildEmployeeWithEmail(
        int id, string first, string last, string email, string timezone)
    {
        var employee = BuildEmployee(id, first, last, isActive: true);
        employee.Email = email;
        employee.Timezone = timezone;
        return employee;
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_WhenTheEmailMatches_ReturnsThatEmployee()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(1);
        result.Timezone.ShouldBe("America/Chicago");
    }

    [Theory]
    [InlineData("ADA.LOVELACE@EXAMPLE.TEST")]
    [InlineData("Ada.Lovelace@Example.Test")]
    [InlineData("  ada.lovelace@example.test  ")]
    [InlineData("\tADA.Lovelace@example.test\n")]
    public async Task GetEmployeeByEmailAsync_IgnoresCaseAndSurroundingWhitespace(string candidate)
    {
        // Arrange -- C-1. The match key is lower(btrim(email)), which is the expression
        // ux_employee_email_ci is built on; matching more strictly would make a record that EXISTS
        // read as missing, because uniqueness itself is enforced that way.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Denver"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            candidate, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Timezone.ShouldBe("America/Denver");
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_MatchesAStoredValueCarryingStrayWhitespace()
    {
        // Arrange -- the OTHER half of C-1, and the reason this must not copy
        // CompassEmployeeRepository.EmailExistsAsync, which trims only the CANDIDATE. Trimming one
        // side leaves the predicate unable to find a stored value with stray whitespace -- and no
        // longer expression-identical to the index either.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "  Ada.Lovelace@Example.Test ", "Pacific/Honolulu"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Timezone.ShouldBe("Pacific/Honolulu");
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_WhenNothingMatches_ReturnsNull()
    {
        // Arrange -- C-2
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            "nobody@example.test", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task GetEmployeeByEmailAsync_FailsClosedOnABlankKey(string? blank)
    {
        // Arrange -- C-3, and the one shape that must never happen: a blank key behaving as a
        // wildcard and handing back an arbitrary employee. Assert null AND that the store was never
        // asked, so an implementation that queries for nothing and gets lucky still fails.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            blank, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
        repository.EmailLookupCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_WithAnInactiveEmployee_StillReturnsThem()
    {
        // Arrange -- C-9. Filtering by activity is the CALLER's decision, matching
        // GetEmployeeAsync, which does not filter either. OOTO applies its own IsActive filter to
        // the legacy row; the boundary must not apply a second, invisible one.
        var repository = new InMemoryCompassDirectoryRepository();
        var inactive = BuildEmployeeWithEmail(
            1, "Dana", "Former", "dana.former@example.test", "America/Anchorage");
        inactive.IsActive = false;
        repository.Seed(inactive);
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            "dana.former@example.test", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.IsActive.ShouldBeFalse();
        result.Timezone.ShouldBe("America/Anchorage");
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_ForABaselineCaller_PublishesTimezoneAndWithholdsTimeTracking()
    {
        // Arrange -- C-7 and C-8 together, because they are the same decision seen from both sides:
        // Timezone is un-gated ON PURPOSE (its own DTO remarks say a gate would withhold it from the
        // only caller it exists for), while TimeTracking stays Super-Admin-only. A single test means
        // neither half can be relaxed without the other going red.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.Timezone.ShouldBe("America/Chicago");
        result.TimeTracking.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<CompassEmployeeDto>();
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_ReturnsOneEntryPerMatchingEmail()
    {
        // Arrange -- the batch case the OOTO employee list needs, across two distinct zones so a
        // per-row resolution is actually being proven rather than one value applied to every row.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        repository.Seed(BuildEmployeeWithEmail(
            2, "Grace", "Hopper", "grace.hopper@example.test", "America/Denver"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeesByEmailAsync(
            ["ADA.Lovelace@example.test", "  grace.hopper@example.test  "],
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.Single(dto => dto.Id == 1).Timezone.ShouldBe("America/Chicago");
        result.Single(dto => dto.Id == 2).Timezone.ShouldBe("America/Denver");
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_OmitsEmailsThatMatchNothing()
    {
        // Arrange -- C-2's batch half. An unmatched email is ABSENT, never a null entry: FR-006
        // requires the caller to distinguish "no Compass record" from "a record with no timezone",
        // and a null placeholder in the list would collapse the two.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeesByEmailAsync(
            ["ada.lovelace@example.test", "nobody@example.test"],
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_CollapsesDuplicateSpellingsToOneEntry()
    {
        // Arrange -- C-5. The match key is lower(btrim(...)), so three spellings of one address are
        // one key. OOTO's own list can legitimately contain two legacy rows sharing an email.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await service.GetEmployeesByEmailAsync(
            ["ada.lovelace@example.test", "ADA.LOVELACE@EXAMPLE.TEST", " Ada.Lovelace@Example.Test "],
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GetEmployeesByEmailAsync_WithNothingUsableToLookUp_ReturnsEmptyWithoutQuerying(
        int blankCount)
    {
        // Arrange -- C-4. Asserting the empty result alone is not enough: a repository that queried
        // for an empty key set would satisfy it and still be wrong, and would still cost a round
        // trip on every OOTO list read whose employees all lack an email.
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        var service = new CompassDirectoryService(repository, new StubCurrentUser());
        string?[] emails = [.. Enumerable.Repeat<string?>("  ", blankCount)];

        // Act
        var result = await service.GetEmployeesByEmailAsync(
            emails, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
        repository.EmailLookupCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_ResolvesTheWholeListInASingleRepositoryCall()
    {
        // Arrange -- FR-007. This is the unit-level half of the constant-read requirement: the
        // service must not fan a collection out into one repository call per email. The other half
        // (that the repository's single call is also a single SQL round trip) can only be proven
        // against real PostgreSQL, and is asserted there.
        var repository = new InMemoryCompassDirectoryRepository();
        for (var id = 1; id <= 25; id++)
        {
            repository.Seed(BuildEmployeeWithEmail(
                id, "Ada", $"Number{id}", $"ada.number{id}@example.test", "America/Chicago"));
        }

        var service = new CompassDirectoryService(repository, new StubCurrentUser());
        string?[] emails = [.. Enumerable.Range(1, 25).Select(id => $"ada.number{id}@example.test")];

        // Act
        var result = await service.GetEmployeesByEmailAsync(
            emails, TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(25);
        repository.EmailLookupCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_ImplementsTheDirectoryBoundary()
    {
        // Arrange
        var repository = new InMemoryCompassDirectoryRepository();
        repository.Seed(BuildEmployeeWithEmail(
            1, "Ada", "Lovelace", "ada.lovelace@example.test", "America/Chicago"));
        IDirectory directory = new CompassDirectoryService(repository, new StubCurrentUser());

        // Act
        var result = await directory.GetEmployeesByEmailAsync(
            ["ada.lovelace@example.test"], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldAllBe(dto => dto is CompassEmployeeDto);
    }
}
