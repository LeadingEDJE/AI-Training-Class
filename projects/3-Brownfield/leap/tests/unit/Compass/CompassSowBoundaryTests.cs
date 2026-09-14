using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The published Compass Directory boundary's SOW read — read family 4 of the Compass Directory
/// boundary (spec 009 Slice 6; FR-007).
/// </summary>
/// <remarks>
/// <para>
/// Hits <c>/api/compass/v1/assignments/{id}/sows</c> over <see cref="TestWebApplicationFactory"/>, the
/// same HTTP surface a real out-of-process consumer would call — mapped inside the EXISTING
/// <c>CompassAssignmentEndpoints</c> route group, inheriting <c>RolePolicy.CompassAdmin</c> from it
/// rather than declaring a new group.
/// </para>
/// <para>
/// There is no reachable Baseline-tier caller for this route. The group requires
/// <c>RolePolicy.CompassAdmin</c>, satisfied only by the Compass Admin (Elevated tier) or Compass
/// Super Admin (SuperAdmin tier) roles, and <c>CompassTierVisibility.SeesOthersSowsAndNotes</c> is
/// <c>tier &gt;= Elevated</c> — so EVERY caller who can reach this handler at all already satisfies
/// the gate and sees <see cref="CompassDirectorySowDto.RateIncrease"/> and <see cref="CompassDirectorySowDto.Note"/>.
/// <c>AsBaselineEdjer()</c> is refused 403 before the handler runs
/// (<c>CompassBoundaryAuthorizationTests</c>), so asserting against its body here would be vacuous —
/// exactly the situation <c>Dtos/Read/SowRowDto.cs</c> already documents for its own SOW surface. This
/// file therefore proves (1) both fields are genuinely PRESENT for the two reachable tiers, and (2) the
/// absent-vs-null mechanism itself (<c>[JsonIgnore(WhenWritingNull)]</c>) is exercised via a note that
/// was simply never recorded, mirroring <c>SowRowDto</c>'s own documented resolution of the identical
/// situation rather than a tier-driven withholding case this route cannot reach.
/// </para>
/// </remarks>
public class CompassSowBoundaryTests : IClassFixture<TestWebApplicationFactory>
{
    private const int EmployeeTypeId = 1;
    private const int EmployeeId = 1;
    private const int ClientId = 1;

    /// <summary>Holds all three <see cref="SowType"/> values, one of which carries a note.</summary>
    private const int AssignmentWithSowsId = 1;

    /// <summary>Holds exactly one SOW, with no note ever recorded.</summary>
    private const int AssignmentWithNoteFreeSowId = 2;

    private const int InitialContractSowId = 1;
    private const int SowExtensionSowId = 2;
    private const int LegacyMigratedSowId = 3;
    private const int NoteFreeSowId = 4;

    private readonly TestWebApplicationFactory _factory;

    public CompassSowBoundaryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static string Route(int assignmentId) => $"/api/compass/v1/assignments/{assignmentId}/sows";

    [Fact]
    public async Task GetSows_ReturnsEachSowsTypeAndDates_WithAllThreeSowTypesRepresented()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(AssignmentWithSowsId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassDirectorySowDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(3);
        dtos.ShouldAllBe(dto => dto.ClientAssignmentId == AssignmentWithSowsId);

        dtos.ShouldContain(dto =>
            dto.Id == InitialContractSowId
            && dto.SowType == nameof(SowType.InitialContract)
            && dto.SowStartDate == new DateOnly(2020, 1, 1)
            && dto.SowEndDate == new DateOnly(2020, 12, 31));

        dtos.ShouldContain(dto =>
            dto.Id == SowExtensionSowId
            && dto.SowType == nameof(SowType.SowExtension)
            && dto.SowStartDate == new DateOnly(2021, 1, 1)
            && dto.SowEndDate == new DateOnly(2021, 12, 31));

        dtos.ShouldContain(dto =>
            dto.Id == LegacyMigratedSowId
            && dto.SowType == nameof(SowType.LegacyMigrated)
            && dto.SowStartDate == new DateOnly(2018, 1, 1)
            && dto.SowEndDate == new DateOnly(2018, 12, 31));
    }

