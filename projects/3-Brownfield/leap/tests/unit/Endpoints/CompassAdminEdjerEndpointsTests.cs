using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Endpoint tests for EDJEr configuration over the real request pipeline.
/// </summary>
/// <remarks>
/// Every request acts as the Compass root. Refusal of lesser roles is
/// <see cref="CompassAdminEdjerAuthorizationTests"/>'s subject, kept separate so a failure there cannot
/// be mistaken for a behaviour failure here.
/// <para>
/// These run on the in-memory provider, so they assert the pipeline, the status codes and the payload
/// shapes. What the provider structurally cannot assert — SQL <c>lower()</c>, the real unique index, real
/// foreign keys, audit rows — is
/// <c>tests/integration/Endpoints/CompassAdminEdjerEndpointsTests</c>'s.
/// </para>
/// </remarks>
public class CompassAdminEdjerEndpointsTests
{
    private const string Route = "/api/compass/v1/admin/edjers";
    private const string EmployeeTypes = "/api/compass/v1/admin/employee-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueEmail() => $"edjer.{Guid.NewGuid():N}@example.test";

    private static CompassEdjerRequest Request(
        int employeeTypeId,
        string? email = null,
        bool isActive = true,
        int? coachEmployeeId = null,
        string firstName = "Ada",
        string lastName = "Lovelace",
        string state = "OH"
    ) =>
        new(
            firstName,
            lastName,
            new DateOnly(2020, 1, 6),
            email ?? UniqueEmail(),
            employeeTypeId,
            coachEmployeeId,
            state,
            isActive,
            TimesheetRequired: true,
            CanSubmitUnder40: false,
            IncludeInPayroll: true
        );

    /// <summary>
    /// Creates an employee type and returns its id, so an EDJEr has an ACTIVE classification to name.
    /// </summary>
    /// <remarks>
    /// Arranged through the lookup API rather than by reaching into the context: this is the same path
    /// the administrator takes, and it asserts its own arrangement succeeded rather than failing later in
    /// a way that reads as an EDJEr defect.
    /// </remarks>
    private static async Task<int> CreateEmployeeTypeAsync(HttpClient client, bool active = true)
    {
        var name = $"Type-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync(
            EmployeeTypes,
            new CreateCompassLookupRequest(name),
            Token
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<EmployeeTypeDto>(Token);
        created.ShouldNotBeNull();

        if (!active)
        {
            var retired = await client.PutAsJsonAsync(
                $"{EmployeeTypes}/{created.Id}",
                new UpdateCompassLookupRequest(created.TypeName, IsActive: false),
                Token
            );
            retired.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return created.Id;
    }

    [Fact]
    public async Task GetAll_ReturnsTheEdjerCollection()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edjers = await response.Content.ReadFromJsonAsync<List<CompassEdjerSummaryDto>>(Token);
        edjers.ShouldNotBeNull();
    }

    [Fact]
    public async Task Post_WithAValidRequest_CreatesItAndReturnsItsLocation()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(employeeTypeId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var created = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        created.ShouldNotBeNull();
        created.Id.ShouldBeGreaterThan(0);
        created.FirstName.ShouldBe("Ada");
        created.IsActive.ShouldBeTrue();
        response.Headers.Location!.ToString().ShouldEndWith(created.Id.ToString());
    }

