using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Contract tests pinning the EXACT public surface of the EDJEr configuration DTOs and request.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="CompassLookupDtoTests"/> and <see cref="CompassEmployeeDtoTests"/>, and for the
/// same reason: it makes "add one more field while I'm here" a failing build. AC-17 fixes the field set,
/// and Principle II forbids speculative fields, so the member set is the requirement rather than an
/// implementation detail.
/// </para>
/// <para>
/// Two omissions are asserted by name rather than left implicit, because both are things a reader would
/// otherwise reasonably add: AC-NFR-6 records no termination date (deactivation is the active
/// flag) and no billing rate. And there is no surrogate identifier: <c>compass.employee</c>'s
/// <c>int</c> identity key is the published form since feature 003, and Constitution 1.2.0 records that
/// no second addressing scheme is wanted.
/// </para>
/// </remarks>
public class CompassEdjerDtoTests
{
    private static readonly string[] AgreedDetailMembers =
    [
        "Id",
        "FirstName",
        "LastName",
        "HireDate",
        "Email",
        "EmployeeTypeId",
        "CoachEmployeeId",
        "StateOfResidence",
        "IsActive",
        "TimesheetRequired",
        "CanSubmitUnder40",
        "IncludeInPayroll",
        // NOT an AC-17 field either. The EDJEr's IANA timezone (FR-8.1, issue #421): Compass became
        // the system of record for it when OOTO started migrating onto this directory, so it is a
        // real directory attribute rather than a speculative one. On BOTH the DTO and the request,
        // unlike LegacyTpsId — the form has to render the stored zone to edit it.
        "Timezone",
        // NOT an AC-17 field either. Whether the EDJEr is on the delivery team, on the ruling in
        // issue #502: stored on the Compass record rather than derived from the frozen legacy
        // directory tables. On BOTH the DTO and the request, for the same reason as Timezone.
        "IsDeliveryTeam",
        // NOT an AC-17 field either. The technical-skills feature: which skills this EDJEr is tagged
        // with. On BOTH the DTO and the request, same shape as Timezone/IsDeliveryTeam above.
        "SkillIds",
    ];

    private static readonly string[] AgreedSummaryMembers =
    [
        "Id",
        "FirstName",
        "LastName",
        "Email",
        "EmployeeTypeName",
        "IsActive",
        // Issue #659: the admin list adopts Team Directory's own columns, so the summary needs the
        // three fields that display renders — hire date, state, and the coach's resolved name (not
        // a hyperlink here, unlike Team Directory's own coach column).
        "HireDate",
        "StateOfResidence",
        "CoachName",
    ];

    private static readonly string[] AgreedRequestMembers =
    [
        "FirstName",
        "LastName",
        "HireDate",
        "Email",
        "EmployeeTypeId",
        "CoachEmployeeId",
        "StateOfResidence",
        "IsActive",
        "TimesheetRequired",
        "CanSubmitUnder40",
        "IncludeInPayroll",
        // See the detail DTO above. Optional on the wire so /api/compass/v1 stays additive; required
        // by the screen, and refused when present-but-blank.
        "Timezone",
        // NOT an AC-17 field. Migration provenance (feature 010): the identifier this EDJEr carried
        // in the legacy TPS directory, settable only by the migration principal. It is on the
        // REQUEST and deliberately not on either DTO — a migrated record is indistinguishable from a
        // hand-created one everywhere the application reads it, which is the point.
        //
        // ⚠️ That "not on either DTO" was fenced HERE and only here, and it got reversed elsewhere
        // (issue #430): a change added the same member to CompassEmployeeDto — the directory
        // boundary's payload, which this file has no opinion about — whose HTTP twin is gated on
        // RolePolicy.CompassAdmin. Every Compass Admin could then read the provenance value that
        // CompassMigrationProvenanceEndpoints withholds from them BY IDENTITY. The boundary payloads
        // are now fenced too, by CompassEmployeeDtoTests and CompassTransportContractTests. There is
        // no in-process exception either: a consumer correlating a Compass record with a legacy one
        // uses email (issue #424), so provenance leaves the module through no path but that one route.
        "LegacyTpsId",
        // See the detail DTO above. Nullable and trailing so /api/compass/v1 stays additive and so
        // absent stays distinguishable from an explicit false — a non-nullable bool would bind
        // false for a client that says nothing, and false is the one value nobody may infer.
        "IsDeliveryTeam",
        // See the detail DTO above. Nullable and trailing for the same reason: absent means "say
        // nothing about skills" rather than "clear them".
        "SkillIds",
    ];

