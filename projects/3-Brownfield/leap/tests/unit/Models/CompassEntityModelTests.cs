using LeadingEDJE.Leap.Api.Modules.Compass;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models;

/// <summary>
/// Model-level tests for the seven EDJE Compass entities built from the companion ERD (issue #50).
/// </summary>
/// <remarks>
/// <para>
/// These assert the ERD's DEFAULTS and SHAPE — the things a database constraint cannot express and
/// the live-catalogue tests therefore cannot catch. <c>CompassSchemaFromErdTests</c> covers the other
/// half (the constraints Postgres enforces); between them the entity and its mapping are both pinned.
/// </para>
/// <para>
/// Note the deliberate absences asserted below. A negative requirement — no termination date, no
/// billing rate, no stored client status — has no constraint to fail against, so a test is the only
/// thing that stops someone "completing" the model later.
/// </para>
/// </remarks>
public class CompassEntityModelTests
{
    // ---- lookups ------------------------------------------------------------------------------

    [Fact]
    public void EmployeeType_DefaultsToActive_WithEmptyName()
    {
        // Arrange & Act
        var type = new EmployeeType();

        // Assert — a newly configured lookup is selectable unless explicitly deactivated.
        type.Id.ShouldBe(0);
        type.TypeName.ShouldBe(string.Empty);
        type.IsActive.ShouldBeTrue();
        type.Employees.ShouldBeEmpty();
    }

    [Fact]
    public void InvoiceFrequencyType_DefaultsToActive_WithEmptyName()
    {
        // Arrange & Act
        var frequency = new InvoiceFrequencyType();

        // Assert
        frequency.Id.ShouldBe(0);
        frequency.TypeName.ShouldBe(string.Empty);
        frequency.IsActive.ShouldBeTrue();
        frequency.Clients.ShouldBeEmpty();
    }

    // ---- employee -----------------------------------------------------------------------------

    [Fact]
    public void Employee_DefaultsToActive_WithNoCoach()
    {
        // Arrange & Act
        var employee = new Employee();

        // Assert — the coach is DB-nullable for the top of the org chart, so the default is none.
        employee.IsActive.ShouldBeTrue();
        employee.CoachEmployeeId.ShouldBeNull();
        employee.Coach.ShouldBeNull();
        employee.Coachees.ShouldBeEmpty();
        employee.ClientAssignments.ShouldBeEmpty();
    }

    [Fact]
    public void Employee_TimeTrackingFlags_AllDefaultToFalse()
    {
        // Arrange & Act
        var employee = new Employee();

        // Assert — opt-in, not opt-out: a new EDJEr grants nothing until it is set deliberately.
        employee.TimesheetRequired.ShouldBeFalse();
        employee.CanSubmitUnder40.ShouldBeFalse();
        employee.IncludeInPayroll.ShouldBeFalse();
    }

    [Fact]
    public void Employee_HireDateIsDateOnly_NotDateTime()
    {
        // Arrange & Act
        var employee = new Employee { HireDate = new DateOnly(2026, 1, 5) };

        // Assert — DateOnly maps to a native Postgres `date` with no Kind-stripping converter. A
        // DateTime here would silently acquire a time component and the Npgsql Kind rules with it.
        employee.HireDate.GetType().ShouldBe(typeof(DateOnly));
        employee.HireDate.ShouldBe(new DateOnly(2026, 1, 5));
    }

    [Fact]
    public void Employee_StoresNoTerminationDateAndNoBillingRate()
    {
        // Arrange — AC-NFR-6 states both are NOT stored, and deactivation is IsActive instead.
        var properties = typeof(Employee)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        // Assert
        properties.ShouldNotContain(name => name.Contains("Termination", StringComparison.Ordinal));
        properties.ShouldNotContain(name => name.Contains("Rate", StringComparison.Ordinal));
        properties.ShouldContain(nameof(Employee.IsActive));
    }

    [Fact]
    public void Employee_RoundTripsEveryErdField()
    {
        // Arrange & Act
        var employee = new Employee
        {
            Id = 7,
            FirstName = "Grace",
            LastName = "Hopper",
            HireDate = new DateOnly(2026, 1, 5),
            Email = "grace.hopper@leadingedje.com",
            EmployeeTypeId = 3,
            CoachEmployeeId = 4,
            IsActive = false,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = true,
            IncludeInPayroll = true,
        };

        // Assert
        employee.Id.ShouldBe(7);
        employee.FirstName.ShouldBe("Grace");
        employee.LastName.ShouldBe("Hopper");
        employee.Email.ShouldBe("grace.hopper@leadingedje.com");
        employee.EmployeeTypeId.ShouldBe(3);
        employee.CoachEmployeeId.ShouldBe(4);
        employee.IsActive.ShouldBeFalse();
        employee.StateOfResidence.ShouldBe("OH");
        employee.TimesheetRequired.ShouldBeTrue();
        employee.CanSubmitUnder40.ShouldBeTrue();
        employee.IncludeInPayroll.ShouldBeTrue();
    }

    // ---- client -------------------------------------------------------------------------------

    [Fact]
    public void Client_HasNoStoredStatusProperty()
    {
        // Arrange — status is DERIVED from assignments. A stored flag could disagree with the
        // assignments it summarises, which is why the column does not exist either.
        var properties = typeof(Client).GetProperties().Select(p => p.Name).ToList();

        // Assert
        properties.ShouldNotContain(nameof(Employee.IsActive));
        properties.ShouldNotContain("Status");
    }

