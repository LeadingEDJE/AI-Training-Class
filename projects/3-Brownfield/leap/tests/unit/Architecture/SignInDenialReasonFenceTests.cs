using LeadingEDJE.Leap.Api.Platform.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Fences the closed set of sign-in denial reasons against the call sites that produce them.
/// </summary>
/// <remarks>
/// <para>
/// Why a source scan rather than a set comparison. The first version of this test asserted
/// <see cref="SignInDenialReasons.All"/> against a second hardcoded list. That pins the constants
/// against edits to themselves and nothing else: a new
/// <c>DenyAsync(httpContext, "Some New Reason")</c> written with a raw string would have sailed
/// past it, which is the exact drift the allowlist needs protecting from. Review caught it on
/// PR #484.
/// </para>
/// <para>
/// What the drift costs, so the fence is not mistaken for tidiness.
/// <c>AccessDeniedPage</c> renders only reasons in <see cref="SignInDenialReasons.All"/>; anything
/// else falls back to the generic denial. So an unregistered reason does not fail — it silently
/// degrades that denial to "Access denied", losing the specific guidance the branded page exists to
/// give. Nothing else in the suite would go red.
/// </para>
/// </remarks>
public class SignInDenialReasonFenceTests
{
    private const string SignInServicePath = "api/Platform/Services/SignInService.cs";

    /// <summary>The only source that can produce <c>PersonProvisionOutcome.Rejected</c>.</summary>
    private const string ProvisioningServicePath =
        "api/Platform/Services/PersonProvisioningService.cs";

    /// <summary>The only shape a denial may be raised in: a constant, never a literal.</summary>
    private const string ExpectedCallPrefix = "DenyAsync(httpContext, SignInDenialReasons.";

    [Fact]
    public void EveryDenyAsyncCallSite_PassesAConstant_NeverARawStringLiteral()
    {
        // Arrange
        var source = ReadSignInService();

        // Act — every DenyAsync call, and the subset written the sanctioned way.
        var totalCalls = CountOccurrences(source, "DenyAsync(httpContext, ");
        var constantCalls = CountOccurrences(source, ExpectedCallPrefix);

        // Assert — non-vacuity FIRST. A scan that matches nothing passes while measuring nothing,
        // which is the failure that hides every other one.
        totalCalls.ShouldBeGreaterThan(
            0,
            $"found no DenyAsync call sites in {SignInServicePath}. Either the pipeline was "
                + "refactored or this fence is now scanning for a shape that no longer exists — "
                + "fix the fence rather than deleting it."
        );

        constantCalls.ShouldBe(
            totalCalls,
            $"{totalCalls - constantCalls} of {totalCalls} DenyAsync call site(s) in "
                + $"{SignInServicePath} pass something other than a SignInDenialReasons constant. A "
                + "raw string there is not rejected — AccessDeniedPage silently renders the GENERIC "
                + "denial instead, losing the specific guidance. Add the reason to "
                + "SignInDenialReasons (and give it copy in AccessDeniedPage) and pass the constant."
        );
    }

    [Fact]
    public void EveryConstantInAll_IsActuallyRaisedBySignInService()
    {
        // The other direction: a constant nobody raises is dead copy on the denial page, and it
        // makes `All` look like it documents the pipeline when it no longer does.

        // Arrange
        var source = ReadSignInService();

        // Act + Assert
        SignInDenialReasons.All.Count.ShouldBeGreaterThan(0, "the allowlist must not be empty.");

        foreach (var reason in SignInDenialReasons.All)
        {
            var constantName = NameOf(reason);
            source.ShouldContain(
                ExpectedCallPrefix + constantName,
                customMessage: $"SignInDenialReasons.{constantName} (\"{reason}\") is in the "
                    + $"allowlist but no DenyAsync call in {SignInServicePath} raises it. Either "
                    + "the pipeline stopped producing it — in which case remove it and its copy "
                    + "from AccessDeniedPage — or it is raised in a shape this fence cannot see."
            );
        }
    }

