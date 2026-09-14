using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Contract D — anti-drift parity between the three boundary detectors' <c>Compass.Contracts</c>
/// exemption (spec 013, #451).
/// </summary>
/// <remarks>
/// <para>
/// Why this file exists at all. The three detectors anchor on three DIFFERENT strings —
/// <see cref="DirectoryConsumerBoundaryTests.CompassModuleReference"/>'s full namespace regex,
/// <see cref="CompassBoundaryTests.TextReferencesCompass"/>'s short-token regex, and
/// <see cref="CompassBoundaryTests.IsCompassInternal"/>'s reflection predicate — so they cannot share
/// one literal, and nothing else stops one being loosened or tightened alone. <see cref="ThePublishedContractExemption_AgreesAcrossDetectors1And2"/>
/// (E1) asserts D1 and D2 return identical verdicts across the whole Contract A battery;
/// <see cref="Detector3_AgreesWithDetectors1And2_OnTheCasesExpressibleInBothForms"/> (E2) asserts D3
/// agrees on the subset expressible as a reflected type or a bare namespace string.
/// </para>
/// <para>
/// Verified by construction (T035). A gate that has never failed has never run: this parity
/// test was confirmed to fail when detector 2's regex was temporarily loosened to admit
/// <c>ContractsInternal</c> as well as <c>Contracts</c>, then reverted. See
/// <c>evidence/T035-parity-verified-by-construction.md</c>.
/// </para>
/// </remarks>
public class CompassContractsExemptionParityTests
{
    private sealed record Case(string Text, bool ExpectFlag);

    /// <summary>
    /// The entire Contract A battery (gate-contract.md), held here as its OWN copy rather than shared
    /// with <c>DirectoryConsumerBoundaryTests</c> or <c>CompassBoundaryTests</c> — see the class
    /// remarks on why the three detectors deliberately do not share one literal. A third copy that
    /// agrees with the other two is what this file exists to prove; a shared constant would remove the
    /// thing being tested.
    /// </summary>
    private static readonly Case[] ContractABattery =
    [
        // ---- A1-A5: MUST NOT FLAG ----
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;", false),
        new(
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n\r\n"
                + "namespace LeadingEDJE.Leap.Api.Modules.Ooto;\r\n\r\n"
                + "internal sealed class Consumer(IDirectory directory);\r\n",
            false),
        new("private readonly LeadingEDJE.Leap.Api.Modules.Compass.Contracts.IDirectory _directory;", false),
        new("using Dir = LeadingEDJE.Leap.Api.Modules.Compass.Contracts.IDirectory;", false),
        new(
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n"
                + "// see LeadingEDJE.Leap.Api.Modules.Compass.Contracts.CompassEmployeeDto\r\n",
            false),

        // ---- B1-B12, B16: MUST FLAG ----
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Services;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Data;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass;", true),
        new("new LeadingEDJE.Leap.Api.Modules.Compass.Employee();", true),
        new("global::LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService x;", true),
        new("using Svc = LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService;", true),
        new("List<LeadingEDJE.Leap.Api.Modules.Compass.Employee> employees;", true),
        new("Type.GetType(\"LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService\");", true),
        new("/// <see cref=\"LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService\"/>", true),
        new(
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n"
                + "using LeadingEDJE.Leap.Api.Modules.Compass.Services;\r\n",
            true),

        // ---- B13-B15: MUST FLAG — the same-word-prefix and format-character traps ----
        new("using LeadingEDJE.Leap.Api.Modules.Compass.ContractsFoo;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass.ContractsInternal;", true),
        new("using LeadingEDJE.Leap.Api.Modules.Compass.Contracts­Internal;", true),
    ];

    /// <summary>A6 — every legal terminator after <c>Contracts</c> keeps the exemption, on both detectors.</summary>
    private static readonly string[] Terminators = [";", ".", ")", ",", ">", "=", "{", " ", "", "\r\n"];

    // ------------------------------------------------------- E1: D1 and D2 agree

