using System.Net;
using System.Net.Http.Json;
using System.Text;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Endpoint tests for client configuration over the real request pipeline.
/// </summary>
/// <remarks>
/// Every request acts as the Compass root. Refusal of lesser roles is
/// <see cref="CompassAdminClientAuthorizationTests"/>'s subject, kept separate so a failure there cannot
/// be mistaken for a behaviour failure here.
/// <para>
/// These run on the in-memory provider, so they assert the pipeline, the status codes and the payload
/// shapes. What the provider structurally cannot assert — the real composite unique index, real foreign
/// keys, audit rows — is <c>tests/integration/Endpoints/CompassAdminClientEndpointsTests</c>'s.
/// </para>
/// </remarks>
public class CompassAdminClientEndpointsTests
{
    private const string Route = "/api/compass/v1/admin/clients";
    private const string InvoiceFrequencyTypes = "/api/compass/v1/admin/invoice-frequency-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueName() => $"Client-{Guid.NewGuid():N}"[..24];

    private static CompassClientRequest Request(
        string? clientName = null,
        int? invoiceFrequencyTypeId = null,
        bool isInternal = false,
        DateOnly? msaSignedDate = null,
        DateOnly? ndaSignedDate = null
    ) =>
        new(
            clientName ?? UniqueName(),
            msaSignedDate,
            ndaSignedDate,
            isInternal,
            invoiceFrequencyTypeId
        );

