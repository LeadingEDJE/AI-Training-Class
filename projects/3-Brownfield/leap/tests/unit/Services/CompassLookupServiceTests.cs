using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Unit tests for lookup administration: add, rename, retire, duplicate rejection, and the active-only
/// query both configuration forms consume.
/// </summary>
/// <remarks>
/// Uses a hand-written in-memory repository double rather than a mocking framework, matching
/// <c>CompassDirectoryServiceTests</c> and the project's test conventions.
/// </remarks>
public class CompassLookupServiceTests
{
    /// <summary>
    /// In-memory stand-in for one lookup's data access. Records whether persistence was requested, so
    /// a test can assert the service — not the repository — owns the save boundary.
    /// </summary>
    private sealed class FakeLookupRepository<TLookup> : ICompassLookupRepository<TLookup>
        where TLookup : class, ICompassLookup, new()
    {
        private readonly List<TLookup> _rows = [];
        private int _nextId = 1;

        public IReadOnlyList<TLookup> Rows => _rows;

        public TLookup Seed(string typeName, bool isActive = true)
        {
            var row = new TLookup
            {
                Id = _nextId++,
                TypeName = typeName,
                IsActive = isActive,
            };
            _rows.Add(row);
            return row;
        }

        public Task<IReadOnlyList<TLookup>> GetAllAsync(
            bool activeOnly,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<TLookup>>(
                [.. _rows.Where(row => !activeOnly || row.IsActive)]
            );

        public Task<TLookup?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.SingleOrDefault(row => row.Id == id));

        public Task<bool> NameExistsAsync(
            string typeName,
            int? excludingId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                _rows.Any(row =>
                    row.Id != excludingId
                    && string.Equals(row.TypeName, typeName, StringComparison.OrdinalIgnoreCase)
                )
            );

        public Task AddAsync(TLookup lookup, CancellationToken cancellationToken)
        {
            lookup.Id = _nextId++;
            _rows.Add(lookup);
            return Task.CompletedTask;
        }
    }