    private static string[] MembersOf(Type type) =>
        [.. type.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)];

    private static string[] Sorted(string[] members) =>
        [.. members.OrderBy(n => n, StringComparer.Ordinal)];

    [Fact]
    public void CompassEdjerDto_ExposesExactlyTheAgreedMembers()
    {
        // Arrange & Act
        var actual = MembersOf(typeof(CompassEdjerDto));

        // Assert
        actual.ShouldBe(
            Sorted(AgreedDetailMembers),
            "AC-17 fixes the EDJEr field set; a new member needs a criterion asking for it"
        );
    }

    [Fact]
    public void CompassEdjerSummaryDto_ExposesExactlyTheAgreedMembers()
    {
        // Arrange & Act
        var actual = MembersOf(typeof(CompassEdjerSummaryDto));

        // Assert
        actual.ShouldBe(Sorted(AgreedSummaryMembers));
    }

    [Fact]
    public void CompassEdjerRequest_ExposesExactlyTheAgreedMembers()
    {
        // Arrange & Act
        var actual = MembersOf(typeof(CompassEdjerRequest));

        // Assert
        actual.ShouldBe(
            Sorted(AgreedRequestMembers),
            "the request captures AC-17's field set plus migration provenance — and carries no id, "
                + "which the route supplies"
        );
    }

    [Fact]
    public void TheRequest_CarriesNoIdentifier_BecauseTheRouteSuppliesIt()
    {
        // Arrange & Act & Assert — an id in the body as well as the path is two sources of truth, and the
        // question of which wins is one nobody should have to ask.
        MembersOf(typeof(CompassEdjerRequest)).ShouldNotContain("Id");
    }

    [Fact]
    public void TheRequest_CarriesNoReasonField()
    {
        // Arrange & Act & Assert — every EDJEr write is audited and IAuditService.LogAsync rejects a
        // blank reason, but the reason is system-generated (CompassAuditReason) because no acceptance
        // criterion asks the administrator for one. A reason field on the form would be an invented
        // requirement (research F-1).
        MembersOf(typeof(CompassEdjerRequest)).ShouldNotContain("Reason");
    }

    [Theory]
    [InlineData("TerminationDate")]
    [InlineData("EndDate")]
    [InlineData("BillingRate")]
    [InlineData("Rate")]
    public void NoEdjerType_CarriesADeliberatelyOmittedField(string omitted)
    {
        // Arrange — AC-NFR-6 omits both a termination date and a billing rate. Deactivation is the active
        // flag; EDJErs are never deleted, which is why email uniqueness spans inactive rows.
        // Act & Assert
        MembersOf(typeof(CompassEdjerDto)).ShouldNotContain(omitted);
        MembersOf(typeof(CompassEdjerSummaryDto)).ShouldNotContain(omitted);
        MembersOf(typeof(CompassEdjerRequest)).ShouldNotContain(omitted);
    }

    [Theory]
    [InlineData("PublicId")]
    [InlineData("Uuid")]
    [InlineData("Guid")]
    public void NoEdjerType_IntroducesASurrogateIdentifier(string surrogate)
    {
        // Arrange — feature 003 made compass.employee's int identity key the published form and
        // Constitution 1.2.0 records that no surrogate is wanted. An earlier draft of spec 004 specified a
        // `public_id uuid`; it is withdrawn in full, and this is what keeps it withdrawn.
        // Act & Assert
        MembersOf(typeof(CompassEdjerDto)).ShouldNotContain(surrogate);
        MembersOf(typeof(CompassEdjerSummaryDto)).ShouldNotContain(surrogate);
        MembersOf(typeof(CompassEdjerRequest)).ShouldNotContain(surrogate);
    }

    [Fact]
    public void TheSummary_CarriesNoTimeTrackingFlag()
    {
        // Arrange — BR-1 makes the time-tracking settings Super-Admin-only, and a list is the wrong place
        // to broadcast them even to a Super Admin. They belong to the record's own screen.
        // Act
        var members = MembersOf(typeof(CompassEdjerSummaryDto));

        // Assert
        members.ShouldNotContain(nameof(CompassEdjerDto.TimesheetRequired));
        members.ShouldNotContain(nameof(CompassEdjerDto.CanSubmitUnder40));
        members.ShouldNotContain(nameof(CompassEdjerDto.IncludeInPayroll));
    }

    [Fact]
    public void TheDetailDto_IdentifiesTheEmployeeTypeById_NotByName()
    {
        // Arrange — the form populates its select from the active-only lookup list, so a denormalised
        // name here would be a second copy of data the screen already holds. The SUMMARY carries the name
        // instead, because the list renders it as text and resolving it per row on the client is the N+1
        // the p95 target rules out.
        // Act & Assert
        MembersOf(typeof(CompassEdjerDto)).ShouldContain("EmployeeTypeId");
        MembersOf(typeof(CompassEdjerDto)).ShouldNotContain("EmployeeTypeName");
        MembersOf(typeof(CompassEdjerSummaryDto)).ShouldContain("EmployeeTypeName");
    }
}
