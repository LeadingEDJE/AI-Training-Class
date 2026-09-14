using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Tests.Auth;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Endpoint-level tests for the cookie-session impersonation routes. The default TestAuthHandler
/// principal is a SuperAdmin (EdjeId ...0001); the <c>X-Test-Impersonator</c> header injects
/// impersonation-provenance claims so the "already impersonating" and "stop restores original"
/// branches are exercised without a live cookie round-trip. The real cookie swap is proven by
/// ImpersonateEndpointsTests (integration).
/// </summary>
public class ImpersonateEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly Guid TargetEdjeId = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid AdminEdjeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly HttpClient _client = factory.CreateClient();

    private void SeedTarget()
    {
        var people = (InMemoryPersonRepository)factory.Services.GetRequiredService<IPersonRepository>();
        people.AddAsync(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = TargetEdjeId,
            FirstName = "Jane",
            LastName = "Target",
            Email = "jane.target@leadingedje.com",
            IsActive = true,
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Start_ValidTarget_ReturnsOkWithTargetMeResponseAndImpersonatorBlock()
    {
        // Arrange
        SeedTarget();

        // Act
        var response = await _client.PostAsync(
            $"/api/impersonate/{TargetEdjeId}", content: null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("edjeId").GetString().ShouldBe(TargetEdjeId.ToString());
        body.GetProperty("displayName").GetString().ShouldBe("Jane Target");
        body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ShouldContain("EDJEr");
        body.GetProperty("impersonator").GetProperty("edjeId").GetString().ShouldBe(AdminEdjeId.ToString());
        body.TryGetProperty("token", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Start_UnknownTarget_ReturnsNotFound()
    {
        // Arrange — a target EdjeId with no matching person row.
        var unknown = Guid.Parse("99999999-9999-9999-9999-999999999999");

        // Act
        var response = await _client.PostAsync(
            $"/api/impersonate/{unknown}", content: null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_WhileAlreadyImpersonating_ReturnsConflict()
    {
        // Arrange
        SeedTarget();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/impersonate/{TargetEdjeId}");
        request.Headers.Add(TestAuthHandler.ImpersonatorOverrideHeader, AdminEdjeId.ToString());

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Start_AsNonSuperAdmin_Returns403()
    {
        // Arrange — a request scoped to EDJEr only (no SuperAdmin).
        SeedTarget();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/impersonate/{TargetEdjeId}");
        request.Headers.Add(TestAuthHandler.PrivilegeOverrideHeader, "EDJEr");

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stop_WhileImpersonating_ReturnsOkWithRestoredIdentity()
    {
        // Arrange — provenance claims present (impersonating).
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/impersonate/stop");
        request.Headers.Add(TestAuthHandler.ImpersonatorOverrideHeader, AdminEdjeId.ToString());

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert — original identity restored, impersonator cleared.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("edjeId").GetString().ShouldBe(AdminEdjeId.ToString());
        body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ShouldContain("SuperAdmin");
        body.GetProperty("impersonator").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Stop_WhenNotImpersonating_ReturnsBadRequest()
    {
        // Act — default principal carries no impersonation provenance.
        var response = await _client.PostAsync(
            "/api/impersonate/stop", content: null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
