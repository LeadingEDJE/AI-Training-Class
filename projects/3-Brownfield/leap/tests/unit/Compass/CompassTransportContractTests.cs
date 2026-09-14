using System.Reflection;
using System.Text.Json.Serialization;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Asserts the ONE-CONTRACT property mechanically: the in-process boundary and the HTTP transport
/// return the SAME type, not merely similar shapes.
/// </summary>
/// <remarks>
/// <para>
/// Why this is worth a test: extracting Compass into its own process later should be a TRANSPORT
/// SWAP, not a rewrite. Two divergent response shapes — one for the interface, one for the endpoint —
/// would quietly make it a rewrite, and nothing else in the build would notice. This test makes
/// introducing a second, endpoint-only response type a FAILING BUILD.
/// </para>
/// <para>
/// The types are compared by reflection rather than by hand-comparing members, so a shape that merely
/// looks the same does not satisfy it.
/// </para>
/// </remarks>
public class CompassTransportContractTests
{
    [Fact]
    public void BoundaryAndEndpoint_ReturnTheSameDtoType()
    {
        // Arrange — the declared payload type of the in-process boundary method.
        var boundaryMethod = typeof(IDirectory).GetMethod(nameof(IDirectory.GetEmployeeAsync));
        boundaryMethod.ShouldNotBeNull();
        var boundaryPayload = UnwrapPayloadType(boundaryMethod.ReturnType);

        // Arrange — the declared payload type of the HTTP handler.
        var handler = typeof(CompassEmployeeEndpoints)
            .GetMethod("GetById", BindingFlags.NonPublic | BindingFlags.Static);
        handler.ShouldNotBeNull(
            "the endpoint handler must exist and stay discoverable for this contract to be checkable");
        var endpointPayload = UnwrapPayloadType(handler.ReturnType);

        // Assert — the SAME type, not two structurally similar ones.
        endpointPayload.ShouldBe(
            boundaryPayload,
            "the HTTP transport and the in-process boundary must return one contract, so a later "
                + "extraction of Compass is a transport swap rather than a rewrite");
        boundaryPayload.ShouldBe(typeof(CompassEmployeeDto));
    }