    [Fact]
    public async Task GetById_ForACreatedEdjer_ReturnsTheFullRecord()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // Act
        var response = await client.GetAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        fetched.ShouldNotBeNull();
        fetched.Email.ShouldBe(created.Email);
        fetched.TimesheetRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task GetById_ForAnUnknownId_Returns404()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync($"{Route}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_WithADuplicateEmail_Returns409()
    {
        // Arrange — BR-9, FR-012.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);
        var email = UniqueEmail();
        await CreateEdjerAsync(client, Request(employeeTypeId, email));

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(employeeTypeId, email), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync(Token);
        body.ShouldContain("email", Case.Insensitive);
    }

    [Fact]
    public async Task Post_NamingAnInactiveEmployeeType_Returns400()
    {
        // Arrange — FR-005: the server refuses it even though the selection list omits it.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var retiredTypeId = await CreateEmployeeTypeAsync(client, active: false);

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(retiredTypeId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WithAStateOutsideTheFiftyPlusDC_Returns400()
    {
        // Arrange — FR-014.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, state: "ZZ"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_WithAValidRequest_UpdatesTheRecord()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(employeeTypeId, created.Email, firstName: "Augusta"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        updated.ShouldNotBeNull();
        updated.FirstName.ShouldBe("Augusta");
    }

    [Fact]
    public async Task Put_ForAnUnknownId_Returns404()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/999999",
            Request(employeeTypeId),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("", "blank")]
    [InlineData("   ", "whitespace only")]
    [InlineData("not-an-address", "no @ at all")]
    public async Task Post_WithAnUnusableEmail_Returns400(string email, string because)
    {
        // FR-010 at the HTTP level, where it was previously only asserted in the service. An email the
        // server cannot use is a 400 at the boundary, not something the service alone is trusted to say.
        //
        // These arrived alongside the email-masking autofix (since reverted) to cover its "[redacted]"
        // fallback. They outlive it: the masking is gone and these still assert behaviour the endpoint
        // suite otherwise never exercised, since every other rejected write here carries a well-formed
        // address.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, email),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task Put_NamingTheEdjerAsTheirOwnCoach_Returns400()
    {
        // Driven through HTTP rather than the service, because that is what the finding is about: the
        // form omits the EDJEr from its own coach picker, and FR-041 makes that filter a convenience
        // rather than the control. A caller bypassing the interface has to be refused by the route.
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(employeeTypeId, created.Email, coachEmployeeId: created.Id),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // And the stored record is untouched — a refused write changes nothing.
        var reread = await (
            await client.GetAsync($"{Route}/{created.Id}", Token)
        ).Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        reread!.CoachEmployeeId.ShouldBeNull();
    }

    /// <summary>
    /// The 422 the deactivation guard answers, at the ENDPOINT level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard's behaviour is <c>CompassEmployeeServiceTests</c>' and its real-database form is the
    /// integration suite's. What only this test covers is the mapping: that
    /// <c>CompassWriteStatus.PreconditionFailed</c> becomes a 422 carrying the blocking assignments, rather
    /// than falling through to the 400 arm.
    /// </para>
    /// <para>
    /// It exists because a per-file coverage gate found the hole.
    /// <c>scripts/check-coverage.sh</c> runs <c>tests/unit</c> only, so the 422 arm of
    /// <c>CompassWriteResults.ToErrorResult</c> — exercised solely by the integration suite — read as
    /// uncovered at 66.7% on <c>CompassWrite.cs</c>. Covering it here rather than relaxing the gate is the
    /// standing rule, and the assertion turns out to be worth having on its own: a 422 silently degrading
    /// to 400 would turn an actionable refusal into a corrective one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Put_DeactivatingAnEdjerWithAnOpenAssignment_Returns422WithTheBlockers()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var employeeTypeId = await CreateEmployeeTypeAsync(client);
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // An assignment with no end date, inserted directly: assignment management is Stream 3's surface
        // (spec A-6), so there is no endpoint to arrange it through.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
            var compassClient = new Client { ClientName = $"Client-{Guid.NewGuid():N}"[..20], IsInternal = false };
            db.Set<Client>().Add(compassClient);
            await db.SaveChangesAsync(Token);

            db.Set<ClientAssignment>()
                .Add(new ClientAssignment
                {
                    EmployeeId = created.Id,
                    ClientId = compassClient.Id,
                    StartDate = new DateOnly(2024, 4, 1),
                    EndDate = null,
                });
            await db.SaveChangesAsync(Token);
        }

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(employeeTypeId, created.Email, isActive: false),
            Token
        );

        // Assert — 422, and NOT 400: the request is well formed and the caller authorised.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.StatusCode.ShouldNotBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(Token);
        body.ShouldContain(
            "blockingAssignments",
            Case.Insensitive,
            "AC-19 requires the refusal to identify what must be end-dated first"
        );
    }

    /// <summary>
    /// There is no <c>DELETE</c>, and its absence is the requirement.
    /// </summary>
    /// <remarks>
    /// An EDJEr is deactivated through the active flag, guarded by FR-019. A path matching a route
    /// template for another verb answers 405, which is the honest answer.
    /// </remarks>
    [Fact]
    public async Task Delete_IsNotOffered()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.DeleteAsync($"{Route}/1", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    private static async Task<CompassEdjerDto> CreateEdjerAsync(
        HttpClient client,
        CompassEdjerRequest request
    )
    {
        var response = await client.PostAsJsonAsync(Route, request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        created.ShouldNotBeNull();
        return created;
    }
}
