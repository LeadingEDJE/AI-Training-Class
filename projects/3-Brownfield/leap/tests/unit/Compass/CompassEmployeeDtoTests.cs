using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Contract test pinning the EXACT public surface of the Compass boundary DTO.
/// </summary>
/// <remarks>
/// <para>
/// This test's purpose is as much social as technical: it makes "add one more field while I'm here"
/// a failing build. Phase 49 delivers plumbing, not product — Compass starts empty specifically so
/// that the incoming team's product requirements drive its data model, and a DTO that quietly grows
/// speculative fields is exactly how Compass domain modelling would creep in before they arrive.
/// </para>
/// <para>
/// To the Compass team: you are free to change this test. It guards THIS phase's scope
/// promise, not your design. Once you own the module, extend the DTO and update this test in the same
/// commit — that is the intended workflow, not a violation of it.
/// </para>
/// <para>
/// Grown for spec 009 Slice 1 (FR-004, FR-008), in exactly the same commit as the DTO — the
/// workflow this test's own remarks invite, not a violation of the pin.
/// </para>
/// </remarks>
public class CompassEmployeeDtoTests
{
    [Fact]
    public void CompassEmployeeDto_ExposesExactlyTheAgreedMembers()
    {
        // Arrange — the agreed set: the original three, plus Slice 1's profile growth (FR-004,
        // FR-008), plus Timezone (PRD v9 FR-8.4, issue #422), plus IsDeliveryTeam, which feature
        // 016 FR-007 requires on BOTH transports and FR-008 requires not tier-gated. Nothing
        // invented beyond what those requirements ask for, and specifically no migration
        // provenance — see the fact below.
        var expected = new[]
        {
            "Id", "DisplayName", "IsActive", "HireDate", "EmployeeType", "StateOfResidence", "Coach",
            "TimeTracking", "Timezone", "Email", "IsDeliveryTeam",
        };

        // Act
        var actual = typeof(CompassEmployeeDto)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Assert
        actual.ShouldBe(
            expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            "the Compass boundary DTO's member set is pinned to prevent Compass domain modelling "
                + "creeping in before the incoming team makes its own data-model decisions");
    }

    /// <summary>
    /// This payload carries NO migration provenance — asserted by NAME rather than left to the exact
    /// member-set pin above (issue #430).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why a second, narrower fact when the pin above is already exact. The pin fails on any
    /// member-set change and says only "the set differs"; this one names the decision, so a reader who
    /// hits it learns why the field cannot come back. It shipped once and had to be reverted:
    /// <c>LegacyTpsId</c> was added here, and because this type has an HTTP twin gated on
    /// <c>RolePolicy.CompassAdmin</c>, every Compass Admin could then read the provenance value that
    /// <c>CompassMigrationProvenanceEndpoints</c> withholds from them BY IDENTITY — explicitly because
    /// "a role check would expose this to every Super Admin".
    /// </para>
    /// <para>
    /// Three gates missed it, which is the reason this fact is on THIS type. The decision was
    /// fenced on <c>CompassEdjerDto</c>, <c>CompassEdjerSummaryDto</c> and <c>CompassEdjerRequest</c>
    /// (<c>CompassEdjerDtoTests</c>), none of which is this type. A consumer needing to correlate a
    /// Compass record with a legacy one correlates on <c>Email</c> (issue #424) — the key both stores
    /// already hold, and no provenance at all. The bridge this fact originally pointed at, an
    /// in-process <c>legacy_tps_id</c> contract, was deleted for exactly that reason: one correlation
    /// key across the cutover beats two (issue #544). The fence is unaffected by its deletion, which
    /// is the point of fencing the DECISION rather than the alternative.
    /// </para>
    /// <para>
    /// Matched on the SUBSTRING rather than the exact name <c>LegacyTpsId</c>, so a differently-spelled
    /// reintroduction (<c>TpsIdentifier</c>, <c>LegacyId</c>) fails here too rather than only tripping
    /// the member-set pin with no explanation attached.
    /// </para>
    /// </remarks>
    [Fact]
    public void CompassEmployeeDto_CarriesNoMigrationProvenanceMember()
    {
        // Arrange & Act
        var members = typeof(CompassEmployeeDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        // Non-vacuity: a reflection scan finding nothing must not pass by default.
        members.ShouldNotBeEmpty("reflected over zero members — this assertion would pass for free");

        var provenanceMembers = members
            .Where(name =>
                name.Contains("Tps", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Legacy", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Provenance", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Assert
        provenanceMembers.ShouldBeEmpty(
            "migration provenance must not appear on an application-facing payload: this DTO's HTTP "
                + "twin is gated on a ROLE, while the one route that publishes the provenance map is "
                + "gated on principal IDENTITY precisely to keep it away from every Compass Super "
                + "Admin. Correlate on Email instead, which is not provenance. Offenders: "
                + string.Join(", ", provenanceMembers));
    }

    [Fact]
    public void CompassEmployeeDto_UsesTheDirectoryIdentifierType()
    {
        // Arrange & Act — compass.employee.employee_id is an int identity, so the boundary
        // identifier is an int (owner decision D-2). Referencing a Compass record by its native key
        // is the platform-wide form.
        //
        // This assertion previously pinned Guid, on the reasoning that "narrowing to an int would
        // require inventing an identity map, which is Compass domain modelling." That reasoning was
        // correct while the boundary resolved against public.employees and its preserved uuid key —
        // an int would then have been a fabricated second identity. Feature 003 removed the premise:
        // Compass owns the store, so the int IS the store's own key and no map is invented.
        var idProperty = typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.Id));

        // Assert
        idProperty.ShouldNotBeNull();
        idProperty.PropertyType.ShouldBe(typeof(int));
    }
}
