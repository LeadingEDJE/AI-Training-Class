using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// US6/#64 — the assignment-level invoice-frequency override and its precedence, against real
/// PostgreSQL (FR-036 to FR-039, AC-28, BR-8).
/// </summary>
/// <remarks>
/// <para>
/// The resolution is a three-case rule, and the third is the one that gets built wrong: the
/// override wins where set, the client default applies where it is not, and where NEITHER is set the
/// effective frequency is "none set" — <c>null</c> on the wire. FR-039 is explicit that this is
/// neither an error nor a substituted value, so a service that fell back to some house default, or
/// refused the write, would satisfy the first two cases and still be wrong.
/// </para>
/// <para>
/// Against real PostgreSQL because the resolution reads through two navigations — the assignment's own
/// override and its client's default — and a missing <c>Include</c> yields a confident <c>null</c>
/// rather than an exception. That is the failure the InMemory provider cannot show, because it resolves
/// navigations it was never asked to load.
/// </para>
/// <para>
/// Authorization denials are the unit project's <c>CompassAssignmentAuthorizationTests</c>', per the
/// module convention of keeping a denial failure from reading as a behaviour failure (research R-7).
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAssignmentInvoiceFrequencyTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAssignmentInvoiceFrequencyTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string AssignmentsUrl = "/api/compass/assignments";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- T106 — the override is carried

    [Fact]
    public async Task Post_WithAnOverride_CarriesItOnTheRow()
    {
        // Arrange — FR-036: an assignment supports an optional override of its own.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var monthly = await SeedInvoiceFrequencyTypeAsync("Monthly");

        // Act
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null, monthly),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.InvoiceFrequencyTypeId.ShouldBe(monthly);
        dto.EffectiveInvoiceFrequency.ShouldBe("Monthly");
    }

    // ---------------------------------------------------------------- T107 — precedence

    [Fact]
    public async Task TheOverride_TakesPrecedenceOverTheClientDefault()
    {
        // Arrange — AC-28/FR-037. The client bills Weekly by default; this engagement bills Monthly.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var weekly = await SeedInvoiceFrequencyTypeAsync("Weekly");
        var monthly = await SeedInvoiceFrequencyTypeAsync("Monthly");
        var clientId = await SeedClientAsync(invoiceFrequencyTypeId: weekly);

        // Act
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null, monthly),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBe("Monthly", "the override wins where one is set");

        // And the client's own default is untouched — an override is not an edit of the client.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<Client>().AsNoTracking().SingleAsync(c => c.Id == clientId, Token);
        stored.InvoiceFrequencyTypeId.ShouldBe(weekly);
    }

    [Fact]
    public async Task WithNoOverride_TheClientDefaultApplies()
    {
        // Arrange — FR-037's second half, and the case a resolution that only ever read the override
        // would report as "none set".
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var weekly = await SeedInvoiceFrequencyTypeAsync("Weekly");
        var clientId = await SeedClientAsync(invoiceFrequencyTypeId: weekly);

        // Act
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null, null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.InvoiceFrequencyTypeId.ShouldBeNull("the assignment set no override of its own");
        dto.EffectiveInvoiceFrequency.ShouldBe("Weekly", "so the client default applies");
    }

    // ---------------------------------------------------------------- T108 — neither set

    [Fact]
    public async Task WithNeitherSet_TheEffectiveFrequencyIsNoneSet_NotAnErrorAndNotSubstituted()
    {
        // Arrange — FR-039. "None set" is a legitimate answer: a client may genuinely have no cadence
        // agreed yet, and neither refusing the write nor inventing a house default is acceptable.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        await SeedInvoiceFrequencyTypeAsync("Monthly");

        // Act
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null, null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "no cadence anywhere is not a refusal");
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.InvoiceFrequencyTypeId.ShouldBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBeNull(
            "null means none set — a seeded Monthly existing in the table must not be substituted");
    }

    // ---------------------------------------------------------------- T109 — active types only

    [Fact]
    public async Task Post_WithARetiredOverride_IsRejected()
    {
        // Arrange — FR-038/AC-26: only ACTIVE types are selectable. The picker omits a retired one;
        // this is the server enforcing it, which is what makes the omission more than a convenience.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var retired = await SeedInvoiceFrequencyTypeAsync("Fortnightly", isActive: false);

        // Act
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null, retired),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.StatusCode.ShouldNotBe(
            HttpStatusCode.InternalServerError,
            "a retired id must be refused with a message, never reach the FK and surface as a 500");
    }

    [Fact]
    public async Task Post_WithAnOverrideThatDoesNotExist_IsRejected()
    {
        // Arrange — one question, not two: unknown and retired get the same answer, because the caller
        // can act on neither differently.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();

        // Act
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null, 999_999),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------- the update path, and the retired-value edit

    [Fact]
    public async Task Put_CanClearTheOverride_FallingBackToTheClientDefault()
    {
        // Arrange — clearing is a real operation: "bill this engagement the way the client does, after
        // all". It must resolve to the client default, not to none-set.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var weekly = await SeedInvoiceFrequencyTypeAsync("Weekly");
        var monthly = await SeedInvoiceFrequencyTypeAsync("Monthly");
        var clientId = await SeedClientAsync(invoiceFrequencyTypeId: weekly);
        var assignment = await CreateAssignmentAsync(ops, employeeId, clientId, monthly);

        // Act
        var cleared = await ops.PutAsJsonAsync(
            $"{AssignmentsUrl}/{assignment.Id}",
            new UpdateAssignmentRequest(new DateOnly(2026, 1, 1), null, null, null),
            Token);

        // Assert
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await cleared.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.InvoiceFrequencyTypeId.ShouldBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBe("Weekly", "cleared falls back to the client default");
    }

    /// <summary>
    /// An assignment whose override was retired AFTER it was chosen stays editable.
    /// </summary>
    /// <remarks>
    /// Not one of T106–T109's four cases, and included deliberately. FR-038 says only active types are
    /// selectable; keeping a value already stored is not selecting one. Without this, retiring a
    /// cadence would make every assignment carrying it un-editable — an edit to a date or a note would
    /// answer 400 on a field the user never touched. <c>004</c> US3 established exactly this rule for
    /// the CLIENT-level default against the same lookup table, so the two surfaces agree.
    /// </remarks>
    [Fact]
    public async Task Put_LeavingAnAlreadyRetiredOverrideAlone_IsAccepted()
    {
        // Arrange
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var cadence = await SeedInvoiceFrequencyTypeAsync("Fortnightly");
        var assignment = await CreateAssignmentAsync(ops, employeeId, clientId, cadence);

        await RetireInvoiceFrequencyTypeAsync(cadence);

        // Act — edit an unrelated field, keeping the now-retired override as it stands.
        var response = await ops.PutAsJsonAsync(
            $"{AssignmentsUrl}/{assignment.Id}",
            new UpdateAssignmentRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), "extended", cadence),
            Token);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "keeping a stored value is not selecting a retired one — otherwise retiring a cadence "
                + "makes every assignment carrying it un-editable");
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBe("Fortnightly");
    }

    [Fact]
    public async Task Put_ChangingToADifferentRetiredOverride_IsRejected()
    {
        // Arrange — the counterpart. Keeping a retired value is grandfathering; MOVING to one is a
        // fresh selection of something unselectable.
        await ResetDatabaseAsync();
        var ops = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var kept = await SeedInvoiceFrequencyTypeAsync("Fortnightly");
        var other = await SeedInvoiceFrequencyTypeAsync("Quarterly", isActive: false);
        var assignment = await CreateAssignmentAsync(ops, employeeId, clientId, kept);

        // Act
        var response = await ops.PutAsJsonAsync(
            $"{AssignmentsUrl}/{assignment.Id}",
            new UpdateAssignmentRequest(new DateOnly(2026, 1, 1), null, null, other),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ Helpers

    private async Task<AssignmentRowDto> CreateAssignmentAsync(
        HttpClient ops,
        int employeeId,
        int clientId,
        int? invoiceFrequencyTypeId)
    {
        var response = await ops.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(
                employeeId,
                clientId,
                new DateOnly(2026, 1, 1),
                null,
                null,
                invoiceFrequencyTypeId),
            Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        return dto;
    }

    private async Task<int> SeedEmployeeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<EmployeeType>().AnyAsync(Token))
        {
            db.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
            await db.SaveChangesAsync(Token);
        }

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada.{Guid.NewGuid():N}@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = 1,
            HireDate = new DateOnly(2020, 1, 6),
            IsActive = true,
        };
        db.Set<Employee>().Add(employee);
        await db.SaveChangesAsync(Token);
        return employee.Id;
    }

    private async Task<int> SeedClientAsync(int? invoiceFrequencyTypeId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var client = new Client
        {
            ClientName = $"Acme-{Guid.NewGuid():N}",
            IsInternal = false,
            InvoiceFrequencyTypeId = invoiceFrequencyTypeId,
        };
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);
        return client.Id;
    }

    private async Task<int> SeedInvoiceFrequencyTypeAsync(string typeName, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var type = new InvoiceFrequencyType { TypeName = typeName, IsActive = isActive };
        db.Set<InvoiceFrequencyType>().Add(type);
        await db.SaveChangesAsync(Token);
        return type.Id;
    }

    private async Task RetireInvoiceFrequencyTypeAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var type = await db.Set<InvoiceFrequencyType>().SingleAsync(t => t.Id == id, Token);
        type.IsActive = false;
        await db.SaveChangesAsync(Token);
    }
}