    /// <summary>
    /// Creates an invoice frequency type and returns its id, so a client has an ACTIVE cadence to name.
    /// </summary>
    /// <remarks>
    /// Arranged through the lookup API rather than by reaching into the context: this is the same path
    /// the administrator takes, and it asserts its own arrangement succeeded rather than failing later in
    /// a way that reads as a client defect.
    /// </remarks>
    private static async Task<int> CreateInvoiceFrequencyTypeAsync(
        HttpClient client,
        bool active = true
    )
    {
        var name = $"Freq-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync(
            InvoiceFrequencyTypes,
            new CreateCompassLookupRequest(name),
            Token
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<InvoiceFrequencyTypeDto>(Token);
        created.ShouldNotBeNull();

        if (!active)
        {
            var retired = await client.PutAsJsonAsync(
                $"{InvoiceFrequencyTypes}/{created.Id}",
                new UpdateCompassLookupRequest(created.TypeName, IsActive: false),
                Token
            );
            retired.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return created.Id;
    }

    private static async Task<ClientDto> CreateClientAsync(
        HttpClient client,
        CompassClientRequest? request = null
    )
    {
        var response = await client.PostAsJsonAsync(Route, request ?? Request(), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        created.ShouldNotBeNull();
        return created;
    }

    // ------------------------------------------------------------------------------------ reads

    [Fact]
    public async Task GetAll_ReturnsTheClientCollection()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clients = await response.Content.ReadFromJsonAsync<List<ClientSummaryDto>>(Token);
        clients.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetById_WhenTheClientExists_ReturnsItWithItsCategories()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);
        var added = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );
        added.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var response = await client.GetAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        fetched.ShouldNotBeNull();
        fetched.Id.ShouldBe(created.Id);
        fetched
            .BillableTimeCategories.Select(category => category.CategoryName)
            .ShouldContain("Development");
    }

    [Fact]
    public async Task GetById_WhenTheClientDoesNotExist_IsNotFound()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync($"{Route}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------------------------- writes

    [Fact]
    public async Task Post_WithAValidRequest_CreatesItAndReturnsItsLocation()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var frequencyId = await CreateInvoiceFrequencyTypeAsync(client);
        var request = Request(invoiceFrequencyTypeId: frequencyId, isInternal: true);

        // Act
        var response = await client.PostAsJsonAsync(Route, request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var created = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        created.ShouldNotBeNull();
        created.ClientName.ShouldBe(request.ClientName);
        created.IsInternal.ShouldBeTrue();
        created.InvoiceFrequencyTypeId.ShouldBe(frequencyId);
        response.Headers.Location.ToString().ShouldEndWith($"{Route}/{created.Id}");
    }

    [Fact]
    public async Task Post_WithADuplicateName_IsAConflict()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var name = UniqueName();
        await CreateClientAsync(client, Request(clientName: name));

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(clientName: name), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_NamingAnInactiveInvoiceFrequencyType_IsABadRequest()
    {
        // Arrange — FR-023, enforced at the server rather than by the select omitting the option.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var retiredId = await CreateInvoiceFrequencyTypeAsync(client, active: false);

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(invoiceFrequencyTypeId: retiredId),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_WithAValidRequest_UpdatesTheClient()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(
                clientName: created.ClientName,
                isInternal: true,
                msaSignedDate: new DateOnly(2024, 3, 1)
            ),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        updated.ShouldNotBeNull();
        updated.IsInternal.ShouldBeTrue();
        updated.MsaSignedDate.ShouldBe(new DateOnly(2024, 3, 1));
    }

    [Fact]
    public async Task Put_WhenTheClientDoesNotExist_IsNotFound()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PutAsJsonAsync($"{Route}/999999", Request(), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostCategory_ThenPutToDeactivateIt_BothSucceed()
    {
        // Arrange — AC-23's edit path for categories, end to end over the pipeline.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        // Act
        var added = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );
        var category = await added.Content.ReadFromJsonAsync<BillableTimeCategoryDto>(Token);
        category.ShouldNotBeNull();

        var deactivated = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories/{category.Id}",
            new UpdateBillableTimeCategoryRequest("Development", IsActive: false),
            Token
        );

        // Assert
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await deactivated.Content.ReadFromJsonAsync<BillableTimeCategoryDto>(Token);
        updated.ShouldNotBeNull();
        updated.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task PostCategory_WithADuplicateNameOnTheSameClient_IsAConflict()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);
        var first = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var response = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// The absence of a DELETE is the requirement, not an omission (AC-23, Principle VIII).
    /// </summary>
    [Fact]
    public async Task Delete_IsNotRouted_ForClientsOrCategories()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        // Act
        var clientDelete = await client.DeleteAsync($"{Route}/{created.Id}", Token);
        var categoryDelete = await client.DeleteAsync(
            $"{Route}/{created.Id}/billable-time-categories/1",
            Token
        );

        // Assert
        clientDelete.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        categoryDelete.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    // ----------------------------------- FR-021: status is reported outward, and never accepted inward

    /// <summary>
    /// The client contract carries no stored status, on the way out.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection so that adding a member fails here rather than silently shipping a second,
    /// stored answer to a question that is only ever derived (FR-021, FR-035). US4 adds a
    /// <c>Status</c> member that is COMPUTED per request; this test is scoped to the stored flag
    /// vocabulary — <c>IsActive</c> and friends — which never becomes correct to add.
    /// </remarks>
    [Theory]
    [InlineData("IsActive")]
    [InlineData("Active")]
    [InlineData("IsInactive")]
    public void TheClientContract_CarriesNoStoredStatusMember(string forbidden)
    {
        // Arrange / Act
        var outbound = typeof(ClientDto).GetProperties().Select(property => property.Name).ToList();
        var summary = typeof(ClientSummaryDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();
        var inbound = typeof(CompassClientRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        // Assert
        outbound.ShouldNotContain(forbidden);
        summary.ShouldNotContain(forbidden);
        inbound.ShouldNotContain(forbidden);
    }

    /// <summary>
    /// FR-021 on the way IN, and the half that is easy to get wrong.
    /// </summary>
    /// <remarks>
    /// The requirement is that such a request is REJECTED, not ignored. System.Text.Json's default
    /// is to discard members it does not recognise, which would answer 201 to a caller who believes they
    /// just set a client's status — the worst of the three possible outcomes, because it is silent. The
    /// request record therefore opts into <c>JsonUnmappedMemberHandling.Disallow</c>.
    /// </remarks>
    [Theory]
    [InlineData("isActive")]
    [InlineData("status")]
    public async Task Post_CarryingAStatusMember_IsRejectedRatherThanIgnored(string member)
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var payload = $$"""
            {
              "clientName": "{{UniqueName()}}",
              "msaSignedDate": null,
              "ndaSignedDate": null,
              "isInternal": false,
              "invoiceFrequencyTypeId": null,
              "{{member}}": true
            }
            """;

        // Act
        var response = await client.PostAsync(
            Route,
            new StringContent(payload, Encoding.UTF8, "application/json"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            "a request that tries to set status must be refused, never silently accepted"
        );
        response.StatusCode.ShouldNotBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Put_CarryingAStatusMember_IsRejectedRatherThanIgnored()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);
        var payload = $$"""
            {
              "clientName": "{{created.ClientName}}",
              "msaSignedDate": null,
              "ndaSignedDate": null,
              "isInternal": true,
              "invoiceFrequencyTypeId": null,
              "isActive": false
            }
            """;

        // Act
        var response = await client.PutAsync(
            $"{Route}/{created.Id}",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The positive control for the two tests above.
    /// </summary>
    /// <remarks>
    /// Without it, a 400 on every POST — a broken route, a serializer misconfiguration — would make them
    /// pass while proving nothing. This is the shape this repository has been bitten by.
    /// </remarks>
    [Fact]
    public async Task Post_CarryingOnlyKnownMembers_IsAccepted()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var payload = $$"""
            {
              "clientName": "{{UniqueName()}}",
              "msaSignedDate": null,
              "ndaSignedDate": null,
              "isInternal": false,
              "invoiceFrequencyTypeId": null
            }
            """;

        // Act
        var response = await client.PostAsync(
            Route,
            new StringContent(payload, Encoding.UTF8, "application/json"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// FR-022 / FR-037: a brand-new client has zero assignments, and nothing about that limits it.
    /// </summary>
    [Fact]
    public async Task AZeroAssignmentClient_IsStillFullyEditable()
    {
        // Arrange — under US4's derivation a zero-assignment client is Inactive, and FR-037 requires that
        // to disable nothing. Asserted here rather than in US4 because the editability is THIS story's
        // surface; the derivation that makes it Inactive is that one's.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        // Act
        var edited = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(clientName: created.ClientName, isInternal: true),
            Token
        );
        var categoryAdded = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        categoryAdded.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetAll_WithClients_CarriesTheCadenceName_AndNullForAClientWithout()
    {
        // Arrange — the list read's projection only runs when there ARE rows, so the empty-collection
        // test above proves the route and nothing about what a row looks like. The second client is the
        // case the LEFT join exists for.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var frequencyId = await CreateInvoiceFrequencyTypeAsync(client);

        var withCadence = await CreateClientAsync(
            client,
            Request(invoiceFrequencyTypeId: frequencyId, isInternal: true)
        );
        var withoutCadence = await CreateClientAsync(client, Request());

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clients = await response.Content.ReadFromJsonAsync<List<ClientSummaryDto>>(Token);
        clients.ShouldNotBeNull();

        var first = clients.Single(row => row.Id == withCadence.Id);
        first.ClientName.ShouldBe(withCadence.ClientName);
        first.IsInternal.ShouldBeTrue();
        first.InvoiceFrequencyTypeName.ShouldNotBeNullOrWhiteSpace();

        clients
            .Single(row => row.Id == withoutCadence.Id)
            .InvoiceFrequencyTypeName.ShouldBeNull(
                "a client with no default must still be listed, which an inner join would break"
            );
    }

    [Fact]
    public async Task PutCategory_WithASiblingsName_IsAConflict()
    {
        // Arrange — the category UPDATE route's rejection arm. Its create counterpart is asserted
        // above; this is the same refusal arriving through the other verb.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        var first = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Support"),
            Token
        );
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var support = await second.Content.ReadFromJsonAsync<BillableTimeCategoryDto>(Token);
        support.ShouldNotBeNull();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories/{support.Id}",
            new UpdateBillableTimeCategoryRequest("Development", IsActive: true),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PutCategory_OnAClientThatDoesNotOwnIt_IsNotFound()
    {
        // Arrange — the client id is part of the server's lookup, not a hint, so guessing a category id
        // cannot reach another client's configuration.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var owner = await CreateClientAsync(client);
        var stranger = await CreateClientAsync(client);

        var added = await client.PostAsJsonAsync(
            $"{Route}/{owner.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var category = await added.Content.ReadFromJsonAsync<BillableTimeCategoryDto>(Token);
        category.ShouldNotBeNull();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{stranger.Id}/billable-time-categories/{category.Id}",
            new UpdateBillableTimeCategoryRequest("Renamed", IsActive: true),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------ US4 T101: derived status on the payload

    [Fact]
    public async Task GetAll_CarriesDerivedStatus_AndABrandNewClientReadsInactive()
    {
        // Arrange — FR-034: a client with zero assignments is Inactive, which every client created
        // through this surface is until Stream 3 can assign anyone to it.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clients = await response.Content.ReadFromJsonAsync<List<ClientSummaryDto>>(Token);
        clients.ShouldNotBeNull();
        clients.Single(row => row.Id == created.Id).Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task GetById_CarriesDerivedStatus()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client);

        // Act
        var response = await client.GetAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        fetched.ShouldNotBeNull();
        fetched.Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task Status_AppearsOnTheWireAsTheSameWordsTheReadSurfacePublishes()
    {
        // Arrange — the wire format is what every consumer reads. Two Compass surfaces now publish a
        // client's status, and a reader that handled "Active" from one and 0 from the other would be
        // a defect nobody notices until a screen renders a number.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        await CreateClientAsync(client);

        // Act
        var payload = await client.GetStringAsync(Route, Token);

        // Assert
        payload.ShouldContain("\"status\":\"Inactive\"");
    }

    /// <summary>
    /// FR-021 still holds after US4: status is REPORTED, never accepted.
    /// </summary>
    /// <remarks>
    /// The obvious regression once a member called <c>status</c> exists on the response is to let it in
    /// on the request too. `CompassClientRequest` opts into <c>JsonUnmappedMemberHandling.Disallow</c>,
    /// so this is a 400 rather than a silently discarded field — and that has to keep being true for
    /// the new member name specifically, not just the old flag-shaped ones.
    /// </remarks>
    [Theory]
    [InlineData("status")]
    [InlineData("Status")]
    public async Task Post_CarryingTheNewStatusMember_IsStillRejected(string member)
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var payload = $$"""
            {
              "clientName": "{{UniqueName()}}",
              "msaSignedDate": null,
              "ndaSignedDate": null,
              "isInternal": false,
              "invoiceFrequencyTypeId": null,
              "{{member}}": "Active"
            }
            """;

        // Act
        var response = await client.PostAsync(
            Route,
            new StringContent(payload, Encoding.UTF8, "application/json"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Status is published as a STRING, and that is fenced rather than stylistic.
    /// </summary>
    /// <remarks>
    /// This assertion was written the other way round first — the enum looked better typed. It is
    /// wrong: <c>ClientStatusNonGatingTests.NoProductionTypeAnywhere_HoldsADerivedStatus</c> fails the
    /// build if any production member holds a <c>ClientStatus</c>, because a held value can outlive
    /// the assignments it summarises (FR-035), and that gate's own message says the read DTOs expose a
    /// string "precisely so nothing can hold the enum". Pinned here so the two surfaces cannot drift
    /// apart on the wire format either.
    /// </remarks>
    [Fact]
    public void TheClientContract_PublishesStatusAsAString_SoNothingHoldsTheEnum()
    {
        // Arrange / Act
        var outbound = typeof(ClientDto).GetProperty(nameof(ClientDto.Status));
        var summary = typeof(ClientSummaryDto).GetProperty(nameof(ClientSummaryDto.Status));

        // Assert
        outbound.ShouldNotBeNull();
        outbound.PropertyType.ShouldBe(typeof(string));
        summary.ShouldNotBeNull();
        summary.PropertyType.ShouldBe(typeof(string));
    }
}