    /// <summary>
    /// Slice 1's profile growth (FR-004, FR-008) is carried by the SAME type both transports already
    /// share — proven above — but this names the growth explicitly, per standing rule 1: extend this
    /// file in the same change as any addition to the published payload, rather than relying on a
    /// generic shape check to notice by accident.
    /// </summary>
    [Fact]
    public void TheGrownProfilePayload_CarriesSlice1sNewMembers()
    {
        // Arrange / Act
        var members = typeof(CompassEmployeeDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert
        members.ShouldContain(nameof(CompassEmployeeDto.HireDate));
        members.ShouldContain(nameof(CompassEmployeeDto.EmployeeType));
        members.ShouldContain(nameof(CompassEmployeeDto.StateOfResidence));
        members.ShouldContain(nameof(CompassEmployeeDto.Coach));
        members.ShouldContain(nameof(CompassEmployeeDto.TimeTracking));

        // The three original members are unchanged (FR-024: a rename or type change is breaking).
        typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.Id))!.PropertyType
            .ShouldBe(typeof(int));
        typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.DisplayName))!.PropertyType
            .ShouldBe(typeof(string));
        typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.IsActive))!.PropertyType
            .ShouldBe(typeof(bool));
    }

    /// <summary>
    /// The profile payload carries the EDJEr's IANA timezone (PRD v9 FR-8.4, issue #422), on both
    /// transports and at every authorized tier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named here per standing rule 1 — the contract requires this file be extended in the same change
    /// as any addition to a published payload, rather than letting the generic reflection gates below
    /// notice by accident.
    /// </para>
    /// <para>
    /// An IANA identifier, so a <c>string</c> and not a <c>TimeZoneInfo</c>. The value is stored
    /// as an IANA id precisely so it crosses the HTTP transport unchanged (FR-8.6);
    /// <c>TimeZoneInfo</c> has no stable JSON form and would resolve differently on a host whose
    /// database of zones differs. The six accepted ids live in <c>UsTimeZones.All</c>, which is
    /// deliberately NOT re-asserted here — this contract publishes whatever Compass stores, and
    /// pinning the option list in a transport test would make widening it (Plan v6 requires that stay
    /// possible without a schema change) fail in a file that has no opinion on the matter.
    /// </para>
    /// <para>
    /// Not tier-gated, unlike <see cref="CompassEmployeeDto.TimeTracking"/>. Timezone is an
    /// ordinary directory attribute in the same class as
    /// <see cref="CompassEmployeeDto.StateOfResidence"/>, so every caller that reaches the read gets it.
    /// This says nothing about ACCESS — the HTTP route keeps its Compass admin policy. It matters
    /// because FR-8.4's consumer (OOTO, rendering and mailing event times) reaches the boundary
    /// in-process holding no Compass privilege, so a payload gate would withhold the field from the one
    /// caller it exists for.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProfilePayload_CarriesTheEdjersIanaTimezone()
    {
        // Arrange / Act
        var timezone = typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.Timezone));

        // Assert
        timezone.ShouldNotBeNull(
            "FR-8.4 requires the directory boundary to publish the EDJEr's timezone, so OOTO reads it "
                + "from Compass rather than from the Timesheet module's own Employee row");
        timezone.PropertyType.ShouldBe(
            typeof(string),
            "the timezone is published as an IANA identifier (FR-8.6), not as a resolved TimeZoneInfo");

        // Ungated. The shape of a tier gate on this DTO is a conditionally-omitted member, so that is
        // what is asserted — not member nullability, which for a reference type reflection cannot see
        // and an assertion about would pass no matter what the code said.
        typeof(CompassEmployeeDto)
            .GetProperty(nameof(CompassEmployeeDto.TimeTracking))!
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .ShouldNotBeEmpty("the gated member is the one that is conditionally omitted");
        timezone
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .ShouldBeEmpty("the timezone is published to every authorized tier, never omitted");
    }

    /// <summary>
    /// The profile payload carries the stored delivery-team flag, and does not tier-gate it
    /// (FR-007, FR-008).
    /// </summary>
    /// <remarks>
    /// Named here per standing rule 1: this file grows in the same change as any addition to a
    /// published payload. The employee-payload assertions above are containment checks, so this
    /// fact adds a gate rather than repairing one that went red. A tier gate on this DTO is a
    /// conditionally omitted member, so that is what is asserted — plus absence from the gated
    /// group, the other place this flag could plausibly have gone.
    /// </remarks>
    [Fact]
    public void TheProfilePayload_CarriesTheDeliveryTeamFlag_AndDoesNotTierGateIt()
    {
        // Arrange — the member name as a LITERAL, not nameof. A control that removes the member or
        // moves it into the gated group has to make this test FAIL; nameof would make it fail to
        // COMPILE instead, which proves nothing about what this suite catches.
        const string Member = "IsDeliveryTeam";

        // Act
        var flag = typeof(CompassEmployeeDto).GetProperty(Member);
        var gatedGroupMembers = typeof(CompassEmployeeTimeTrackingDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        // Assert — the member exists on the shared contract, with the expected type.
        flag.ShouldNotBeNull(
            $"FR-007 requires {Member} on BOTH transports, and both return this one type");
        flag.PropertyType.ShouldBe(
            typeof(bool),
            "the flag is stored non-nullable with a default, so the payload has no third state");

        // Assert — not tier-gated, on both shapes a gate could take here.
        gatedGroupMembers.ShouldNotBeEmpty(
            "reflected over zero gated members — the containment check below would pass for free");
        gatedGroupMembers.ShouldNotContain(
            Member,
            "moving the flag into the tier-gated group would withhold it from every caller below "
                + "Compass Super Admin, which is exactly what FR-008 forbids");
        typeof(CompassEmployeeDto)
            .GetProperty(nameof(CompassEmployeeDto.TimeTracking))!
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .ShouldNotBeEmpty("the gated member is the one that is conditionally omitted");
        flag
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .ShouldBeEmpty("the flag is published to every authorized tier, never omitted");
    }

    /// <summary>
    /// NO payload on this boundary carries migration provenance — not the employee's, not the
    /// client's (issue #430).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named here per standing rule 1: this file is extended in the same change as any change to a
    /// published payload, rather than letting the generic reflection gates below notice by accident.
    /// This fact replaces one that asserted the opposite for the employee, and the reversal is
    /// the point of recording it here.
    /// </para>
    /// <para>
    /// What went wrong. The employee payload briefly published the stored
    /// <c>legacy_tps_id</c> string. Both transports return this same type, and the HTTP twin
    /// (<c>GET /api/compass/v1/employees/{id}</c>) is gated on <c>RolePolicy.CompassAdmin</c> — so
    /// every Compass Admin could read the provenance value that
    /// <c>CompassMigrationProvenanceEndpoints</c> withholds from them BY IDENTITY, explicitly because
    /// "a role check would expose this to every Super Admin". One-contract-two-transports is exactly
    /// what made a payload decision into a disclosure decision.
    /// </para>
    /// <para>
    /// The replacement. A consumer whose own wire contract is a <c>Guid</c> correlates
    /// employees on <c>Email</c> (issue #424) — an ordinary directory attribute both stores already
    /// hold, and no provenance at all. An in-process <c>legacy_tps_id</c> contract was built for this
    /// and deleted once the email read landed, because one correlation key across the cutover beats
    /// two (issue #544). Clients keep ADR-010's derivation from the Compass <c>int</c>:
    /// <c>legacy_tps_id</c> is written only by the migration, so correlating clients on it would
    /// permanently exclude every client created natively in Compass.
    /// </para>
    /// <para>
    /// Scanned across EVERY <see cref="IDirectory"/> payload rather than the two named types, because
    /// the failure this guards against is a provenance member appearing on whichever payload a future
    /// consumer happens to reach for.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoBoundaryPayload_CarriesMigrationProvenance()
    {
        // Arrange — every payload type reachable from the boundary interface's own signatures, and
        // then TRANSITIVELY every published-seam type any of those nests.
        var roots = typeof(IDirectory)
            .GetMethods()
            .Select(method => UnwrapPayloadType(method.ReturnType))
            .Distinct()
            .ToList();

        // Non-vacuity: a scan over zero types passes every assertion.
        roots.ShouldNotBeEmpty(
            $"{nameof(IDirectory)} declares no payload types — this assertion would pass vacuously");

        var boundaryDtos = TransitivelyReachablePayloads(roots);

        boundaryDtos.ShouldContain(typeof(CompassEmployeeDto));
        boundaryDtos.ShouldContain(typeof(CompassClientDto));

        // Non-vacuity for the RECURSION specifically. Without this the walk could quietly stop
        // working — a changed namespace constant, a wrapper UnwrapPayloadType stops peeling — and
        // the assertion below would go back to scanning only top-level members while still passing.
        // This type is reached ONLY through CompassEmployeeDto.Coach.
        boundaryDtos.ShouldContain(
            typeof(CompassEmployeeCoachDto),
            "the transitive walk must reach a NESTED published payload; a nested type is where the "
                + "provenance disclosure ADR-010 records actually slipped through the first time");
        boundaryDtos.ShouldContain(typeof(CompassEmployeeTimeTrackingDto));
        boundaryDtos.Count.ShouldBeGreaterThan(
            roots.Count,
            "the walk found no nested type at all, so it is a top-level scan wearing a recursion's name");

        // Act — matched on the substring, so a differently-spelled reintroduction fails here too.
        var offenders = boundaryDtos
            .SelectMany(dto => dto.GetProperties().Select(p => $"{dto.Name}.{p.Name}"))
            .Where(member =>
                member.Contains("Tps", StringComparison.OrdinalIgnoreCase)
                || member.Contains("Legacy", StringComparison.OrdinalIgnoreCase)
                || member.Contains("Provenance", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "a published boundary payload carries migration provenance. Both transports return the "
                + "same type, and the HTTP twin is gated on a ROLE — so this hands provenance to "
                + "every Compass Admin, which the identity-gated provenance route exists to prevent. "
                + "Correlate on Email instead, which is not provenance. Offenders: "
                + string.Join(", ", offenders));
    }

    /// <summary>
    /// The profile payload carries the employee's email — the key the email-keyed reads correlate on
    /// (feature 018, PRD v9 FR-8.4). Added here per standing rule 1, in the same change as the member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this member is not optional to the contract.
    /// <see cref="IDirectory.GetEmployeesByEmailAsync"/> answers for a SET of addresses and omits the
    /// ones matching nothing, so a caller cannot tell which returned record answers which request
    /// unless the record carries the key. Without it the batch read is unusable and the caller is
    /// forced back to one lookup per row.
    /// </para>
    /// <para>
    /// Ungated, like <see cref="CompassEmployeeDto.Timezone"/> and
    /// <see cref="CompassEmployeeDto.StateOfResidence"/>. Asserted the same way for the same
    /// reason: a tier gate on this DTO takes the shape of a conditionally-omitted member, so the
    /// absence of that attribute is the checkable property. Route-level authorization on both
    /// transports is untouched.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProfilePayload_CarriesTheCorrelationEmail()
    {
        // Arrange / Act
        var email = typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.Email));

        // Assert
        email.ShouldNotBeNull(
            "the email-keyed reads correlate on this attribute, and a batch result that omits it "
                + "cannot be matched back to the addresses that were requested");
        email.PropertyType.ShouldBe(typeof(string));
        email
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .ShouldBeEmpty("the email is published to every authorized tier, never omitted");
    }

    /// <summary>
    /// Slice 2's invoice-frequencies payload (FR-010) carries only <c>Id</c> and <c>TypeName</c> — per
    /// standing rule 1, extend this file in the same change as any addition to the published payload
    /// rather than relying on the generic reflection gates below to notice by accident.
    /// </summary>
    [Fact]
    public void TheInvoiceFrequencyPayload_CarriesTypeNameOnly()
    {
        // Arrange / Act
        var members = typeof(CompassInvoiceFrequencyDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert — exactly Id and TypeName. No IsActive: every returned row is active by construction
        // (data-model.md § 6), so publishing an always-true column invites a consumer to filter on it.
        members.Count.ShouldBe(2);
        members.ShouldContain(nameof(CompassInvoiceFrequencyDto.Id));
        members.ShouldContain(nameof(CompassInvoiceFrequencyDto.TypeName));

        typeof(CompassInvoiceFrequencyDto).GetProperty(nameof(CompassInvoiceFrequencyDto.Id))!.PropertyType
            .ShouldBe(typeof(int));
        typeof(CompassInvoiceFrequencyDto).GetProperty(nameof(CompassInvoiceFrequencyDto.TypeName))!.PropertyType
            .ShouldBe(typeof(string));
    }

    /// <summary>
    /// Slice 3's client payload (FR-005) carries the derived status as a STRING, never the
    /// <c>ClientStatus</c> enum — per standing rule 1, extend this file in the same change as any
    /// addition to the published payload rather than relying on the generic reflection gates below to
    /// notice by accident.
    /// </summary>
    /// <remarks>
    /// This is the specific hazard this slice introduces: <c>ClientStatusNonGatingTests
    /// .NoProductionTypeAnywhere_HoldsADerivedStatus</c> already fails the build if any production
    /// member holds the enum, so this fact names the requirement explicitly rather than leaning on
    /// that architecture gate alone to catch a regression here.
    /// </remarks>
    [Fact]
    public void TheClientPayload_CarriesTheDerivedStatusAsAString()
    {
        // Arrange / Act
        var members = typeof(CompassClientDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert
        members.ShouldContain(nameof(CompassClientDto.Id));
        members.ShouldContain(nameof(CompassClientDto.ClientName));
        members.ShouldContain(nameof(CompassClientDto.MsaSignedDate));
        members.ShouldContain(nameof(CompassClientDto.NdaSignedDate));
        members.ShouldContain(nameof(CompassClientDto.IsInternal));
        members.ShouldContain(nameof(CompassClientDto.InvoiceFrequency));
        members.ShouldContain(nameof(CompassClientDto.Status));

        // The derived value is a STRING, never the ClientStatus enum -- the whole point of this fact.
        typeof(CompassClientDto).GetProperty(nameof(CompassClientDto.Status))!.PropertyType
            .ShouldBe(typeof(string));

        // No raw InvoiceFrequencyTypeId foreign key -- the resolved NAME only (data-model.md § 2).
        members.ShouldNotContain("InvoiceFrequencyTypeId");
    }

    /// <summary>
    /// Slice 4's billable-categories payload (FR-009) carries exactly <c>Id</c>, <c>ClientId</c>,
    /// <c>CategoryName</c> and <c>IsActive</c> — per standing rule 1, extend this file in the same
    /// change as any addition to the published payload rather than relying on the generic reflection
    /// gates below to notice by accident.
    /// </summary>
    /// <remarks>
    /// This is the family that carries an <c>IsActive</c> flag rather than filtering it out — the
    /// OPPOSITE choice from <see cref="CompassInvoiceFrequencyDto"/> above. Both are requirement-driven
    /// (data-model.md § 5); this fact exists so a reviewer sees the asymmetry named rather than
    /// discovering it as an inconsistency.
    /// </remarks>
    [Fact]
    public void TheBillableCategoryPayload_CarriesClientIdAndItsOwnActiveFlag()
    {
        // Arrange / Act
        var members = typeof(CompassBillableCategoryDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert — exactly these four members, unlike CompassInvoiceFrequencyDto's two.
        members.Count.ShouldBe(4);
        members.ShouldContain(nameof(CompassBillableCategoryDto.Id));
        members.ShouldContain(nameof(CompassBillableCategoryDto.ClientId));
        members.ShouldContain(nameof(CompassBillableCategoryDto.CategoryName));
        members.ShouldContain(nameof(CompassBillableCategoryDto.IsActive));

        typeof(CompassBillableCategoryDto).GetProperty(nameof(CompassBillableCategoryDto.Id))!.PropertyType
            .ShouldBe(typeof(int));
        typeof(CompassBillableCategoryDto).GetProperty(nameof(CompassBillableCategoryDto.ClientId))!.PropertyType
            .ShouldBe(typeof(int));
        typeof(CompassBillableCategoryDto).GetProperty(nameof(CompassBillableCategoryDto.CategoryName))!.PropertyType
            .ShouldBe(typeof(string));
        typeof(CompassBillableCategoryDto).GetProperty(nameof(CompassBillableCategoryDto.IsActive))!.PropertyType
            .ShouldBe(typeof(bool));
    }

    /// <summary>
    /// Slice 5's assignment payload (FR-006, FR-012) carries exactly these eight members — per
    /// standing rule 1, extend this file in the same change as any addition to the published payload
    /// rather than relying on the generic reflection gates below to notice by accident.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the family reached by THREE routes (<c>/assignments/{id}</c>,
    /// <c>/employees/{id}/assignments</c>, <c>/clients/{id}/assignments</c>) sharing the SAME
    /// contract — exactly the "one contract per data kind, reached however many ways" shape
    /// <see cref="TheDirectoryBoundary_PublishesOneDtoPerFamily_WithNoSharedShapes"/> and
    /// <see cref="EveryBoundaryHandlerPayload_IsAlsoAnIDirectoryPayload"/> below are written to
    /// tolerate: they dedupe by TYPE, not by route or method count, so growing from one route to three
    /// for the same DTO does not trip either gate.
    /// </para>
    /// <para>
    /// No raw <c>InvoiceFrequencyTypeId</c> foreign key — only the resolved, PRECEDENCE-derived
    /// <see cref="CompassAssignmentDto.EffectiveInvoiceFrequency"/> name (FR-012), matching the pattern
    /// from <see cref="CompassClientDto"/> and <see cref="CompassBillableCategoryDto"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAssignmentPayload_CarriesTheResolvedEffectiveInvoiceFrequencyName()
    {
        // Arrange / Act
        var members = typeof(CompassAssignmentDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert — exactly these eight members.
        members.Count.ShouldBe(8);
        members.ShouldContain(nameof(CompassAssignmentDto.Id));
        members.ShouldContain(nameof(CompassAssignmentDto.EmployeeId));
        members.ShouldContain(nameof(CompassAssignmentDto.ClientId));
        members.ShouldContain(nameof(CompassAssignmentDto.ClientName));
        members.ShouldContain(nameof(CompassAssignmentDto.StartDate));
        members.ShouldContain(nameof(CompassAssignmentDto.EndDate));
        members.ShouldContain(nameof(CompassAssignmentDto.Note));
        members.ShouldContain(nameof(CompassAssignmentDto.EffectiveInvoiceFrequency));

        // The resolved NAME is a nullable string, not the raw foreign key.
        typeof(CompassAssignmentDto).GetProperty(nameof(CompassAssignmentDto.EffectiveInvoiceFrequency))!
            .PropertyType.ShouldBe(typeof(string));

        // No raw InvoiceFrequencyTypeId foreign key on either side of the precedence (data-model.md § 3).
        members.ShouldNotContain("InvoiceFrequencyTypeId");
    }

    /// <summary>
    /// The three assignment lookups all declare <see cref="CompassAssignmentDto"/> (or a collection of
    /// it) as their payload — locking in the "one contract, three routes" shape named above as an
    /// explicit fact rather than leaving it implicit in the generic reflection gates.
    /// </summary>
    [Fact]
    public void AllThreeAssignmentLookups_ShareTheSameBoundaryContract()
    {
        // Arrange / Act
        var byId = typeof(IDirectory).GetMethod(nameof(IDirectory.GetAssignmentAsync));
        var byEmployee = typeof(IDirectory).GetMethod(nameof(IDirectory.GetAssignmentsByEmployeeAsync));
        var byClient = typeof(IDirectory).GetMethod(nameof(IDirectory.GetAssignmentsByClientAsync));

        // Assert
        byId.ShouldNotBeNull();
        byEmployee.ShouldNotBeNull();
        byClient.ShouldNotBeNull();

        UnwrapPayloadType(byId.ReturnType).ShouldBe(typeof(CompassAssignmentDto));
        UnwrapPayloadType(byEmployee.ReturnType).ShouldBe(typeof(CompassAssignmentDto));
        UnwrapPayloadType(byClient.ReturnType).ShouldBe(typeof(CompassAssignmentDto));
    }

    /// <summary>
    /// Slice 6's SOW payload (FR-007) carries exactly these seven members — per standing rule 1, extend
    /// this file in the same change as any addition to the published payload rather than relying on the
    /// generic reflection gates below to notice by accident.
    /// </summary>
    /// <remarks>
    /// <see cref="Sow.HasPassedApplicationValidation"/> is spec 006's internal grandfathering marker for
    /// the legacy-migration bypass, not consumer data (FR-011) — this fact names its absence explicitly
    /// rather than leaving it to be noticed only if a future edit added it back. The type is named
    /// <c>CompassDirectorySowDto</c>, not the data-model's literal <c>CompassSowDto</c>, because that
    /// name is already taken in this same namespace by the unrelated admin/migration configuration
    /// surface's response record (<c>CompassSowRequests.cs</c>) — see this type's own remarks.
    /// </remarks>
    [Fact]
    public void TheSowPayload_CarriesExactlySevenMembers_AndExcludesHasPassedApplicationValidation()
    {
        // Arrange / Act
        var members = typeof(CompassDirectorySowDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert — exactly these seven members.
        members.Count.ShouldBe(7);
        members.ShouldContain(nameof(CompassDirectorySowDto.Id));
        members.ShouldContain(nameof(CompassDirectorySowDto.ClientAssignmentId));
        members.ShouldContain(nameof(CompassDirectorySowDto.SowType));
        members.ShouldContain(nameof(CompassDirectorySowDto.SowStartDate));
        members.ShouldContain(nameof(CompassDirectorySowDto.SowEndDate));
        members.ShouldContain(nameof(CompassDirectorySowDto.RateIncrease));
        members.ShouldContain(nameof(CompassDirectorySowDto.Note));

        // SowType is the enum's NAME, a plain string -- matching SowRowDto's convention.
        typeof(CompassDirectorySowDto).GetProperty(nameof(CompassDirectorySowDto.SowType))!.PropertyType
            .ShouldBe(typeof(string));

        // Never HasPassedApplicationValidation -- spec 006's internal grandfathering marker, not
        // consumer data.
        members.ShouldNotContain(nameof(Sow.HasPassedApplicationValidation));
    }

    /// <summary>
    /// The directory boundary publishes one DTO per data kind, and no two of them share a shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rewritten again, for feature 009. This test previously asserted
    /// <c>ShouldHaveSingleItem</c> — "the directory boundary has exactly one response contract" — which
    /// was true only because <see cref="IDirectory"/> published a single data kind (the employee
    /// profile). AC-NFR-1 names seven. The first read family this feature adds beyond the profile would
    /// have turned this exact assertion red for a reason that has nothing to do with divergence — the
    /// same trap 001 documented for FR-081 ("a gate that is red the day a real change lands gets
    /// suppressed, which is worse than no gate") and the one <c>ClientStatusNonGatingTests</c> hit at
    /// feature 007. Constitution § Gate integrity forbids weakening a gate to make a change pass, so
    /// this replaces the invariant with the property it was protecting rather than relaxing it.
    /// </para>
    /// <para>
    /// What is protected now: one contract PER data kind, not one contract overall. Every payload
    /// reachable from <see cref="IDirectory"/>'s own method signatures must (1) live in the published DTO
    /// namespace — exactly <see cref="PublishedDtoNamespace"/>, never the application read surface such as
    /// <c>…Dtos.Read</c>, which is the application read surface ADR-008 keeps distinct — and (2) carry a
    /// member set no other published payload shares. A family whose type drifted outside the published
    /// namespace, or two families that reuse an identical shape, still fail the build; growing from one
    /// family to seven no longer does.
    /// </para>
    /// <para>
    /// Deliberately asserts against the UNFILTERED set of boundary payload types rather than filtering
    /// to the Compass namespace first, unlike the assertion this replaces — filtering first would let a
    /// payload that drifted OUTSIDE the published namespace escape notice entirely, which is the
    /// "a pattern matching zero passes every assertion" hazard named repeatedly across this repository.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDirectoryBoundary_PublishesOneDtoPerFamily_WithNoSharedShapes()
    {
        // Arrange — every payload type reachable from the boundary interface's own signatures.
        var boundaryDtos = typeof(IDirectory)
            .GetMethods()
            .Select(method => UnwrapPayloadType(method.ReturnType))
            .Distinct()
            .ToList();

        // Non-vacuity: a reflection scan finding nothing must not pass by default.
        boundaryDtos.ShouldNotBeEmpty(
            $"{nameof(IDirectory)} declares no payload types — this assertion would pass vacuously");

        // Assert — every payload lives in the published boundary's own DTO namespace.
        foreach (var payload in boundaryDtos)
        {
            payload.Namespace.ShouldBe(
                PublishedDtoNamespace,
                $"{payload.Name} is reachable from {nameof(IDirectory)} but is not declared in the "
                    + $"published boundary's own namespace ({PublishedDtoNamespace}) — ADR-008 requires "
                    + "the published contract and the application read surface to stay distinct");

            // And assert that separation DIRECTLY, not as a side effect of the exact match above.
            // Before spec 013 the exact match was the whole guard: `…Compass.Dtos` had a `.Read`
            // sibling, so equality excluded it. `…Compass.Contracts` has no such sibling, so
            // equality alone would no longer be capable of catching a published payload that
            // drifted back into the read surface (FR-005a).
            payload.Namespace?.StartsWith(ApplicationReadSurfaceNamespaceRoot, StringComparison.Ordinal)
                .ShouldBeFalse(
                    $"{payload.Name} is reachable from {nameof(IDirectory)} but lives under the "
                        + $"application read surface ({ApplicationReadSurfaceNamespaceRoot}). ADR-008 "
                        + "keeps the UI's projections and the published contract separate: a type "
                        + "cannot be both. Move it into the published Contracts seam, or return a "
                        + "contract type from the boundary instead of a read projection");
        }

        // Assert — no two published payloads share an identical member set. This is the divergence
        // the original single-item assertion was reaching for, and it stays meaningful at seven families.
        var sharedShapes = boundaryDtos
            .GroupBy(type => string.Join(",", MemberNames(type)))
            .Where(group => group.Count() > 1)
            .ToList();

        sharedShapes.ShouldBeEmpty(
            "two or more IDirectory payload types share an identical member set, meaning the "
                + "transports have diverged onto duplicate shapes rather than one contract per data "
                + "kind: "
                + string.Join(
                    "; ",
                    sharedShapes.Select(group => string.Join(", ", group.Select(type => type.Name)))));

        boundaryDtos.ShouldContain(typeof(CompassEmployeeDto));
    }

    /// <summary>
    /// No other Compass DTO impersonates the boundary contract.
    /// </summary>
    /// <remarks>
    /// The other half of the property above. Growth is fine — a new resource brings its own DTO — but a
    /// second type carrying the boundary's OWN member set is the divergence this file exists to catch,
    /// and it would be invisible to the signature check if it were returned only by an endpoint.
    /// </remarks>
    [Fact]
    public void NoOtherCompassDto_DuplicatesTheBoundaryContractsShape()
    {
        // Arrange
        var boundaryMembers = MemberNames(typeof(CompassEmployeeDto));

        // Act
        var impostors = typeof(CompassEmployeeDto)
            .Assembly.GetTypes()
            .Where(type =>
                type.Namespace is not null
                && type.Namespace.StartsWith(
                    "LeadingEDJE.Leap.Api.Modules.Compass",
                    StringComparison.Ordinal
                )
                && type != typeof(CompassEmployeeDto)
                && type.IsClass
                && !type.IsAbstract
                && type.Name.EndsWith("Dto", StringComparison.Ordinal)
                && MemberNames(type).SequenceEqual(boundaryMembers)
            )
            .ToList();

        // Assert
        impostors.ShouldBeEmpty(
            "a second type with the boundary DTO's exact member set is the divergence this file "
                + "guards against: " + string.Join(", ", impostors.Select(type => type.Name))
        );

        // Non-vacuity: the comparison must actually be looking at Compass DTOs. Without this, a
        // namespace rename would make the scan match nothing and pass for free.
        typeof(CompassEmployeeDto)
            .Assembly.GetTypes()
            .Count(type =>
                type.Namespace?.StartsWith(
                    "LeadingEDJE.Leap.Api.Modules.Compass",
                    StringComparison.Ordinal
                ) == true
                && type.Name.EndsWith("Dto", StringComparison.Ordinal)
            )
            .ShouldBeGreaterThan(1, "the scan found fewer Compass DTOs than actually exist");
    }

    /// <summary>
    /// Every payload a PUBLISHED BOUNDARY handler returns is also an <see cref="IDirectory"/> payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added by feature 005, alongside the two assertions above rather than replacing them. Those read
    /// the boundary INTERFACE; this reads the HTTP handlers, and catches the case they cannot: an
    /// endpoint-only DTO whose member set differs from <see cref="CompassEmployeeDto"/>'s, which
    /// <see cref="NoOtherCompassDto_DuplicatesTheBoundaryContractsShape"/> would not flag.
    /// </para>
    /// <para>
    /// Membership is the EXACT namespace <c>…Modules.Compass.Endpoints</c>. Since ADR-008 Compass
    /// serves two HTTP surfaces, and <c>…Endpoints.Read</c> is the application read surface whose
    /// payloads are viewer-scoped by design — sweeping those in would conflate the two surfaces via
    /// the very test meant to keep them coherent.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryBoundaryHandlerPayload_IsAlsoAnIDirectoryPayload()
    {
        // Arrange
        var boundaryPayloads = typeof(IDirectory)
            .GetMethods()
            .Select(method => UnwrapPayloadType(method.ReturnType))
            .ToHashSet();

        // Act
        var handlers = BoundaryHandlers().ToList();

        // Assert
        handlers.ShouldNotBeEmpty(
            $"no handlers found under {BoundaryEndpointNamespace} — a reflection scan matching nothing "
                + "passes every assertion, so this gate would be inspecting nothing");

        foreach (var (declaringType, handler, payload) in handlers)
        {
            boundaryPayloads.ShouldContain(
                payload,
                $"{declaringType.Name}.{handler.Name} returns {payload.Name}, which no IDirectory "
                    + "method returns. Either serve it through IDirectory too, or move the endpoint to "
                    + "the application read surface (…Endpoints.Read) if it is UI data (ADR-008).");
        }
    }

    /// <summary>The published boundary's namespace — EXACTLY, so <c>…Endpoints.Read</c> is excluded.</summary>
    private const string BoundaryEndpointNamespace = "LeadingEDJE.Leap.Api.Modules.Compass.Endpoints";

    /// <summary>
    /// The published boundary's DTO namespace — the <c>Contracts</c> seam as of spec 013 (#451).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was <c>…Compass.Dtos</c>, and the exact match carried the whole of ADR-008's
    /// separation: it kept <c>…Dtos.Read</c> — the application read surface — out of the published
    /// contract. Once the published DTOs moved to <c>…Compass.Contracts</c> there is no
    /// <c>Contracts.Read</c> sibling, so an exact match on this constant alone no longer excludes
    /// anything and could no longer fail for the reason it was written. That is the fail-open shape
    /// this file's own remarks warn about.
    /// </para>
    /// <para>
    /// So the separation is now asserted directly by
    /// <see cref="ApplicationReadSurfaceNamespaceRoot"/>: a published payload must be in the seam AND
    /// must not be anywhere under the application read surface. Retargeting this string without
    /// adding that would have left a green assertion protecting nothing (spec 013 FR-005a).
    /// </para>
    /// </remarks>
    private const string PublishedDtoNamespace = "LeadingEDJE.Leap.Api.Modules.Compass.Contracts";

    /// <summary>
    /// The application read surface's namespace root. A published contract payload must never live
    /// under it — ADR-008 keeps the UI's projections distinct from the boundary's published types.
    /// </summary>
    private const string ApplicationReadSurfaceNamespaceRoot =
        "LeadingEDJE.Leap.Api.Modules.Compass.Dtos";

    /// <summary>Every handler on the published HTTP boundary, with the payload type it declares.</summary>
    /// <remarks>Handlers are private static methods; the public Map… extensions return a route builder.</remarks>
    private static IEnumerable<(Type DeclaringType, MethodInfo Handler, Type Payload)> BoundaryHandlers()
    {
        var endpointTypes = typeof(CompassEmployeeDto)
            .Assembly.GetTypes()
            .Where(t => t.Namespace == BoundaryEndpointNamespace
                        && t is { IsClass: true, IsAbstract: true, IsSealed: true });

        foreach (var type in endpointTypes)
        {
            var handlers = type
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);

            foreach (var handler in handlers)
            {
                var payload = UnwrapPayloadType(handler.ReturnType);
                if (payload.Namespace?.StartsWith("LeadingEDJE.Leap.Api.Modules.Compass", StringComparison.Ordinal) == true
                    && payload is { IsClass: true, IsAbstract: false })
                {
                    yield return (type, handler, payload);
                }
            }
        }
    }

    private static string[] MemberNames(Type type) =>
        [.. type.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// The given payload types, plus every published-seam type reachable from them through
    /// properties, transitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why the roots alone are not enough. A boundary method's declared payload is only the
    /// outermost type; <c>CompassEmployeeDto.Coach</c> and <c>.TimeTracking</c> are serialized inside
    /// <c>GET /api/compass/v1/employees/{id}</c> — the <c>RolePolicy.CompassAdmin</c>-gated route
    /// whose exposure was ADR-010's original disclosure defect — and a top-level property scan cannot
    /// see them. Measured before this walk existed: a <c>LegacyTpsId</c> on
    /// <see cref="CompassEmployeeCoachDto"/> left the whole unit suite green (3286 tests, 0 failed).
    /// </para>
    /// <para>
    /// Only the published seam is followed. A property whose type is a primitive, a
    /// framework type, or anything outside <see cref="PublishedDtoNamespace"/> is not expanded — the
    /// walk is a contract-surface walk, not a general object-graph walk, and expanding outward would
    /// eventually reach the whole framework. The <see cref="HashSet{T}"/> makes it terminate even if
    /// a cycle is ever introduced.
    /// </para>
    /// </remarks>
    private static List<Type> TransitivelyReachablePayloads(IEnumerable<Type> roots)
    {
        var reached = new List<Type>();
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>(roots);

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            if (!seen.Add(type))
            {
                continue;
            }

            reached.Add(type);

            foreach (var nested in type.GetProperties().Select(p => UnwrapPayloadType(p.PropertyType)))
            {
                if (nested.Namespace == PublishedDtoNamespace && !seen.Contains(nested))
                {
                    pending.Enqueue(nested);
                }
            }
        }

        return reached;
    }

    // Peels Task<...>, Results<...>, Ok<...> and Nullable<...> wrappers down to the payload type.
    private static Type UnwrapPayloadType(Type type)
    {
        while (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            // Results<Ok<T>, NotFound> — take the argument that itself carries a payload.
            var next = args.FirstOrDefault(a => a.IsGenericType) ?? args[0];
            type = next;

            if (!type.IsGenericType)
            {
                break;
            }
        }

        return Nullable.GetUnderlyingType(type) ?? type;
    }
}
