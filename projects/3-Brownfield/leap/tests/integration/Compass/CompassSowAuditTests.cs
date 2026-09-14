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
/// US3/#66, FR-049 — every SOW create and edit writes an audit row with a non-blank reason and
/// <c>effective_roles</c> populated, mirroring <c>CompassAssignmentAuditTests</c> (data-model.md §5).
/// </summary>
public class CompassSowAuditTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string SowsUrl(int assignmentId) => $"/api/compass/assignments/{assignmentId}/sows";

    private async Task<int> SeedAssignmentAsync()
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

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2024, 4, 1),
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }

    private async Task<AuditLog> SingleAuditRowFor(string action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "CompassSow" && a.Action == action)
            .SingleAsync(Token);
    }

    [Fact]
    public async Task Create_WritesAnAuditRow_WithEffectiveRolesPopulated()
    {
        // Arrange
        await ResetDatabaseAsync();
        var httpClient = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var request = new CreateSowRequest(
            SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null);

        // Act
        var response = await httpClient.PostAsJsonAsync(SowsUrl(assignmentId), request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Assert
        var row = await SingleAuditRowFor("create");
        row.EntityType.ShouldBe("CompassSow");
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
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await httpClient.PostAsJsonAsync(
            SowsUrl(assignmentId),
            new CreateSowRequest(
                SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        var response = await httpClient.PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{created!.Id}",
            new UpdateSowRequest(
                SowType.InitialContract, RateIncrease: false, created.SowStartDate, created.SowEndDate, "Updated note"),
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
    public async Task Delete_WritesAnAuditRow_WithEffectiveRolesPopulated()
    {
        // Arrange — issue #593: a true delete is still an attributed Compass write.
        await ResetDatabaseAsync();
        var opsClient = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await opsClient.PostAsJsonAsync(
            SowsUrl(assignmentId),
            new CreateSowRequest(
                SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        var response = await _factory.AsCompassSuperAdmin()
            .DeleteAsync($"{SowsUrl(assignmentId)}/{created!.Id}", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Assert
        var row = await SingleAuditRowFor("delete");
        row.EntityId.ShouldBe(created.Id.ToString());
        row.Reason.ShouldNotBeNullOrWhiteSpace();
        row.EffectiveRoles.ShouldNotBeNull();
        row.EffectiveRoles!.ShouldContain(RolePolicy.CompassSuperAdminRole);
    }
}