    [Fact]
    public void PersonProvisioning_RejectsForExactlyOneReason_SoTheThirdDenialStaysUnreachable()
    {
        // ISSUE #510. `No Matching Person Record` is asserted only by unit tests against doubles, not
        // by a SAML end-to-end test — which is fine only while that denial stays unreachable through
        // the real pipeline. The chain is two links long:
        //
        //   1. `PersonProvisionOutcome.Rejected` is the ONLY outcome that is neither Created,
        //      Existing nor Inactive, so it is the only thing that can reach the
        //      NoMatchingPersonRecord branch in SignInService.
        //   2. PersonProvisioningService returns it on exactly ONE condition — a blank email — and
        //      SignInService rejects a blank email before provisioning is ever called.
        //
        // Link 2's ORDERING half is guarded three times over, including by the nullable reference
        // type system (see SignInServiceTests.CompleteSignIn_BlankEmail_...). Its COUNT half is
        // guarded by nothing but this test: add a second `ForRejected()` for some other condition —
        // a malformed address, a banned domain, a provisioning conflict — and the denial becomes
        // live for a real identity, reopening the gap #510 originally described.
        //
        // This is deliberately a SOURCE scan rather than a behavioural test: "how many ways can this
        // return Rejected" is a property of the code's shape, and enumerating inputs could never
        // prove the absence of a second trigger.

        // Arrange
        var source = ReadRepositoryFile(ProvisioningServicePath);

        // Act
        var rejectSites = CountOccurrences(source, "ForRejected(");

        // Assert — non-vacuity first: a renamed factory method would read as zero and pass forever.
        rejectSites.ShouldBeGreaterThan(
            0,
            $"found no `ForRejected(` in {ProvisioningServicePath}. Either the factory was renamed — "
                + "update this fence — or the rejection path is gone, in which case "
                + "PersonProvisionOutcome.Rejected is dead and should be deleted along with the "
                + "NoMatchingPersonRecord branch it feeds."
        );

        rejectSites.ShouldBe(
            1,
            $"{ProvisioningServicePath} now returns Rejected from {rejectSites} places. Only a blank "
                + "email is pre-empted by SignInService's domain gate, so any OTHER trigger makes "
                + "`No Matching Person Record` reachable for a real identity. That denial is asserted "
                + "only by unit tests against doubles — add the SAML end-to-end assertion this now needs."
        );

        // The one site must still be the blank-email condition the reasoning above depends on.
        source.ShouldContain(
            "if (string.IsNullOrWhiteSpace(email))",
            Case.Sensitive,
            customMessage: "the single rejection is no longer keyed on a blank email, so the domain "
                + "gate no longer pre-empts it."
        );
    }

    /// <summary>Maps a reason's value back to the constant that declares it.</summary>
    private static string NameOf(string reason) => reason switch
    {
        SignInDenialReasons.UnauthorizedDomain => nameof(SignInDenialReasons.UnauthorizedDomain),
        SignInDenialReasons.SignInFailed => nameof(SignInDenialReasons.SignInFailed),
        SignInDenialReasons.InactivePersonRecord => nameof(SignInDenialReasons.InactivePersonRecord),
        SignInDenialReasons.NoMatchingPersonRecord => nameof(
            SignInDenialReasons.NoMatchingPersonRecord
        ),
        _ => throw new InvalidOperationException(
            $"'{reason}' is in SignInDenialReasons.All but this test cannot name it. Add it here "
                + "so the fence keeps covering every entry rather than quietly skipping one."
        ),
    };

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);

        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath}. A scan over a file that is not there passes vacuously, so "
                + "this fails loudly instead."
        );

        return File.ReadAllText(path);
    }

    private static string ReadSignInService()
    {
        var path = Path.Combine(RepositoryRoot(), SignInServicePath);

        File.Exists(path).ShouldBeTrue(
            $"expected the sign-in pipeline at '{path}'. A scan over a file that is not there "
                + "passes vacuously, so this fails loudly instead."
        );

        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "leap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "The sign-in denial-reason fence found nothing to inspect: no repository root containing "
                    + $"leap.slnx above '{AppContext.BaseDirectory}'.");
    }
}