    [Fact]
    public async Task GetSows_ReturnsRateIncreaseAndNote_ForCompassAdmin_TheElevatedTier()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(AssignmentWithSowsId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassDirectorySowDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        var extension = dtos.Single(dto => dto.Id == SowExtensionSowId);
        extension.RateIncrease.ShouldBe(true);
        extension.Note.ShouldBe("Renewal note");
    }

    [Fact]
    public async Task GetSows_ReturnsRateIncreaseAndNote_ForCompassSuperAdmin()
    {
        // Act
        var response = await _factory.AsCompassSuperAdmin()
            .GetAsync(Route(AssignmentWithSowsId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassDirectorySowDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        var extension = dtos.Single(dto => dto.Id == SowExtensionSowId);
        extension.RateIncrease.ShouldBe(true);
        extension.Note.ShouldBe("Renewal note");
    }

    [Fact]
    public async Task GetSows_OmitsNote_WhenNoNoteWasEverRecorded()
    {
        // Arrange -- a single-SOW assignment whose SOW carries no note, so the raw JSON response is
        // clean to assert against (a multi-item array where only SOME items lack a note cannot be
        // checked with a whole-body ShouldNotContain). ABSENT because none was ever given, not because
        // it was withheld -- there is no reachable Baseline caller for this route (see class remarks).

        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(AssignmentWithNoteFreeSowId), TestContext.Current.CancellationToken);

        // Assert -- must actually reach the handler, or the payload assertion below is vacuous.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // ABSENT from the payload, not present-and-null: a null would still tell a reader the field
        // exists on this record.
        json.ShouldNotContain("\"note\"", Case.Insensitive);
    }

    [Fact]
    public async Task GetSows_WhenAssignmentHasNoSows_ReturnsEmptyList_NotAnError()
    {
        // Act -- no not-found case: an unknown/SOW-less assignment simply has zero SOWs, matching the
        // collection-GET precedent from Slices 2/4/5.
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(9_999), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassDirectorySowDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.ShouldBeEmpty();
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Sow>().Any())
        {
            return;
        }

        context.Set<EmployeeType>().Add(
            new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>().Add(new Employee
        {
            Id = EmployeeId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada.lovelace@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = EmployeeTypeId,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true,
        });

        context.Set<Client>().Add(new Client
        {
            Id = ClientId,
            ClientName = "Acme Corp",
            IsInternal = false,
        });

        context.SaveChanges();

        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = AssignmentWithSowsId,
                EmployeeId = EmployeeId,
                ClientId = ClientId,
                StartDate = new DateOnly(2018, 1, 1),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = AssignmentWithNoteFreeSowId,
                EmployeeId = EmployeeId,
                ClientId = ClientId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            });

        context.SaveChanges();

        context.Set<Sow>().AddRange(
            new Sow
            {
                Id = InitialContractSowId,
                ClientAssignmentId = AssignmentWithSowsId,
                SowType = SowType.InitialContract,
                SowStartDate = new DateOnly(2020, 1, 1),
                SowEndDate = new DateOnly(2020, 12, 31),
                RateIncrease = false,
                Note = null,
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = SowExtensionSowId,
                ClientAssignmentId = AssignmentWithSowsId,
                SowType = SowType.SowExtension,
                SowStartDate = new DateOnly(2021, 1, 1),
                SowEndDate = new DateOnly(2021, 12, 31),
                RateIncrease = true,
                Note = "Renewal note",
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = LegacyMigratedSowId,
                ClientAssignmentId = AssignmentWithSowsId,
                SowType = SowType.LegacyMigrated,
                SowStartDate = new DateOnly(2018, 1, 1),
                SowEndDate = new DateOnly(2018, 12, 31),
                RateIncrease = false,
                Note = null,
                HasPassedApplicationValidation = false,
            },
            new Sow
            {
                Id = NoteFreeSowId,
                ClientAssignmentId = AssignmentWithNoteFreeSowId,
                SowType = SowType.InitialContract,
                SowStartDate = new DateOnly(2022, 1, 1),
                SowEndDate = new DateOnly(2022, 12, 31),
                RateIncrease = false,
                Note = null,
                HasPassedApplicationValidation = true,
            });

        context.SaveChanges();
    }
}
