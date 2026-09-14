using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Endpoint tests for invoice-frequency-type administration.
/// </summary>
/// <remarks>
/// The two lookups share a service, so these are not a copy of the employee-type suite for its own
/// sake: they prove the second surface is actually wired to its own table and its own name space. The
/// last test here is the one that would catch both routes being pointed at the same lookup.
/// </remarks>
public class CompassAdminInvoiceFrequencyTypeEndpointsTests
{
    private const string Route = "/api/compass/v1/admin/invoice-frequency-types";
    private const string EmployeeTypeRoute = "/api/compass/v1/admin/employee-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task GetAll_ReturnsTheLookupCollection()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<InvoiceFrequencyTypeDto>>(Token);
        types.ShouldNotBeNull();
    }

    [Fact]
    public async Task Post_WithANewName_CreatesItActive()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var name = UniqueName("Fortnightly");

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest(name),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<InvoiceFrequencyTypeDto>(Token);
        created!.TypeName.ShouldBe(name);
        created.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Post_WithADuplicateName_Returns409()
    {
        // Arrange — FR-006.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var name = UniqueName("Fortnightly");
        await client.PostAsJsonAsync(Route, new CreateCompassLookupRequest(name), Token);

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest(name),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_TogglesTheValueInactive()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var name = UniqueName("Fortnightly");
        var created = (
            await (
                await client.PostAsJsonAsync(Route, new CreateCompassLookupRequest(name), Token)
            ).Content.ReadFromJsonAsync<InvoiceFrequencyTypeDto>(Token)
        )!;

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            new UpdateCompassLookupRequest(name, IsActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<InvoiceFrequencyTypeDto>(Token);
        updated!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Put_ForAnUnknownId_Returns404()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/424242",
            new UpdateCompassLookupRequest("Monthly", IsActive: true),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheTwoLookups_AreIndependentNameSpaces()
    {
        // Arrange — the load-bearing test of this file. One service serves both lookups, so the
        // failure to guard against is both routes reading and writing the SAME table: a name taken as
        // an employee type would then collide here, and the two collections would agree.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var sharedName = UniqueName("Shared");
        var asEmployeeType = await client.PostAsJsonAsync(
            EmployeeTypeRoute,
            new CreateCompassLookupRequest(sharedName),
            Token
        );
        asEmployeeType.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act — the same name, as the other lookup.
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest(sharedName),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            "employee types and invoice frequency types are separate name spaces"
        );
        var frequencies = await (await client.GetAsync(Route, Token)).Content.ReadFromJsonAsync<
            List<InvoiceFrequencyTypeDto>
        >(Token);
        var employeeTypes = await (
            await client.GetAsync(EmployeeTypeRoute, Token)
        ).Content.ReadFromJsonAsync<List<EmployeeTypeDto>>(Token);
        frequencies!.Count(frequency => frequency.TypeName == sharedName)
            .ShouldBe(1, "the invoice-frequency collection holds only what was written to it");
        employeeTypes!.Count(employeeType => employeeType.TypeName == sharedName)
            .ShouldBe(1, "and likewise the employee-type collection");
    }
}
