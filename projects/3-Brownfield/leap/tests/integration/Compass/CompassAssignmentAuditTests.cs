using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// US1/#62, FR-049 and SC-009 — every create, update and end of a <see cref="ClientAssignment"/> writes
/// an audit row with a non-blank reason and <c>effective_roles</c> POPULATED, not null.
/// </summary>
/// <remarks>
/// This is Compass's first audited write path (data-model.md §5), and the first writer of
/// <c>EffectiveRoles</c> anywhere. Populated, not merely present, matters: <c>null</c> means "not
/// captured" (every Timesheet/OOTO write), and conflating the two would hide the exact regression this
/// feature exists to prevent.
/// </remarks>
public class CompassAssignmentAuditTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    private const string AssignmentsUrl = "/api/compass/assignments";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(int EmployeeId, int ClientId)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);
        return (employee.Id, client.Id);
    }

    private async Task<AuditLog> SingleAuditRowFor(string action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "CompassClientAssignment" && a.Action == action)
            .SingleAsync(Token);
    }

    [Fact]
    public async Task Create_WritesAnAuditRow_WithEffectiveRolesPopulated()
    {
        // Arrange
        await ResetDatabaseAsync();
        var httpClient = _factory.AsCompassOps();
        var (employeeId, clientId) = await SeedAsync();

        // Act
        var response = await httpClient.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Assert
        var row = await SingleAuditRowFor("create");
        row.EntityType.ShouldBe("CompassClientAssignment");
        row.Reason.ShouldNotBeNullOrWhiteSpace();
        row.EffectiveRoles.ShouldNotBeNull("effective_roles must be CAPTURED for a Compass write, not left null");
        row.EffectiveRoles!.ShouldContain(RolePolicy.CompassOpsRole);
    }

    [Fact]
    public async Task Update_WritesAnAuditRow_WithEffectiveRolesPopulated()
    {
        // Arrange
        await ResetDatabaseAsync();
        var httpClient = _factory.AsCompassOps();
        var (employeeId, clientId) = await SeedAsync();
        var created = await (await httpClient.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await httpClient.PutAsJsonAsync(
            $"{AssignmentsUrl}/{created!.Id}",
            new UpdateAssignmentRequest(created.StartDate, null, "Updated note"),
            Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        var row = await SingleAuditRowFor("update");
        row.EntityId.ShouldBe(created.Id.ToString());
        row.Reason.ShouldNotBeNullOrWhiteSpace();
        row.EffectiveRoles.ShouldNotBeNull();
        row.EffectiveRoles!.ShouldContain(RolePolicy.CompassOpsRole);
    }

    [Fact]
    public async Task EndDating_WritesAnAuditRow_WithActionEnd_AndEffectiveRolesPopulated()
    {
        // Arrange — the FIRST NULL-to-value transition on EndDate is the "end" action (J14 step 5).
        await ResetDatabaseAsync();
        var httpClient = _factory.AsCompassOps();
        var (employeeId, clientId) = await SeedAsync();
        var created = await (await httpClient.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await httpClient.PutAsJsonAsync(
            $"{AssignmentsUrl}/{created!.Id}",
            new UpdateAssignmentRequest(created.StartDate, new DateOnly(2026, 6, 30), null),
            Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        var row = await SingleAuditRowFor("end");
        row.EntityId.ShouldBe(created.Id.ToString());
        row.Reason.ShouldNotBeNullOrWhiteSpace();
        row.EffectiveRoles.ShouldNotBeNull();
    }

    [Fact]
    public async Task Delete_WritesAnAuditRow_WithEffectiveRolesPopulated()
    {
        // Arrange — issue #593: a true delete is still an attributed Compass write.
        await ResetDatabaseAsync();
        var opsClient = _factory.AsCompassOps();
        var (employeeId, clientId) = await SeedAsync();
        var created = await (await opsClient.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await _factory.AsCompassSuperAdmin()
            .DeleteAsync($"{AssignmentsUrl}/{created!.Id}", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Assert
        var row = await SingleAuditRowFor("delete");
        row.EntityId.ShouldBe(created.Id.ToString());
        row.Reason.ShouldNotBeNullOrWhiteSpace();
        row.EffectiveRoles.ShouldNotBeNull("effective_roles must be CAPTURED for a Compass write, not left null");
        row.EffectiveRoles!.ShouldContain(RolePolicy.CompassSuperAdminRole);
    }
}