    [Fact]
    public void ThePublishedContractExemption_AgreesAcrossDetectors1And2()
    {
        // Arrange
        ContractABattery.Length.ShouldBeGreaterThan(
            0, "the parity battery is empty — this test would pass for free");

        // Act & Assert
        foreach (var @case in ContractABattery)
        {
            var d1 = DirectoryConsumerBoundaryTests.CompassModuleReference.IsMatch(@case.Text);
            var d2 = CompassBoundaryTests.TextReferencesCompass(@case.Text);

            d1.ShouldBe(
                @case.ExpectFlag,
                $"detector 1 disagreed with the expected verdict for: {@case.Text}");
            d2.ShouldBe(
                @case.ExpectFlag,
                $"detector 2 disagreed with the expected verdict for: {@case.Text}");
            d1.ShouldBe(
                d2,
                "detectors 1 and 2 disagree with EACH OTHER, which is exactly the drift Contract D "
                    + $"exists to catch, on: {@case.Text}");
        }
    }

    [Fact]
    public void ThePublishedContractExemption_AgreesAcrossDetectors1And2_ForEveryLegalTerminator()
    {
        // Arrange
        const string Reference = "LeadingEDJE.Leap.Api.Modules.Compass.Contracts";

        // Act & Assert
        foreach (var terminator in Terminators)
        {
            var text = Reference + terminator;
            var d1 = DirectoryConsumerBoundaryTests.CompassModuleReference.IsMatch(text);
            var d2 = CompassBoundaryTests.TextReferencesCompass(text);

            d1.ShouldBeFalse($"detector 1 flagged the published contract before terminator '{terminator}'");
            d2.ShouldBeFalse($"detector 2 flagged the published contract before terminator '{terminator}'");
            d1.ShouldBe(d2, $"detectors 1 and 2 disagree on terminator '{terminator}'");
        }
    }

    // ------------------------------------------------- E2: D3 agrees where expressible

    /// <summary>
    /// D3 agrees with D1/D2 on the two cases expressible in reflection form: A1 ↔ C1 (the published
    /// contract is referenceable) and B13/B14 ↔ C8 (a same-word-prefix namespace is still internal).
    /// </summary>
    /// <remarks>
    /// Most of Contract A's battery has no reflection equivalent — a <c>using</c> alias or an XML
    /// <c>cref</c> produces no declared-signature type at all — so D3 cannot agree or disagree with
    /// them; this asserts only the subset where a comparison is meaningful, per gate-contract.md's
    /// own scoping of Contract D's E2 row.
    /// </remarks>
    [Fact]
    public void Detector3_AgreesWithDetectors1And2_OnTheCasesExpressibleInBothForms()
    {
        // ---- A1 ↔ C1: the published contract type itself must not be flagged ----
        const string A1 = "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;";
        DirectoryConsumerBoundaryTests.CompassModuleReference.IsMatch(A1).ShouldBeFalse();
        CompassBoundaryTests.TextReferencesCompass(A1).ShouldBeFalse();

        CompassBoundaryTests.IsCompassInternal(typeof(IDirectory)).ShouldBeFalse(
            "detector 3 disagrees with detectors 1 and 2: A1's published-contract text is allowed, "
                + "but C1's reflected IDirectory type is still treated as a Compass internal");

        // ---- B13/B14 ↔ C8: a same-word-prefix namespace stays internal, in every form ----
        const string B13 = "using LeadingEDJE.Leap.Api.Modules.Compass.ContractsFoo;";
        const string B14 = "using LeadingEDJE.Leap.Api.Modules.Compass.ContractsInternal;";
        DirectoryConsumerBoundaryTests.CompassModuleReference.IsMatch(B13).ShouldBeTrue();
        DirectoryConsumerBoundaryTests.CompassModuleReference.IsMatch(B14).ShouldBeTrue();
        CompassBoundaryTests.TextReferencesCompass(B13).ShouldBeTrue();
        CompassBoundaryTests.TextReferencesCompass(B14).ShouldBeTrue();

        CompassBoundaryTests.IsPublishedContractNamespace(
            "LeadingEDJE.Leap.Api.Modules.Compass.ContractsFoo").ShouldBeFalse(
                "detector 3 disagrees with detectors 1 and 2: B13's namespace is flagged as internal "
                    + "by the text detectors but C8's reflection form would treat it as published");
        CompassBoundaryTests.IsPublishedContractNamespace(
            "LeadingEDJE.Leap.Api.Modules.Compass.ContractsInternal").ShouldBeFalse(
                "detector 3 disagrees with detectors 1 and 2: B14's namespace is flagged as internal "
                    + "by the text detectors but C8's reflection form would treat it as published");
    }
}
