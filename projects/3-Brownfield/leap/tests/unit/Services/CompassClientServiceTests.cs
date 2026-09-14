using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Unit tests for client configuration: the active-only invoice-frequency rule, per-client category
/// name uniqueness, and the audit obligation that spans categories and the invoice default.
/// </summary>
/// <remarks>
/// Hand-written in-memory doubles rather than a mocking framework, matching
/// <c>CompassEmployeeServiceTests</c> and the project's conventions.
/// <para>
/// Status is DERIVED here, never stored and never settable (FR-021, FR-035). US3 shipped these
/// payloads carrying no status at all; US4's T101 adds a value COMPUTED per request from the client's
/// assignments. That is not a reversal — the forbidden thing is a stored flag, and a request that
/// tries to supply one is still refused rather than ignored (<c>CompassAdminClientEndpointsTests</c>).
/// </para>
/// </remarks>
public class CompassClientServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- the doubles

    /// <summary>
    /// In-memory stand-in for client data access.
    /// </summary>
    /// <remarks>
    /// Category name comparison is case-insensitive and scoped to one client, which is the rule
    /// under test (FR-025) and the shape of <c>ux_billable_time_category_client_id_category_name</c>. A
    /// double that compared names globally would make the FR-006 half — the same name on a different
    /// client — pass for the wrong reason.
    /// </remarks>
    private sealed class FakeClientRepository : ICompassClientRepository
    {
        private readonly List<Client> _clients = [];
        private readonly List<BillableTimeCategory> _categories = [];
        private readonly Dictionary<int, (string Name, bool IsActive)> _frequencyTypes = [];
        private readonly HashSet<int> _holdingCurrentAssignment = [];
        private readonly HashSet<int> _everAssigned = [];
        private int _nextClientId = 1;
        private int _nextCategoryId = 1;

        public IReadOnlyList<Client> Clients => _clients;

        public IReadOnlyList<BillableTimeCategory> Categories => _categories;

        /// <summary>Registers an invoice frequency type and whether it is selectable.</summary>
        /// <remarks>
        /// Name and selectability are modelled separately for the same reason the EDJEr double does it:
        /// a retired type keeps its name so a client already defaulted to it keeps displaying that
        /// default (FR-007), while ceasing to be choosable.
        /// </remarks>
        public void SeedFrequencyType(int id, string name, bool isActive) =>
            _frequencyTypes[id] = (name, isActive);

        public Client Seed(string clientName, int? invoiceFrequencyTypeId = null)
        {
            var row = new Client
            {
                Id = _nextClientId++,
                ClientName = clientName,
                IsInternal = false,
                InvoiceFrequencyTypeId = invoiceFrequencyTypeId,
            };
            _clients.Add(row);
            return row;
        }

        public BillableTimeCategory SeedCategory(
            int clientId,
            string categoryName,
            bool isActive = true
        )
        {
            var row = new BillableTimeCategory
            {
                Id = _nextCategoryId++,
                ClientId = clientId,
                CategoryName = categoryName,
                IsActive = isActive,
            };
            _categories.Add(row);
            return row;
        }

        /// <summary>
        /// Gives a client an assignment that is CURRENT as of the business date.
        /// </summary>
        /// <remarks>
        /// Modelled as the set of client ids the set-wise query would return, not as assignment rows
        /// with dates: the date comparison itself belongs to <c>ClientStatusDerivation</c> and is
        /// asserted against real SQL in the integration suite. A double that re-implemented the
        /// comparison here would be a second derivation — exactly what BR-11 forbids — and could agree
        /// with the real one today and diverge on the boundary tomorrow.
        /// </remarks>
        public void SeedCurrentAssignment(int clientId)
        {
            _holdingCurrentAssignment.Add(clientId);

            // A current assignment IS an assignment, so it satisfies both predicates. Keeping the two
            // sets consistent here rather than at each call site is what stops a fixture accidentally
            // describing the impossible client — current, yet never assigned (issue #274).
            _everAssigned.Add(clientId);
        }

        /// <summary>
        /// Gives a client an assignment that has ENDED — engaged in the past, not now (issue #274).
        /// </summary>
        /// <remarks>
        /// Modelled as set membership for the same reason <see cref="SeedCurrentAssignment"/> is: the
        /// only thing separating Former from Inactive is whether any assignment has ever existed, and
        /// re-implementing that as rows with dates here would make this double a second derivation.
        /// </remarks>
        public void SeedEndedAssignment(int clientId) => _everAssigned.Add(clientId);

        public Task<
            IReadOnlyList<(
                Client Client,
                string? InvoiceFrequencyTypeName,
                bool HoldsCurrentAssignment,
                bool HasEverBeenAssigned
            )>
        > GetAllWithFrequencyNameAsync(DateOnly today, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(Client, string?, bool, bool)>>(
                [
                    .. _clients
                        .OrderBy(row => row.ClientName)
                        .Select(row =>
                            (
                                row,
                                row.InvoiceFrequencyTypeId is { } id
                                && _frequencyTypes.TryGetValue(id, out var type)
                                    ? type.Name
                                    : (string?)null,
                                _holdingCurrentAssignment.Contains(row.Id),
                                _everAssigned.Contains(row.Id)
                            )
                        ),
                ]
            );

        public Task<(bool HoldsCurrentAssignment, bool HasEverBeenAssigned)> GetStatusFactsAsync(
            int clientId,
            DateOnly today,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                (_holdingCurrentAssignment.Contains(clientId), _everAssigned.Contains(clientId))
            );

        public Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            var client = _clients.SingleOrDefault(row => row.Id == id);
            if (client is not null)
            {
                client.BillableTimeCategories =
                [
                    .. _categories.Where(category => category.ClientId == id),
                ];
            }
            return Task.FromResult(client);
        }

        public Task<bool> ClientNameExistsAsync(
            string clientName,
            int? excludingId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                _clients.Any(row =>
                    row.Id != excludingId
                    && string.Equals(
                        row.ClientName.Trim(),
                        clientName.Trim(),
                        StringComparison.Ordinal
                    )
                )
            );

        public Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
            int invoiceFrequencyTypeId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                _frequencyTypes.TryGetValue(invoiceFrequencyTypeId, out var type) && type.IsActive
            );

        public Task<BillableTimeCategory?> GetCategoryAsync(
            int clientId,
            int categoryId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                _categories.SingleOrDefault(row =>
                    row.Id == categoryId && row.ClientId == clientId
                )
            );

        public Task<bool> CategoryNameExistsAsync(
            int clientId,
            string categoryName,
            int? excludingId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                _categories.Any(row =>
                    row.ClientId == clientId
                    && row.Id != excludingId
                    && string.Equals(
                        row.CategoryName.Trim(),
                        categoryName.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );

        public Task AddAsync(Client client, CancellationToken cancellationToken)
        {
            client.Id = _nextClientId++;
            _clients.Add(client);
            return Task.CompletedTask;
        }

        public Task AddCategoryAsync(
            BillableTimeCategory category,
            CancellationToken cancellationToken
        )
        {
            category.Id = _nextCategoryId++;
            _categories.Add(category);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : ICompassUnitOfWork
    {
        /// <summary>
        /// Runs the operation with no transaction: these are unit tests over an in-memory double, where
        /// there is nothing to commit. The transactional guarantee itself is asserted against real
        /// Postgres by <c>CompassAuditAtomicityTests</c>, because only a real provider has transactions.
        /// </summary>
        public Task<T> ExecuteAtomicallyAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<T, bool> commitWhen,
            CancellationToken cancellationToken) => operation(cancellationToken);

        public int SaveCount { get; private set; }

        /// <summary>
        /// When set, the next save reports a lost uniqueness race — what the database does when a
        /// concurrent caller committed the same name between this request's check and its write.
        /// </summary>
        public bool FailNextSaveAsDuplicate { get; set; }

        /// <summary>
        /// Which unique index the simulated failure names. Defaults to the name index the service
        /// pre-checks; set it to a provenance index to simulate a duplicate legacy TPS identifier,
        /// which nothing pre-checks at all.
        /// </summary>
        public string? FailNextSaveConstraint { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            if (FailNextSaveAsDuplicate)
            {
                throw new CompassDuplicateKeyException(
                    "A concurrent write already committed a value with this name.",
                    FailNextSaveConstraint ?? "ux_client_client_name"
                );
            }
            return Task.FromResult(1);
        }
    }

    /// <summary>Records what was audited, so a test can assert the entry's content and not just its count.</summary>
    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(AuditEntry entry)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(
            string entityType,
            string entityId
        ) => throw new NotSupportedException("client configuration never reads the audit trail");

        public Task<PaginatedAuditLogResponse> BrowseAsync(
            string? entityType,
            string? actor,
            string? employeeId,
            DateTime? fromDate,
            DateTime? toDate,
            int page,
            int pageSize
        ) => throw new NotSupportedException("client configuration never reads the audit trail");

        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() =>
            throw new NotSupportedException("client configuration never reads the audit trail");
    }

    /// <summary>
    /// A derivation that answers the OPPOSITE of the truth.
    /// </summary>
    /// <remarks>
    /// Not a mock of convenience: it is what distinguishes a service that ASKS the shared derivation
    /// from one that names <c>ClientStatus.Active</c> itself. Both look identical against a truthful
    /// double, and only the second violates BR-11's single-implementation rule.
    /// </remarks>
    private sealed class InvertedDerivation : IClientStatusDerivation
    {
        private readonly ClientStatusDerivation _real = new();

        public System.Linq.Expressions.Expression<Func<ClientAssignment, bool>> IsCurrent(
            DateOnly today
        ) => _real.IsCurrent(today);

        public System.Linq.Expressions.Expression<Func<ClientAssignment, bool>> IsFutureDated(
            DateOnly today
        ) => _real.IsFutureDated(today);

        public System.Linq.Expressions.Expression<Func<Sow, bool>> IsExpiringWithin(
            DateOnly today,
            int days
        ) => _real.IsExpiringWithin(today, days);

        public System.Linq.Expressions.Expression<Func<Sow, bool>> HasNoFollowOn() =>
            _real.HasNoFollowOn();

        public System.Linq.Expressions.Expression<Func<Sow, bool>> IsActiveSow(DateOnly today) =>
            _real.IsActiveSow(today);

        public System.Linq.Expressions.Expression<Func<Sow, bool>> SowAssignmentIsOpenEndedAndActive(
            DateOnly today
        ) => _real.SowAssignmentIsOpenEndedAndActive(today);

        public System.Linq.Expressions.Expression<Func<ClientAssignment, bool>> HasStarted(
            DateOnly today
        ) => _real.HasStarted(today);

        public System.Linq.Expressions.Expression<Func<Client, bool>> IsActive(DateOnly today) =>
            _real.IsActive(today);

        public System.Linq.Expressions.Expression<Func<Client, bool>> HasEverBeenAssigned() =>
            _real.HasEverBeenAssigned();

        public ClientStatus Of(Client client, DateOnly today) =>
            StatusOfClient(holdsCurrentAssignment: true, hasEverBeenAssigned: true);

        // BOTH facts inverted (issue #274), so the answer stays one the real derivation could never
        // give for the row. Inverting only the currency bool would map an engaged client to Former,
        // which is a value the real derivation DOES produce for some clients — and a test whose
        // "impossible" answer is merely unlikely stops distinguishing delegation from local naming.
        public ClientStatus StatusOfClient(bool holdsCurrentAssignment, bool hasEverBeenAssigned) =>
            _real.StatusOfClient(!holdsCurrentAssignment, !hasEverBeenAssigned);

        public AssignmentStatus StatusOfAssignment(bool isCurrent) =>
            _real.StatusOfAssignment(!isCurrent);
    }

    /// <summary>Counts how many times the business date was read, and answers a fixed one.</summary>
    private sealed class FixedBusinessDate : ICompassBusinessDate
    {
        public int Calls { get; private set; }

        public DateOnly Today()
        {
            Calls++;
            return new DateOnly(2026, 8, 14);
        }
    }

    private sealed class StubCurrentUser : ICurrentUserContext
    {
        public Guid EdjeId { get; init; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string Email => "super.admin@example.test";
        public string TpsEmployeeId => "1";
        public IReadOnlyList<string> Privileges { get; init; } =
            ["Compass Super Admin", "Compass Admin"];

        public bool HasPrivilege(string privilege) => Privileges.Contains(privilege);
    }

    /// <summary>The business date double the most recent {@link Build} handed the service.</summary>
    private static FixedBusinessDate LastBusinessDate = new();

    /// <summary>How many times that service read the clock — one per request, never one per row.</summary>
    private static int BusinessDateCalls => LastBusinessDate.Calls;

    private static (
        CompassClientService Service,
        FakeClientRepository Clients,
        CountingUnitOfWork UnitOfWork,
        RecordingAuditService Audit
    ) Build()
    {
        var clients = new FakeClientRepository();
        clients.SeedFrequencyType(1, "Monthly", isActive: true);
        clients.SeedFrequencyType(2, "Fortnightly", isActive: false);

        var unitOfWork = new CountingUnitOfWork();
        var audit = new RecordingAuditService();
        LastBusinessDate = new FixedBusinessDate();
        var service = new CompassClientService(
            clients,
            unitOfWork,
            audit,
            new StubCurrentUser(),
            new ClientStatusDerivation(),
            LastBusinessDate
        );

        return (service, clients, unitOfWork, audit);
    }

    private static CompassClientRequest Request(
        string clientName = "Contoso",
        int? invoiceFrequencyTypeId = 1,
        bool isInternal = false,
        DateOnly? msaSignedDate = null,
        DateOnly? ndaSignedDate = null,
        string? legacyTpsId = null
    ) =>
        new(
            clientName,
            msaSignedDate,
            ndaSignedDate,
            isInternal,
            invoiceFrequencyTypeId,
            legacyTpsId
        );

    // ---------------------------------------------------------------- the happy path

    [Fact]
    public async Task CreateClient_WithAValidRequest_SucceedsAndPersistsOnce()
    {
        // Arrange
        var (service, clients, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.ClientName.ShouldBe("Contoso");
        result.Value.Id.ShouldBeGreaterThan(0);
        clients.Clients.Count.ShouldBe(1);
        unitOfWork.SaveCount.ShouldBe(1, "the SERVICE owns the persistence boundary");
    }

    [Fact]
    public async Task CreateClient_WithNoInvoiceFrequencyDefault_IsAccepted()
    {
        // Arrange — the column is nullable and AC-22 makes the default optional. A client with no
        // cadence set is a normal client, not an incomplete one.
        var (service, clients, _, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(invoiceFrequencyTypeId: null), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        clients.Clients.Single().InvoiceFrequencyTypeId.ShouldBeNull();
    }

    [Fact]
    public async Task CreateClient_WithZeroCategories_IsAccepted()
    {
        // Arrange — FR-024 says zero-to-many. Zero is the count a brand-new client has, and FR-022
        // requires it be immediately usable at that count.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.BillableTimeCategories.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateClient_TrimsTheName_SoTheStoredFormMatchesTheIndexedOne()
    {
        // Arrange
        var (service, clients, _, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(clientName: "  Contoso  "), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        clients.Clients.Single().ClientName.ShouldBe("Contoso");
    }

    [Fact]
    public async Task CreateClient_SetsTheInternalBeachFlag()
    {
        // Arrange — the flag the dashboard's "on the beach" tile is built from.
        var (service, clients, _, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(isInternal: true), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        clients.Clients.Single().IsInternal.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateClient_CapturesBothContractDates()
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var msa = new DateOnly(2024, 3, 1);
        var nda = new DateOnly(2024, 2, 14);

        // Act
        var result = await service.CreateClientAsync(
            Request(msaSignedDate: msa, ndaSignedDate: nda),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        clients.Clients.Single().MsaSignedDate.ShouldBe(msa);
        clients.Clients.Single().NdaSignedDate.ShouldBe(nda);
    }

    // ---------------------------------------------------------------- name uniqueness

    [Fact]
    public async Task CreateClient_WithANameAlreadyHeld_IsRejectedAsAConflict()
    {
        // Arrange
        var (service, clients, unitOfWork, _) = Build();
        clients.Seed("Contoso");

        // Act
        var result = await service.CreateClientAsync(Request(clientName: "Contoso"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace("the rejection must name the conflicting field");
        result.Error!.ShouldContain("name", Case.Insensitive);
        clients.Clients.Count.ShouldBe(1, "no record may be created");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateClient_LosingTheUniquenessRace_IsRejectedAsAConflictNotA500()
    {
        // Arrange — the pre-check is a check-then-act: a concurrent caller can commit the same name
        // between it and this write, and ux_client_client_name then rejects ours. The losing writer must
        // take the SAME path as a sequential duplicate, or the outcome depends on timing they cannot see.
        var (service, _, unitOfWork, _) = Build();
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.CreateClientAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error!.ShouldContain("name", Case.Insensitive);
    }

    [Fact]
    public async Task UpdateClient_KeepingItsOwnName_DoesNotCollideWithItself()
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Contoso", isInternal: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.IsInternal.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateClient_ThatDoesNotExist_IsNotFound()
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.UpdateClientAsync(999_999, Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.NotFound);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateClient_WithNoName_IsRejected(string clientName)
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(clientName: clientName), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    // -------------------------------------------- FR-023: the invoice default must name an ACTIVE type

    [Fact]
    public async Task CreateClient_NamingAnInactiveInvoiceFrequencyType_IsRefusedServerSide()
    {
        // Arrange — type 2 is retired. The selection list omits it; that omission is convenience, and
        // FR-041 makes the server the control. This is the enforcement.
        var (service, clients, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(invoiceFrequencyTypeId: 2), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        clients.Clients.ShouldBeEmpty();
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateClient_NamingAnUnknownInvoiceFrequencyType_IsRefused()
    {
        // Arrange — one question, not two: the server's answer is the same either way.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(invoiceFrequencyTypeId: 404), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }

    [Fact]
    public async Task UpdateClient_LeavingAnAlreadyRetiredDefaultAlone_IsAccepted()
    {
        // Arrange — FR-007: retiring a lookup value must not rewrite the records already using it. A
        // client defaulted to a cadence that was later retired must stay editable in every other field
        // without being forced to re-pick a cadence. Mirrors the EDJEr surface's employee-type rule.
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso", invoiceFrequencyTypeId: 2);

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Contoso", invoiceFrequencyTypeId: 2, isInternal: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(
            CompassWriteStatus.Success,
            "an edit that leaves a retired default alone is not a re-selection of it"
        );
        result.Value.ShouldNotBeNull();
        result.Value.IsInternal.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateClient_ChangingToADifferentRetiredDefault_IsRefused()
    {
        // Arrange — the counterpart to the rule above. Keeping a retired value is grandfathering;
        // MOVING to one is a fresh selection of something unselectable.
        var (service, clients, _, _) = Build();
        clients.SeedFrequencyType(3, "Quarterly", isActive: false);
        var client = clients.Seed("Contoso", invoiceFrequencyTypeId: 2);

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Contoso", invoiceFrequencyTypeId: 3),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }

    [Fact]
    public async Task UpdateClient_ClearingTheInvoiceDefault_IsAccepted()
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso", invoiceFrequencyTypeId: 1);

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Contoso", invoiceFrequencyTypeId: null),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        clients.Clients.Single().InvoiceFrequencyTypeId.ShouldBeNull();
    }

    // ------------------------------------- FR-025 / FR-006: category names are unique PER CLIENT

    [Fact]
    public async Task AddCategory_WithAValidName_SucceedsAndIsActive()
    {
        // Arrange
        var (service, clients, unitOfWork, _) = Build();
        var client = clients.Seed("Contoso");

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.CategoryName.ShouldBe("Development");
        result.Value.IsActive.ShouldBeTrue("a newly added category is offered, not withheld");
        unitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task AddCategory_WithANameTheSameClientAlreadyOffers_IsRejectedAsAConflict()
    {
        // Arrange — FR-025.
        var (service, clients, unitOfWork, _) = Build();
        var client = clients.Seed("Contoso");
        clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        clients.Categories.Count.ShouldBe(1);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task AddCategory_WithTheSameNameOnADifferentClient_IsAccepted()
    {
        // Arrange — FR-006/FR-025's other half, and the reason the unique index is composite. A
        // single-column index would wrongly stop two clients both offering "Development", which is the
        // ordinary case rather than the exceptional one.
        var (service, clients, _, _) = Build();
        var contoso = clients.Seed("Contoso");
        var fabrikam = clients.Seed("Fabrikam");
        clients.SeedCategory(contoso.Id, "Development");

        // Act
        var result = await service.AddCategoryAsync(
            fabrikam.Id,
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        clients.Categories.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    [InlineData("  Development  ")]
    public async Task AddCategory_WithANameThatNormalisesToAnExistingOne_IsRejected(string submitted)
    {
        // Arrange — two categories differing only by case or padding are the same category to a human
        // reading the timesheet dropdown they feed.
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");
        clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest(submitted),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
    }

    [Fact]
    public async Task AddCategory_ToAClientThatDoesNotExist_IsNotFound()
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.AddCategoryAsync(
            999_999,
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.NotFound);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddCategory_WithNoName_IsRejected(string categoryName)
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest(categoryName),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }

    [Fact]
    public async Task UpdateCategory_DeactivatingIt_SucceedsAndLeavesTheRowInPlace()
    {
        // Arrange — AC-23: categories are deactivated through PUT. There is no DELETE, because records
        // elsewhere reference them (Principle VIII).
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Development", IsActive: false),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.IsActive.ShouldBeFalse();
        clients.Categories.Count.ShouldBe(1, "deactivating is not deleting");
    }

    [Fact]
    public async Task UpdateCategory_RenamingItToASiblingsName_IsRejectedAsAConflict()
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");
        clients.SeedCategory(client.Id, "Development");
        var support = clients.SeedCategory(client.Id, "Support");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            support.Id,
            new UpdateBillableTimeCategoryRequest("Development", IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
    }

    [Fact]
    public async Task UpdateCategory_KeepingItsOwnName_DoesNotCollideWithItself()
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Development", IsActive: false),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
    }

    [Fact]
    public async Task UpdateCategory_BelongingToADifferentClient_IsNotFound()
    {
        // Arrange — the category id is real, but not this client's. Answering anything but 404 would let
        // a caller edit another client's configuration by guessing an id.
        var (service, clients, _, _) = Build();
        var contoso = clients.Seed("Contoso");
        var fabrikam = clients.Seed("Fabrikam");
        var category = clients.SeedCategory(contoso.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            fabrikam.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Renamed", IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.NotFound);
    }

    // ------------------------------------------------------------------------------------ audit

    [Fact]
    public async Task CreateClient_IsAudited_NamingActorChangeAndEffectiveRoles()
    {
        // Arrange — FR-027. Unlike the lookup surface, which is deliberately outside the trail
        // (FR-008, AC-NFR-3), every client write is attributable.
        var (service, _, _, audit) = Build();

        // Act
        var result = await service.CreateClientAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityId.ShouldBe(result.Value!.Id.ToString());
        entry.Action.ShouldBe("create");
        entry.Actor.ShouldNotBeNullOrWhiteSpace();
        entry.Reason.ShouldNotBeNullOrWhiteSpace();
        entry.EffectiveRoles.ShouldNotBeNull();
        entry.EffectiveRoles.ShouldContain("Compass Super Admin");
    }

    [Fact]
    public async Task UpdateClient_ChangingTheInvoiceDefault_IsAuditedAsAFieldChange()
    {
        // Arrange — AC-NFR-3 names the invoice-frequency default as inside the audited scope, verbatim.
        var (service, clients, _, audit) = Build();
        var client = clients.Seed("Contoso", invoiceFrequencyTypeId: null);

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Contoso", invoiceFrequencyTypeId: 1),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.Changes.ShouldNotBeNull();
        entry
            .Changes.Select(change => change.Field)
            .ShouldContain(nameof(Client.InvoiceFrequencyTypeId));
    }

    [Fact]
    public async Task AddCategory_IsAudited_AgainstTheOwningClientRecord()
    {
        // Arrange — AC-NFR-3 puts billable categories inside the CLIENT's audited scope. Attributing
        // them to the client is what makes "what changed about Contoso" answerable from one query;
        // giving categories their own entity type would scatter the answer across two.
        var (service, clients, _, audit) = Build();
        var client = clients.Seed("Contoso");

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityId.ShouldBe(
            client.Id.ToString(),
            "a category change is a change to its client's record"
        );
        entry.Changes.ShouldNotBeNull();
        entry.Changes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task UpdateCategory_DeactivatingIt_IsAuditedAgainstTheOwningClientRecord()
    {
        // Arrange
        var (service, clients, _, audit) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Development", IsActive: false),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityId.ShouldBe(client.Id.ToString());

        // The field name is QUALIFIED by the category — "BillableTimeCategory:Development.IsActive",
        // not a bare "IsActive". These entries sit among the client's own field changes, and an
        // unqualified IsActive in a client's trail would read as though the CLIENT had been
        // deactivated, which is the one thing that can never happen (FR-021). The assertion was written
        // against the bare name before the implementation existed and is tightened here rather than
        // relaxed: it now pins both halves.
        var changed = entry.Changes!.Select(change => change.Field).ToList();
        changed.ShouldContain(field => field.EndsWith(nameof(BillableTimeCategory.IsActive)));
        changed.ShouldContain(field => field.Contains("Development"));
        changed.ShouldNotContain(nameof(BillableTimeCategory.IsActive));
    }

    [Fact]
    public async Task ARefusedWrite_IsNotAudited()
    {
        // Arrange — the trail records what happened, not what was attempted. A rejected write changed
        // nothing, and an entry for it would make the trail disagree with the data.
        var (service, clients, _, audit) = Build();
        clients.Seed("Contoso");

        // Act
        var result = await service.CreateClientAsync(Request(clientName: "Contoso"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        audit.Entries.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ the list projection

    [Fact]
    public async Task GetClients_ProjectsEachRow_IncludingTheCadenceNameAndItsAbsence()
    {
        // Arrange — one client with a cadence and one without. The second is the case a LEFT join
        // exists for: a client with no invoice default must still appear, with a null name.
        var (service, clients, _, _) = Build();
        clients.Seed("Contoso", invoiceFrequencyTypeId: 1);
        clients.Seed("Fabrikam");

        // Act
        var summaries = await service.GetClientsAsync(Token);

        // Assert
        summaries.Count.ShouldBe(2);
        summaries
            .Single(row => row.ClientName == "Contoso")
            .InvoiceFrequencyTypeName.ShouldBe("Monthly");
        summaries
            .Single(row => row.ClientName == "Fabrikam")
            .InvoiceFrequencyTypeName.ShouldBeNull(
                "no default is an ordinary state, not missing data"
            );
        summaries.ShouldAllBe(row => row.Id > 0);
        summaries.ShouldAllBe(row => !row.IsInternal);
    }

    [Fact]
    public async Task GetClient_WhenNoSuchClientExists_IsNull()
    {
        // Arrange
        var (service, _, _, _) = Build();

        // Act / Assert
        (await service.GetClientAsync(999_999, Token)).ShouldBeNull();
    }

    // ------------------------------------------------- the remaining lost-race paths (FR-013 shape)

    // compass.client and compass.billable_time_category each carry a SECOND unique index on
    // legacy_tps_id, which no pre-check guards. A collision there must not be reported as a name
    // collision — the caller reads a field they never touched. Category UPDATE is deliberately absent:
    // UpdateBillableTimeCategoryRequest carries no LegacyTpsId, so its save cannot violate that index.

    [Fact]
    public async Task CreateClient_LosingARaceOnTheProvenanceIndex_NamesProvenanceRatherThanTheName()
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();
        unitOfWork.FailNextSaveAsDuplicate = true;
        unitOfWork.FailNextSaveConstraint = "ux_client_legacy_tps_id";

        // Act
        var result = await service.CreateClientAsync(Request(legacyTpsId: "tps-client-1"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("tps-client-1");
        result.Error.ShouldNotContain("client name", Case.Insensitive);
    }

    [Fact]
    public async Task UpdateClient_LosingARaceOnTheProvenanceIndex_NamesProvenanceRatherThanTheName()
    {
        // Arrange
        var (service, clients, unitOfWork, _) = Build();
        var client = clients.Seed("Contoso");
        unitOfWork.FailNextSaveAsDuplicate = true;
        unitOfWork.FailNextSaveConstraint = "ux_client_legacy_tps_id";

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Contoso", legacyTpsId: "tps-client-2"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("tps-client-2");
        result.Error.ShouldNotContain("client name", Case.Insensitive);
    }

    [Fact]
    public async Task AddCategory_LosingARaceOnTheProvenanceIndex_NamesProvenanceRatherThanTheName()
    {
        // Arrange
        var (service, clients, unitOfWork, _) = Build();
        var client = clients.Seed("Contoso");
        unitOfWork.FailNextSaveAsDuplicate = true;
        unitOfWork.FailNextSaveConstraint = "ux_billable_time_category_legacy_tps_id";

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest("Development", "tps-category-1"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("tps-category-1");
        result.Error.ShouldNotContain("category named", Case.Insensitive);
    }

    [Fact]
    public async Task UpdateClient_LosingTheUniquenessRace_IsRejectedAsAConflictNotA500()
    {
        // Arrange — two administrators renaming DIFFERENT clients to the same name both pass the
        // pre-check, and ux_client_client_name rejects the loser.
        var (service, clients, unitOfWork, audit) = Build();
        var client = clients.Seed("Contoso");
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.UpdateClientAsync(
            client.Id,
            Request(clientName: "Renamed"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("name", Case.Insensitive);
        audit.Entries.ShouldBeEmpty("a write that lost its race changed nothing");
    }

    [Fact]
    public async Task AddCategory_LosingTheUniquenessRace_IsRejectedAsAConflictNotA500()
    {
        // Arrange — the composite index is the guard; the pre-check only makes the message useful.
        var (service, clients, unitOfWork, audit) = Build();
        var client = clients.Seed("Contoso");
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.AddCategoryAsync(
            client.Id,
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        audit.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateCategory_LosingTheUniquenessRace_IsRejectedAsAConflictNotA500()
    {
        // Arrange
        var (service, clients, unitOfWork, audit) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Renamed", IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        audit.Entries.ShouldBeEmpty();
    }

    // --------------------------------------------------------------- the remaining validation arms

    [Fact]
    public async Task CreateClient_WithAnOverlongName_IsRejected()
    {
        // Arrange — the column is varchar(200). Refusing here names the field; letting it reach the
        // database produces a provider error nobody can act on.
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateClientAsync(
            Request(clientName: new string('x', 201)),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("200");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateCategory_WithNoName_IsRejected(string categoryName)
    {
        // Arrange — the update path validates the name too. Only the ADD path was covered before, and
        // a rename to blank is the same defect arriving by a different route.
        var (service, clients, unitOfWork, _) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");
        var savesBefore = unitOfWork.SaveCount;

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest(categoryName, IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        unitOfWork.SaveCount.ShouldBe(savesBefore);
    }

    [Fact]
    public async Task UpdateCategory_WithAnOverlongName_IsRejected()
    {
        // Arrange — the column is varchar(100).
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest(new string('x', 101), IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("100");
    }

    [Fact]
    public async Task UpdateCategory_RenamingIt_AuditsTheRenameQualifiedByTheOldName()
    {
        // Arrange — the rename arm of the change list, which deactivation alone never reaches. The
        // field name carries the category so the entry cannot be misread as the CLIENT being renamed.
        var (service, clients, _, audit) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Engineering", IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.CategoryName.ShouldBe("Engineering");

        var changed = audit
            .Entries.ShouldHaveSingleItem()
            .Changes!.Select(change => change.Field)
            .ToList();
        changed.ShouldContain(field => field.EndsWith(nameof(BillableTimeCategory.CategoryName)));
        changed.ShouldContain(field => field.Contains("Development"), "qualified by the name it HAD");
        changed.ShouldNotContain(nameof(BillableTimeCategory.CategoryName));
    }

    [Fact]
    public async Task UpdateCategory_ChangingNothing_RecordsAnEmptyChangeList()
    {
        // Arrange — neither arm of the change list fires. The write still happens and is still
        // attributable; what it must not do is invent a change that did not occur.
        var (service, clients, _, audit) = Build();
        var client = clients.Seed("Contoso");
        var category = clients.SeedCategory(client.Id, "Development");

        // Act
        var result = await service.UpdateCategoryAsync(
            client.Id,
            category.Id,
            new UpdateBillableTimeCategoryRequest("Development", IsActive: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        audit.Entries.ShouldHaveSingleItem().Changes.ShouldBeEmpty();
    }

    // ------------------------------------------------------ US4 T101: derived status on the payload

    [Fact]
    public async Task GetClients_CarriesDerivedStatus_ForEachClient()
    {
        // Arrange — status is DERIVED from assignments (FR-021, BR-11), never stored. The fake tracks
        // which clients hold a current assignment, which is exactly what the set-wise query answers.
        var (service, clients, _, _) = Build();
        var engaged = clients.Seed("Contoso");
        clients.Seed("Fabrikam");
        clients.SeedCurrentAssignment(engaged.Id);

        // Act
        var summaries = await service.GetClientsAsync(Token);

        // Assert
        summaries.Single(row => row.Id == engaged.Id).Status.ShouldBe("Active");
        summaries.Single(row => row.ClientName == "Fabrikam").Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task GetClient_CarriesDerivedStatus()
    {
        // Arrange
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");
        clients.SeedCurrentAssignment(client.Id);

        // Act
        var loaded = await service.GetClientAsync(client.Id, Token);

        // Assert
        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task AClientWithNoAssignments_IsInactive_OnBothTheListAndTheRecord()
    {
        // Arrange — FR-034, and the case every brand-new client is in. It is Inactive and that must
        // limit nothing: the write tests above prove the same client stays fully editable (FR-037).
        var (service, clients, _, _) = Build();
        var client = clients.Seed("Contoso");

        // Act
        var summaries = await service.GetClientsAsync(Token);
        var loaded = await service.GetClientAsync(client.Id, Token);

        // Assert
        summaries.ShouldHaveSingleItem().Status.ShouldBe("Inactive");
        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task Status_ComesFromTheSharedDerivation_NotFromTheServiceNamingTheValues()
    {
        // Arrange — BR-11 requires ONE implementation, because independent ones diverge on the
        // exactly-today boundary (AC-42). This asserts the service asks the derivation rather than
        // deciding: a derivation that inverts its answer must invert what the payload reports.
        var (_, clients, unitOfWork, audit) = Build();
        var client = clients.Seed("Contoso");
        clients.SeedCurrentAssignment(client.Id);

        var service = new CompassClientService(
            clients,
            unitOfWork,
            audit,
            new StubCurrentUser(),
            new InvertedDerivation(),
            new FixedBusinessDate()
        );

        // Act
        var summaries = await service.GetClientsAsync(Token);

        // Assert — the client holds a current assignment, so a service naming the values itself would
        // still say Active. Only one that delegates reports what the (inverted) derivation says.
        summaries.ShouldHaveSingleItem()
            .Status.ShouldBe(
                "Inactive",
                "the value must come from IClientStatusDerivation, not from the service"
            );
    }

    [Fact]
    public async Task TheSameBusinessDate_IsUsedForEveryRowOfOneRequest()
    {
        // Arrange — ICompassBusinessDate exists so "today" is asked for ONCE and is identical across a
        // request. A list that re-read the clock per row could straddle midnight and report two
        // clients inconsistently, which is the boundary AC-42 says independent implementations get
        // wrong.
        var (service, clients, _, _) = Build();
        clients.Seed("Contoso");
        clients.Seed("Fabrikam");
        clients.Seed("Northwind");

        // Act
        await service.GetClientsAsync(Token);

        // Assert
        BusinessDateCalls.ShouldBe(1, "the clock is read once per request, not once per row");
    }
}