    /// <summary>Counts persistence calls so the save boundary can be asserted.</summary>
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

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            if (FailNextSaveAsDuplicate)
            {
                throw new CompassDuplicateKeyException("ux_employee_type_type_name");
            }
            return Task.FromResult(1);
        }
    }

    private static (
        CompassLookupService Service,
        FakeLookupRepository<EmployeeType> EmployeeTypes,
        FakeLookupRepository<InvoiceFrequencyType> InvoiceFrequencyTypes,
        CountingUnitOfWork UnitOfWork
    ) Build()
    {
        var employeeTypes = new FakeLookupRepository<EmployeeType>();
        var invoiceFrequencyTypes = new FakeLookupRepository<InvoiceFrequencyType>();
        var unitOfWork = new CountingUnitOfWork();
        return (
            new CompassLookupService(employeeTypes, invoiceFrequencyTypes, unitOfWork),
            employeeTypes,
            invoiceFrequencyTypes,
            unitOfWork
        );
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- reading

    [Fact]
    public async Task GetEmployeeTypesAsync_ActiveOnly_OmitsRetiredValues()
    {
        // Arrange — this is the query the EDJEr configuration form consumes (AC-25).
        var (service, employeeTypes, _, _) = Build();
        employeeTypes.Seed("Full Time");
        employeeTypes.Seed("Contract", isActive: false);

        // Act
        var result = await service.GetEmployeeTypesAsync(activeOnly: true, Token);

        // Assert
        result.Select(dto => dto.TypeName).ShouldBe(["Full Time"]);
    }

    [Fact]
    public async Task GetEmployeeTypesAsync_NotActiveOnly_IncludesRetiredValues()
    {
        // Arrange — the admin screen must show retired values, or they could never be reinstated.
        var (service, employeeTypes, _, _) = Build();
        employeeTypes.Seed("Full Time");
        employeeTypes.Seed("Contract", isActive: false);

        // Act
        var result = await service.GetEmployeeTypesAsync(activeOnly: false, Token);

        // Assert
        result.Select(dto => dto.TypeName).ShouldBe(["Full Time", "Contract"], ignoreOrder: true);
        result.Single(dto => dto.TypeName == "Contract").IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task GetInvoiceFrequencyTypesAsync_ActiveOnly_OmitsRetiredValues()
    {
        // Arrange — the client configuration form's invoice-frequency default (AC-26).
        var (service, _, invoiceFrequencyTypes, _) = Build();
        invoiceFrequencyTypes.Seed("Monthly");
        invoiceFrequencyTypes.Seed("Fortnightly", isActive: false);

        // Act
        var result = await service.GetInvoiceFrequencyTypesAsync(activeOnly: true, Token);

        // Assert
        result.Select(dto => dto.TypeName).ShouldBe(["Monthly"]);
    }

    // ---------------------------------------------------------------- creating

    [Fact]
    public async Task CreateEmployeeTypeAsync_WithANewName_CreatesItActive()
    {
        // Arrange
        var (service, employeeTypes, _, unitOfWork) = Build();

        // Act
        var result = await service.CreateEmployeeTypeAsync("Contract", Token);

        // Assert — a new value is immediately selectable, per quickstart 3.1 step 2.
        result.Status.ShouldBe(AdminMutationStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value!.TypeName.ShouldBe("Contract");
        result.Value!.IsActive.ShouldBeTrue();
        result.Value!.Id.ShouldBeGreaterThan(0);
        employeeTypes.Rows.ShouldHaveSingleItem();
        unitOfWork.SaveCount.ShouldBe(1, "the SERVICE owns the persistence boundary");
    }

    [Fact]
    public async Task CreateEmployeeTypeAsync_TrimsSurroundingWhitespace()
    {
        // Arrange — otherwise " Contract" and "Contract" coexist and read as duplicates to a human.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEmployeeTypeAsync("  Contract  ", Token);

        // Assert
        result.Value!.TypeName.ShouldBe("Contract");
    }

    [Fact]
    public async Task CreateEmployeeTypeAsync_WithADuplicateName_IsRejected()
    {
        // Arrange — FR-004.
        var (service, employeeTypes, _, unitOfWork) = Build();
        employeeTypes.Seed("Contract");

        // Act
        var result = await service.CreateEmployeeTypeAsync("Contract", Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        employeeTypes.Rows.Count.ShouldBe(1, "the duplicate must not be written");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("CONTRACT")]
    [InlineData("  Contract ")]
    public async Task CreateEmployeeTypeAsync_WithANameDifferingOnlyByCaseOrSpace_IsRejected(
        string candidate
    )
    {
        // Arrange — a lookup list holding both "Contract" and "contract" is a defect, not a feature.
        // The database index on type_name is an exact match, so the service is the enforcement for
        // case and whitespace variants.
        var (service, employeeTypes, _, _) = Build();
        employeeTypes.Seed("Contract");

        // Act
        var result = await service.CreateEmployeeTypeAsync(candidate, Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateEmployeeTypeAsync_WithABlankName_IsRejectedAsInvalidNotAsAConflict(
        string candidate
    )
    {
        // Arrange — a blank name is a malformed request (400), not a collision (409).
        var (service, _, _, unitOfWork) = Build();

        // Act
        var result = await service.CreateEmployeeTypeAsync(candidate, Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateEmployeeTypeAsync_WithANameLongerThanTheColumn_IsRejected()
    {
        // Arrange — type_name is varchar(50). Refusing here beats a database error surfacing as a 500.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEmployeeTypeAsync(new string('x', 51), Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    [Fact]
    public async Task CreateInvoiceFrequencyTypeAsync_WithADuplicateName_IsRejected()
    {
        // Arrange — the same rule applies to the other lookup (FR-006).
        var (service, _, invoiceFrequencyTypes, _) = Build();
        invoiceFrequencyTypes.Seed("Monthly");

        // Act
        var result = await service.CreateInvoiceFrequencyTypeAsync("monthly", Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
    }

    [Fact]
    public async Task CreateInvoiceFrequencyTypeAsync_DoesNotCollideWithAnEmployeeTypeName()
    {
        // Arrange — the two lookups are independent name spaces.
        var (service, employeeTypes, _, _) = Build();
        employeeTypes.Seed("Monthly");

        // Act
        var result = await service.CreateInvoiceFrequencyTypeAsync("Monthly", Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
    }

    // ---------------------------------------------------------------- updating

    [Fact]
    public async Task UpdateEmployeeTypeAsync_RenamesTheValue()
    {
        // Arrange
        var (service, employeeTypes, _, unitOfWork) = Build();
        var existing = employeeTypes.Seed("Contract");

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            "Contractor",
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        result.Value!.TypeName.ShouldBe("Contractor");
        existing.TypeName.ShouldBe("Contractor");
        unitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateEmployeeTypeAsync_RetiresTheValue()
    {
        // Arrange — retiring is an edit, which is why there is no separate deactivate route.
        var (service, employeeTypes, _, _) = Build();
        var existing = employeeTypes.Seed("Contract");

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            "Contract",
            isActive: false,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        result.Value!.IsActive.ShouldBeFalse();
        existing.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateEmployeeTypeAsync_KeepingItsOwnName_IsNotAConflictWithItself()
    {
        // Arrange — the collision check must exclude the row being edited, or retiring a value would
        // be impossible without also renaming it.
        var (service, employeeTypes, _, _) = Build();
        var existing = employeeTypes.Seed("Contract");

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            "Contract",
            isActive: false,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
    }

    [Fact]
    public async Task UpdateEmployeeTypeAsync_TakingAnotherValuesName_IsRejected()
    {
        // Arrange
        var (service, employeeTypes, _, _) = Build();
        employeeTypes.Seed("Full Time");
        var second = employeeTypes.Seed("Contract");

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            second.Id,
            "Full Time",
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        second.TypeName.ShouldBe("Contract", "the rejected rename must not have been applied");
    }

    // The create path refuses a blank and an over-long name; the update path refuses them with the
    // SAME helper and had no test for either. Worth covering rather than assuming, because the guard
    // sits BEFORE the id lookup: an invalid name is a 400 even when the id would have been a 404, and
    // that ordering is not visible from the create path's tests at all.

    [Theory]
    [InlineData("", "blank")]
    [InlineData("   ", "whitespace only")]
    public async Task UpdateEmployeeTypeAsync_WithABlankName_IsRejectedAsInvalidNotAsAConflict(
        string candidate,
        string because
    )
    {
        // Arrange
        var (service, employeeTypes, _, unitOfWork) = Build();
        var existing = employeeTypes.Seed("Contract");

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            candidate,
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError, because);
        existing.TypeName.ShouldBe("Contract", "a refused rename must not have been applied");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateEmployeeTypeAsync_WithANameLongerThanTheColumn_IsRejected()
    {
        // Arrange — type_name is varchar(50), so 51 characters is the first value that must fail.
        var (service, employeeTypes, _, unitOfWork) = Build();
        var existing = employeeTypes.Seed("Contract");

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            new string('x', 51),
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        existing.TypeName.ShouldBe("Contract", "a refused rename must not have been applied");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateEmployeeTypeAsync_ForAnUnknownId_ReportsNotFound()
    {
        // Arrange
        var (service, _, _, unitOfWork) = Build();

        // Act
        var result = await service.UpdateEmployeeTypeAsync(4242, "Contract", isActive: true, Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateInvoiceFrequencyTypeAsync_RetiresTheValue()
    {
        // Arrange
        var (service, _, invoiceFrequencyTypes, _) = Build();
        var existing = invoiceFrequencyTypes.Seed("Fortnightly");

        // Act
        var result = await service.UpdateInvoiceFrequencyTypeAsync(
            existing.Id,
            "Fortnightly",
            isActive: false,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        existing.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateInvoiceFrequencyTypeAsync_ForAnUnknownId_ReportsNotFound()
    {
        // Arrange
        var (service, _, _, _) = Build();

        // Act
        var result = await service.UpdateInvoiceFrequencyTypeAsync(
            4242,
            "Monthly",
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
    }

    // ---------------------------------------------------------------- the audit asymmetry (FR-008)

    [Fact]
    public async Task LookupWrites_Succeed_WithNoAuditServiceAvailableAtAll()
    {
        // Arrange — the deliberate asymmetry from AC-NFR-3, stated verbatim: "Lookup tables (employee
        // types, invoice frequency types) are not audited." Every OTHER Compass configuration write IS
        // audited, so this is the one place a reviewer might "fix" a non-bug.
        //
        // The assertion is structural rather than behavioural on purpose. A recording audit double
        // that nothing is wired to would report zero entries whatever the service did — the fail-open
        // shape this repository has been bitten by. Instead: the service cannot reach an audit service,
        // and every write path still succeeds.
        var (service, employeeTypes, _, unitOfWork) = Build();
        var existing = employeeTypes.Seed("Contract");

        // Act — every write path this surface offers.
        var created = await service.CreateEmployeeTypeAsync("Consultant", Token);
        var updated = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            "Contractor",
            isActive: false,
            Token
        );
        var frequency = await service.CreateInvoiceFrequencyTypeAsync("Fortnightly", Token);

        // Assert
        created.Status.ShouldBe(AdminMutationStatus.Success);
        updated.Status.ShouldBe(AdminMutationStatus.Success);
        frequency.Status.ShouldBe(AdminMutationStatus.Success);
        unitOfWork.SaveCount.ShouldBe(3);
    }

    [Fact]
    public void TheLookupService_CannotReachTheAuditService()
    {
        // Arrange & Act — the structural half of FR-008: not "does not audit today" but "cannot".
        var dependencies = typeof(CompassLookupService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        // Assert
        dependencies.ShouldNotBeEmpty("the service takes its collaborators by constructor injection");
        dependencies.ShouldNotContain(
            typeof(IAuditService),
            "lookup administration is outside the audit trail by requirement (FR-008), so the service "
                + "must not even be able to reach the audit service"
        );
    }

    // ---------------------------------------------------------- the check-then-act race (PR #213)

    [Fact]
    public async Task CreateEmployeeTypeAsync_LosingTheUniquenessRace_ReportsAConflictNotAnUnhandledFailure()
    {
        // Arrange — every uniqueness guard here is a check-then-act: NameExistsAsync says the name is
        // free, then a concurrent caller commits it before this request's write lands. The repository
        // double reports the name as free, so this is the losing writer's exact path.
        //
        // Before this was handled the database's unique-index rejection escaped the service and became
        // a bare HTTP 500, where the caller should see the same 409 the sequential path produces.
        // Flagged in review of PR #213; the platform hit the same shape in UserRoleService.AssignRoleAsync,
        // reproduced live there by two Playwright workers running one fixture assignment at once.
        var (service, _, _, unitOfWork) = Build();
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.CreateEmployeeTypeAsync("Contract", Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateEmployeeTypeAsync_LosingTheUniquenessRace_ReportsAConflict()
    {
        // Arrange — the same race on rename: two administrators rename different values to the same
        // name, both pass the check, and the loser's write is rejected.
        var (service, employeeTypes, _, unitOfWork) = Build();
        var existing = employeeTypes.Seed("Contract");
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.UpdateEmployeeTypeAsync(
            existing.Id,
            "Contractor",
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateInvoiceFrequencyTypeAsync_LosingTheUniquenessRace_ReportsAConflict()
    {
        // Arrange — the other lookup shares the code path, so it must share the behaviour.
        var (service, _, _, unitOfWork) = Build();
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.CreateInvoiceFrequencyTypeAsync("Fortnightly", Token);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
    }

    [Fact]
    public async Task UpdateInvoiceFrequencyTypeAsync_LosingTheUniquenessRace_ReportsAConflict()
    {
        // Arrange
        var (service, _, invoiceFrequencyTypes, unitOfWork) = Build();
        var existing = invoiceFrequencyTypes.Seed("Monthly");
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.UpdateInvoiceFrequencyTypeAsync(
            existing.Id,
            "Fortnightly",
            isActive: true,
            Token
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
    }

    [Fact]
    public async Task ASaveFailureThatIsNotADuplicate_IsNotSwallowedAsAConflict()
    {
        // Arrange — the guard on the guard. Catching every save failure as 409 would hide real faults
        // behind a message telling the administrator to pick a different name.
        var employeeTypes = new FakeLookupRepository<EmployeeType>();
        var service = new CompassLookupService(
            employeeTypes,
            new FakeLookupRepository<InvoiceFrequencyType>(),
            new ThrowingUnitOfWork()
        );

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => service.CreateEmployeeTypeAsync("Contract", Token)
        );
    }

    /// <summary>A unit of work whose failure is NOT a uniqueness violation.</summary>
    private sealed class ThrowingUnitOfWork : ICompassUnitOfWork
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

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the connection dropped");
    }
}