    [Fact]
    public void Client_OptionalAgreementDatesAndFrequency_DefaultToNull()
    {
        // Arrange & Act
        var client = new Client();

        // Assert
        client.MsaSignedDate.ShouldBeNull();
        client.NdaSignedDate.ShouldBeNull();
        client.InvoiceFrequencyTypeId.ShouldBeNull();
        client.IsInternal.ShouldBeFalse();
        client.BillableTimeCategories.ShouldBeEmpty();
        client.ClientAssignments.ShouldBeEmpty();
    }

    [Fact]
    public void Client_RoundTripsEveryErdField()
    {
        // Arrange & Act
        var client = new Client
        {
            Id = 11,
            ClientName = "Acme Industrial",
            MsaSignedDate = new DateOnly(2025, 3, 1),
            NdaSignedDate = new DateOnly(2025, 2, 1),
            IsInternal = true,
            InvoiceFrequencyTypeId = 2,
        };

        // Assert
        client.Id.ShouldBe(11);
        client.ClientName.ShouldBe("Acme Industrial");
        client.MsaSignedDate.ShouldBe(new DateOnly(2025, 3, 1));
        client.NdaSignedDate.ShouldBe(new DateOnly(2025, 2, 1));
        client.IsInternal.ShouldBeTrue();
        client.InvoiceFrequencyTypeId.ShouldBe(2);
    }

    // ---- billable time category ---------------------------------------------------------------

    [Fact]
    public void BillableTimeCategory_DefaultsToActive_AndBelongsToAClient()
    {
        // Arrange & Act
        var category = new BillableTimeCategory
        {
            Id = 5,
            ClientId = 11,
            CategoryName = "Development",
        };

        // Assert
        category.IsActive.ShouldBeTrue();
        category.ClientId.ShouldBe(11);
        category.CategoryName.ShouldBe("Development");
        category.Client.ShouldBeNull();
    }

    // ---- client assignment ----------------------------------------------------------------------

    [Fact]
    public void ClientAssignment_DefaultsToOpenEnded_WithNoNote()
    {
        // Arrange & Act
        var assignment = new ClientAssignment();

        // Assert — NULL end date means open-ended, which is what "current client assigned" reads.
        assignment.EndDate.ShouldBeNull();
        assignment.Note.ShouldBeNull();
        assignment.Sows.ShouldBeEmpty();
    }

    [Fact]
    public void ClientAssignment_RoundTripsEveryErdField()
    {
        // Arrange & Act
        var assignment = new ClientAssignment
        {
            Id = 21,
            EmployeeId = 7,
            ClientId = 11,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Note = "Rolling off at year end",
        };

        // Assert
        assignment.Id.ShouldBe(21);
        assignment.EmployeeId.ShouldBe(7);
        assignment.ClientId.ShouldBe(11);
        assignment.StartDate.ShouldBe(new DateOnly(2026, 1, 1));
        assignment.EndDate.ShouldBe(new DateOnly(2026, 12, 31));
        assignment.Note.ShouldBe("Rolling off at year end");
        assignment.Employee.ShouldBeNull();
        assignment.Client.ShouldBeNull();
    }

    // ---- sow ------------------------------------------------------------------------------------

    [Fact]
    public void Sow_DefaultsToAnInitialContract_WithNoRateIncrease()
    {
        // Arrange & Act
        var sow = new Sow();

        // Assert — a new period is an Initial Contract until it is made an extension. Provenance IS
        // modelled, via the third value: legacy TPS tracks no extensions, so nothing is conflated.
        sow.SowType.ShouldBe(SowType.InitialContract);
        sow.RateIncrease.ShouldBeFalse();
        sow.HasPassedApplicationValidation.ShouldBeFalse();
        sow.Note.ShouldBeNull();
        sow.ClientAssignment.ShouldBeNull();
    }

    [Fact]
    public void SowType_HasExactlyTheThreeValuesTheSpecDefines()
    {
        // Arrange & Act — FR-051. The names are persisted verbatim (HasConversion<string>) and are
        // pinned by a database CHECK, so renaming one is a schema change, not a refactor.
        var values = Enum.GetNames<SowType>();

        // Assert
        values.ShouldBe(["InitialContract", "SowExtension", "LegacyMigrated"]);
    }

    [Fact]
    public void Sow_StoresNoRateAmount_OnlyTheIndicator()
    {
        // Arrange — AC-NFR-6: rates are not tracked. RateIncrease is a yes/no flag, not a value.
        var rateProperties = typeof(Sow)
            .GetProperties()
            .Where(p => p.Name.Contains("Rate", StringComparison.Ordinal))
            .ToList();

        // Assert
        rateProperties.Count.ShouldBe(1);
        rateProperties[0].Name.ShouldBe(nameof(Sow.RateIncrease));
        rateProperties[0].PropertyType.ShouldBe(typeof(bool));
    }

    [Fact]
    public void Sow_RoundTripsEveryErdField()
    {
        // Arrange & Act
        var sow = new Sow
        {
            Id = 31,
            ClientAssignmentId = 21,
            SowType = SowType.SowExtension,
            HasPassedApplicationValidation = true,
            RateIncrease = true,
            SowStartDate = new DateOnly(2026, 7, 1),
            SowEndDate = new DateOnly(2026, 12, 31),
            Note = "Second extension",
        };

        // Assert
        sow.Id.ShouldBe(31);
        sow.ClientAssignmentId.ShouldBe(21);
        sow.SowType.ShouldBe(SowType.SowExtension);
        sow.HasPassedApplicationValidation.ShouldBeTrue();
        sow.RateIncrease.ShouldBeTrue();
        sow.SowStartDate.ShouldBe(new DateOnly(2026, 7, 1));
        sow.SowEndDate.ShouldBe(new DateOnly(2026, 12, 31));
        sow.Note.ShouldBe("Second extension");
    }
}
