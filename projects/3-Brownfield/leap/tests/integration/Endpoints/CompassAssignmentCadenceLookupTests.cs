using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// US6/#64 — the invoice-frequency cadence list the assignment screens read to populate the override
/// selector.
/// </summary>
/// <remarks>
/// <para>
/// Why this endpoint exists at all. The only route serving these lookups was
/// <c>GET /api/compass/v1/admin/invoice-frequency-types</c>, gated by
/// <see cref="Platform.Authorization.RolePolicy.CompassSuperAdmin"/>. Setting an assignment's override
/// requires only <see cref="Platform.Authorization.RolePolicy.CompassOps"/>, so a Compass Ops user was
/// authorized to choose a cadence but forbidden from LISTING the cadences to choose from — the fetch
/// 403'd and the selector silently degraded to offering "use the client default" alone. Silently is
/// the operative word: a 403 on a lookup reads as an empty list, not as an error, so the screen looked
/// like a client with no cadences configured rather than a permissions fault.
/// </para>
/// <para>
/// Gated at <see cref="Platform.Authorization.RolePolicy.CompassElevated"/>, not
/// <c>CompassOps</c>. Elevated is the tier that can REACH the assignment detail screen at all
/// (AC-16/FR-025 — the same policy on <c>GET /assignments/{id}</c>), and it already contains the Ops
/// role. Compass Admin and Sales viewers need the list too: the read-only view names the stored
/// override from it, so without the list an active cadence renders as "Retired cadence" — an assertion
/// about the engagement that is simply false.
/// </para>
/// <para>
/// The admin route is deliberately left alone. It is the CONFIGURATION surface — it creates and
/// updates cadences, and gating that at the Compass root is correct. This is a read.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAssignmentCadenceLookupTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAssignmentCadenceLookupTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string CadencesUrl = "/api/compass/assignments/pickers/invoice-frequency-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(int ActiveId, int RetiredId)> SeedCadencesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var active = new InvoiceFrequencyType { TypeName = $"Weekly-{Guid.NewGuid():N}", IsActive = true };
        var retired = new InvoiceFrequencyType { TypeName = $"Fortnightly-{Guid.NewGuid():N}", IsActive = false };
        db.Set<InvoiceFrequencyType>().AddRange(active, retired);
        await db.SaveChangesAsync(Token);
        return (active.Id, retired.Id);
    }

    [Fact]
    public async Task ACompassOpsUser_CanListTheCadences_TheyAreAuthorizedToChooseFrom()
    {
        // Arrange -- the regression this endpoint exists for. Ops may set an override (FR-036), so Ops
        // must be able to read the list; anything else is an authorization surface that contradicts
        // itself.
        await ResetDatabaseAsync();
        var (activeId, _) = await SeedCadencesAsync();

        // Act
        var response = await _factory.AsCompassOps().GetAsync($"{CadencesUrl}?activeOnly=true", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InvoiceFrequencyTypeDto>>(Token);
        rows!.ShouldContain(r => r.Id == activeId);
    }

    [Fact]
    public async Task ActiveOnly_ExcludesARetiredCadence_SoItIsNeverOfferedAsANewChoice()
    {
        // Arrange -- FR-038. The server refuses a retired id regardless; this keeps the UI from
        // offering one in the first place.
        await ResetDatabaseAsync();
        var (activeId, retiredId) = await SeedCadencesAsync();

        // Act
        var response = await _factory.AsCompassOps().GetAsync($"{CadencesUrl}?activeOnly=true", Token);
        var rows = await response.Content.ReadFromJsonAsync<List<InvoiceFrequencyTypeDto>>(Token);

        // Assert
        rows!.ShouldContain(r => r.Id == activeId);
        rows!.ShouldNotContain(r => r.Id == retiredId);
    }

    [Fact]
    public async Task WithoutActiveOnly_TheRetiredCadenceIsStillListed()
    {
        // Arrange -- the read-only view names a STORED override, which may since have been retired.
        // Dropping it from every response would make that view unable to name it at all.
        await ResetDatabaseAsync();
        var (_, retiredId) = await SeedCadencesAsync();

        // Act
        var response = await _factory.AsCompassOps().GetAsync(CadencesUrl, Token);
        var rows = await response.Content.ReadFromJsonAsync<List<InvoiceFrequencyTypeDto>>(Token);

        // Assert
        rows!.ShouldContain(r => r.Id == retiredId);
    }

    [Fact]
    public async Task ACompassSalesUser_CanListTheCadences_BecauseTheReadOnlyViewNamesTheOverride()
    {
        // Arrange -- AC-16/FR-025: Sales can VIEW an assignment. Without the list the view would call
        // an active cadence "Retired cadence".
        await ResetDatabaseAsync();
        var (activeId, _) = await SeedCadencesAsync();

        // Act
        var response = await _factory.AsCompassSales().GetAsync($"{CadencesUrl}?activeOnly=true", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InvoiceFrequencyTypeDto>>(Token);
        rows!.ShouldContain(r => r.Id == activeId);
    }

    [Fact]
    public async Task ACompassAdminUser_CanListTheCadences()
    {
        // Arrange -- the other half of the Elevated tier (AC-44: Admin is read-only, but it READS).
        await ResetDatabaseAsync();
        await SeedCadencesAsync();

        // Act
        var response = await _factory.AsCompassAdmin().GetAsync(CadencesUrl, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATimesheetOnlySession_IsRefused()
    {
        // Arrange -- every module owns its own roles and inherits NOTHING. Asserting the EXACT status
        // rather than "not 200": under a stale DevBypass a loose assertion would pass and the gate
        // would be gone.
        await ResetDatabaseAsync();
        await SeedCadencesAsync();

        // Act
        var response = await _factory.AsTimesheetRoot().GetAsync(CadencesUrl, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
