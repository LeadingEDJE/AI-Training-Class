using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Inner-loop coverage of the sign-in pipeline's identity resolution: the open-redirect return-url
/// guard, the person-provisioning outcomes (auto-create, inactive, rejected, throwing), display-name
/// persistence, and bootstrap-SuperAdmin survival across the role-sync reconcile.
/// </summary>
public class SignInServiceTests
{
    private const string Domain = "leadingedje.com";

    // Empty return-url options => local paths only (default behavior unchanged). Bootstrap options
    // default to EMPTY and the provisioning fake defaults to Rejected, so every pre-existing test
    // keeps its original behavior.
    private static SignInService CreateService(RecordingUserRoleService roles) =>
        CreateService(roles, new AuthReturnUrlOptions());

    private static SignInService CreateService(
        RecordingUserRoleService roles,
        AuthReturnUrlOptions returnUrlOptions,
        IPersonProvisioningService? personProvisioning = null,
        BootstrapAdminOptions? bootstrapOptions = null)
    {
        var google = new GoogleAuthService(Options.Create(new GoogleAuthOptions
        {
            AllowedDomain = Domain,
        }));
        return new SignInService(
            google,
            roles,
            NullLogger<SignInService>.Instance,
            Options.Create(returnUrlOptions),
            personProvisioning ?? FakePersonProvisioningService.RejectingEverything(),
            Options.Create(bootstrapOptions ?? new BootstrapAdminOptions()),
            new StubHostEnvironment());
    }

    /// <summary>Reports "Development" — only <see cref="EnvironmentName"/> is exercised here.</summary>
    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "LeadingEDJE.Leap.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // Drives SafeReturnUrl end-to-end: a successful sign-in returns SafeReturnUrl(returnUrl) as the
    // outcome Location, so asserting the Location proves the open-redirect guard's decision.
    private static async Task<string> ResolveLocationAsync(AuthReturnUrlOptions options, string? returnUrl)
    {
        var provisioning = FakePersonProvisioningService.Creating(Guid.NewGuid(), "Test User");
        var service = CreateService(new RecordingUserRoleService(), options, provisioning);
        var (context, _) = CreateContext();

        var outcome = await service.CompleteSignInAsync(
            "user@leadingedje.com", null, [], context, returnUrl);

        outcome.Succeeded.ShouldBeTrue();
        return outcome.Location;
    }

    private const string DevOrigin = "https://leap.dev.leadingedje.com";
    private const string DevSuffix = ".dev.leadingedje.com";

    [Fact]
    public async Task SafeReturnUrl_ExactAllowedOrigin_HonoredVerbatim()
    {
        // Arrange
        var options = new AuthReturnUrlOptions { AllowedReturnOrigins = [DevOrigin] };
        var returnUrl = DevOrigin + "/ooto/manage";

        // Act
        var location = await ResolveLocationAsync(options, returnUrl);

        // Assert
        location.ShouldBe(returnUrl);
    }

    [Fact]
    public async Task SafeReturnUrl_AllowedOrigin_HostCompareIsCaseInsensitive()
    {
        // Arrange — allowed origin is lower-case; returnUrl host differs only in case.
        var options = new AuthReturnUrlOptions { AllowedReturnOrigins = [DevOrigin] };
        var returnUrl = "https://LEAP.DEV.LEADINGEDJE.COM/reports";

        // Act
        var location = await ResolveLocationAsync(options, returnUrl);

        // Assert — Uri.Compare SchemeAndServer matches host case-insensitively.
        location.ShouldBe(returnUrl);
    }

    [Fact]
    public async Task SafeReturnUrl_DotPrefixedSuffix_MatchesDynamicPreviewHost()
    {
        // Arrange
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };
        var returnUrl = "https://app-leap-app-pr-7.dev.leadingedje.com/";

        // Act
        var location = await ResolveLocationAsync(options, returnUrl);

        // Assert
        location.ShouldBe(returnUrl);
    }

    [Fact]
    public async Task SafeReturnUrl_Suffix_RejectsLeadingDotBypass()
    {
        // Arrange — "evildev.leadingedje.com" would match a dotless suffix; the leading dot blocks it.
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };

        // Act
        var location = await ResolveLocationAsync(options, "https://evildev.leadingedje.com/");

        // Assert
        location.ShouldBe("/");
    }

    [Fact]
    public async Task SafeReturnUrl_Suffix_RejectsUnrelatedHost()
    {
        // Arrange
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };

        // Act
        var location = await ResolveLocationAsync(options, "https://evil.com/");

        // Assert
        location.ShouldBe("/");
    }

    [Fact]
    public async Task SafeReturnUrl_Suffix_RejectsHttpScheme()
    {
        // Arrange — https only; an http sibling is rejected even with a matching host suffix.
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };

        // Act
        var location = await ResolveLocationAsync(options, "http://app-x.dev.leadingedje.com/");

        // Assert
        location.ShouldBe("/");
    }

    [Fact]
    public async Task SafeReturnUrl_Suffix_RejectsUserinfoTrick()
    {
        // Arrange — the parsed Host of this URL is evil.com, not app-x.dev.leadingedje.com.
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };

        // Act
        var location = await ResolveLocationAsync(options, "https://app-x.dev.leadingedje.com@evil.com/");

        // Assert
        location.ShouldBe("/");
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    public async Task SafeReturnUrl_Suffix_StillRejectsLocalPathNegatives(string returnUrl)
    {
        // Arrange — existing CWE-601 local-path negatives keep falling back to "/".
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };

        // Act
        var location = await ResolveLocationAsync(options, returnUrl);

        // Assert
        location.ShouldBe("/");
    }

    [Fact]
    public async Task SafeReturnUrl_NoOptionsConfigured_RejectsAbsoluteSibling()
    {
        // Arrange — default behavior unchanged: with no sibling config, absolute URLs fall back to "/".
        var options = new AuthReturnUrlOptions();

        // Act
        var location = await ResolveLocationAsync(options, "https://app-x.dev.leadingedje.com/");

        // Assert
        location.ShouldBe("/");
    }

    [Fact]
    public async Task SafeReturnUrl_SuffixWithoutLeadingDot_IsInert()
    {
        // Arrange — a misconfigured (dotless) suffix must never activate the suffix rule.
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = "dev.leadingedje.com" };

        // Act
        var location = await ResolveLocationAsync(options, "https://app-x.dev.leadingedje.com/");

        // Assert
        location.ShouldBe("/");
    }

    [Fact]
    public async Task SafeReturnUrl_LocalPath_HonoredRegardlessOfOptions()
    {
        // Arrange — local paths are always honored (unchanged rule), even with sibling config present.
        var options = new AuthReturnUrlOptions { AllowedReturnHostSuffix = DevSuffix };

        // Act
        var location = await ResolveLocationAsync(options, "/ooto/manage");

        // Assert
        location.ShouldBe("/ooto/manage");
    }

    [Fact]
    public async Task SafeReturnUrl_NonMatchingAllowedOrigin_FallsThroughAndRejects()
    {
        // Arrange — a non-empty origin allow-list whose single entry does NOT match the target, so the
        // origin loop iterates to exhaustion (no early return) and the guard falls through to reject.
        var options = new AuthReturnUrlOptions { AllowedReturnOrigins = [DevOrigin] };

        // Act — a well-formed https URL on an unrelated host, no suffix configured.
        var location = await ResolveLocationAsync(options, "https://unrelated.example.com/x");

        // Assert
        location.ShouldBe("/");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsLocalUrl_NullOrEmpty_ReturnsFalse(string? url)
    {
        // The SafeReturnUrl caller guards empty before calling IsLocalUrl, so this defensive
        // null/empty branch is exercised directly (internal for test visibility).
        SignInService.IsLocalUrl(url).ShouldBeFalse();
    }

    private static (HttpContext Context, RecordingAuthenticationService Auth) CreateContext()
    {
        var auth = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return (context, auth);
    }

    // --- First-sign-in auto-create + bootstrap SuperAdmin survival (quick task 260729-sqc) ----------

    private const string Newcomer = "newbie@leadingedje.com";

    private static List<string> PrivilegesOf(RecordingAuthenticationService auth) =>
        auth.SignedInPrincipal!.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value).ToList();

    [Fact]
    public async Task CompleteSignIn_DomainValidatedEmailWithNoPersonRow_AutoCreatesAndSignsInWithRealEdjeId()
    {
        // Arrange — the directory knows nobody; provisioning mints the row (D-05).
        var edjeId = Guid.NewGuid();
        var provisioning = FakePersonProvisioningService.Creating(edjeId, "Auto Probe");
        var roles = new RecordingUserRoleService();
        var service = CreateService(roles, new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(Newcomer, "Auto Probe", [], context, returnUrl: null);

        // Assert — signed in, provisioned exactly once with the SAML auto-create provenance source.
        outcome.Succeeded.ShouldBeTrue();
        provisioning.Calls.Count.ShouldBe(1);
        provisioning.Calls[0].Email.ShouldBe(Newcomer);
        provisioning.Calls[0].DisplayName.ShouldBe("Auto Probe");
        provisioning.Calls[0].Source.ShouldBe(PersonSource.SamlAutoCreate);

        // Assert — the session carries a REAL EdjeId (so /api/me can never 500), plus the baseline role.
        var identity = auth.SignedInPrincipal!;
        identity.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim).ShouldBe(edjeId.ToString());
        identity.FindFirstValue(ClaimTypes.Email).ShouldBe(Newcomer);
        identity.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim).ShouldNotBeNullOrWhiteSpace();
        PrivilegesOf(auth).ShouldContain(RolePolicy.EDJEr);
        roles.SyncedRoles.ShouldContain(RolePolicy.EDJEr);
    }

    [Fact]
    public async Task CompleteSignIn_ProvisioningReportsInactive_DeniesInactivePersonRecordWithNoSessionOrRoleSync()
    {
        // Arrange — D-06 at the pipeline level: a deactivated person is never re-admitted.
        var provisioning = FakePersonProvisioningService.ReportingInactive();
        var roles = new RecordingUserRoleService();
        var service = CreateService(roles, new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(Newcomer, "Erin Ghost", [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("Inactive Person Record");
        auth.SignedInPrincipal.ShouldBeNull();
        roles.SyncedRoles.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompleteSignIn_ProvisioningReportsRejected_PreservesNoMatchingPersonRecordDenial()
    {
        // Arrange — the pre-existing deny wording is kept for any un-provisionable case.
        var provisioning = FakePersonProvisioningService.RejectingEverything();
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(Newcomer, null, [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("No Matching Person Record");
        auth.SignedInPrincipal.ShouldBeNull();
    }

    [Fact]
    public async Task CompleteSignIn_ProvisioningThrows_DeniesSignInFailedWithNoSession()
    {
        // Arrange
        var provisioning = FakePersonProvisioningService.Throwing();
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(Newcomer, null, [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("Sign-in failed");
        auth.SignedInPrincipal.ShouldBeNull();
    }

    [Fact]
    public async Task CompleteSignIn_ForeignDomain_NeverReachesProvisioning()
    {
        // Arrange — T-sqc-01: the domain gate is the ONLY thing between "any Google account on the
        // internet" and a row in `people`, so it must run before provisioning can ever be reached.
        var provisioning = FakePersonProvisioningService.Creating(Guid.NewGuid(), "Intruder");
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync("intruder@gmail.com", "Intruder", [], context, null);

        // Assert
        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("Unauthorized domain");
        auth.SignedInPrincipal.ShouldBeNull();
        provisioning.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("	")]
    public async Task CompleteSignIn_BlankEmail_DeniesUnauthorizedDomain_NotNoMatchingPersonRecord(
        string? email)
    {
        // WHY THIS PINS AN ORDERING RATHER THAN A MESSAGE (issue #510).
        //
        // `No Matching Person Record` is UNREACHABLE through the real pipeline, and this test is what
        // keeps that true. The chain is short and entirely dependent on statement order:
        //
        //   1. PersonProvisioningService returns `Rejected` on exactly ONE condition — a blank email.
        //   2. `Rejected` is the only outcome that is neither Created, Existing nor Inactive, so it is
        //      the only thing that can reach the NoMatchingPersonRecord branch in SignInService.
        //   3. But SignInService's FIRST statement rejects a blank email as `Unauthorized domain`,
        //      so provisioning never sees one, so `Rejected` never happens.
        //
        // HONEST SCOPE, measured while writing this. Step 3 turns out to be guarded THREE times over,
        // and the third is the strongest:
        //
        //   a. SignInService's own blank check, above.
        //   b. GoogleAuthService.ValidateDomain rejects a blank email independently.
        //   c. The NULLABLE REFERENCE TYPE SYSTEM. `email` is `string?`, and every downstream
        //      consumer takes `string`, so moving the gate below provisioning DOES NOT COMPILE —
        //      verified by trying it: CS8604 on the `ValidateDomain` call.
        //
        // So this is a CHARACTERIZATION test, not the load-bearing gate, and it is worth saying so
        // rather than implying it catches something it cannot. It earns its place for the case where
        // (c) goes away — someone makes `email` non-nullable in a refactor — at which point (a) and
        // (b) become the only guards and this is what notices if one is dropped.
        //
        // Asserting `Calls.ShouldBeEmpty()` is still the load-bearing half here: a test that only
        // checked the deny reason would pass even if provisioning ran first, because both orderings
        // produce *a* denial — just different ones.

        // Arrange
        var provisioning = FakePersonProvisioningService.Creating(Guid.NewGuid(), "Nobody");
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(email, "Nobody", [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe(
            "Unauthorized domain",
            "a blank email must be caught by the domain gate. If this now says 'No Matching Person "
                + "Record', the gate no longer runs first and that denial has become reachable — "
                + "add the SAML end-to-end assertion issue #510 asked for.");
        auth.SignedInPrincipal.ShouldBeNull();
        provisioning.Calls.ShouldBeEmpty(
            "provisioning must never see a blank email; that it cannot is the whole reason "
                + "PersonProvisionOutcome.Rejected is unreachable in production.");
    }

    // ---- Display-name persistence (smoke-test finding P2, 2026-07-30) ------------------------------
    // The deployed shell greeted "Welcome, avery.quinn@example.com" and Admin -> People showed a
    // blank NAME for all four bootstrap admins. Cause: the bootstrap seeder creates rows with no name,
    // and provisioning substitutes the EMAIL for a blank display name. BuildIdentity then saw a
    // NON-EMPTY resolved display name, so its "else fall back to the assertion name" branch was
    // unreachable and Google's real name was thrown away every sign-in.

    [Fact]
    public async Task CompleteSignIn_PersonRowHasNoName_StampsTheAssertionNameAndPersistsIt()
    {
        // Arrange — a nameless row, exactly as the bootstrap seeder leaves it: EnsurePersonAsync
        // substitutes the email for the missing display name (Existing's `displayName ?? email`).
        var edjeId = Guid.NewGuid();
        var provisioning = FakePersonProvisioningService.Existing(edjeId, "avery.quinn@leadingedje.com");
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(
            "avery.quinn@leadingedje.com", "Avery Quinn", [], context, returnUrl: null);

        // Assert — the claim carries the real name, NOT the email standing in for one.
        outcome.Succeeded.ShouldBeTrue();
        auth.SignedInPrincipal!
            .FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
            .ShouldBe("Avery Quinn");

        // Assert — and it was handed to the provisioning service to WRITE, so Admin -> People shows it
        // too. Without this the name would be correct for one session and blank in the people list.
        provisioning.DisplayNameCalls.Count.ShouldBe(1);
        provisioning.DisplayNameCalls[0].EdjeId.ShouldBe(edjeId);
        provisioning.DisplayNameCalls[0].AssertionName.ShouldBe("Avery Quinn");
        provisioning.DisplayNameCalls[0].EmailFallback.ShouldBe("avery.quinn@leadingedje.com");
    }

    [Fact]
    public async Task CompleteSignIn_PersistedNameDiffersFromAssertion_StampsThePersistedName()
    {
        // Arrange — provisioning is the authority on what the row ends up holding (it refuses to
        // overwrite an HR-owned name), so the session must carry what it returns, not the raw assertion.
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "blair.dev@leadingedje.com");
        provisioning.DisplayNameToReturn = "Blair Dev";
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(
            "blair.dev@leadingedje.com", "Blair Nickname Dev", [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        auth.SignedInPrincipal!
            .FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
            .ShouldBe("Blair Dev");
    }

    [Fact]
    public async Task CompleteSignIn_AutoCreatedPerson_AlsoPersistsTheAssertionName()
    {
        // Arrange — the auto-create path splits the assertion name on INSERT, but a returning
        // auto-created user must still flow through the same persistence call (one code path, not two).
        var edjeId = Guid.NewGuid();
        var provisioning = FakePersonProvisioningService.Creating(edjeId, "Auto Probe");
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(Newcomer, "Auto Probe", [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        provisioning.DisplayNameCalls.Count.ShouldBe(1);
        provisioning.DisplayNameCalls[0].EdjeId.ShouldBe(edjeId);
        provisioning.DisplayNameCalls[0].AssertionName.ShouldBe("Auto Probe");
        auth.SignedInPrincipal!
            .FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
            .ShouldBe("Auto Probe");
    }

    [Fact]
    public async Task CompleteSignIn_NoAssertionName_StillStampsANonEmptyDisplayNameClaim()
    {
        // Arrange — an IdP that sends no name must not produce a blank greeting; the email is the
        // documented last resort (D-08 — identity only, never a fabricated name).
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "nameless@leadingedje.com");
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(
            "nameless@leadingedje.com", name: null, [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        auth.SignedInPrincipal!
            .FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
            .ShouldBe("nameless@leadingedje.com");
    }

    [Fact]
    public async Task CompleteSignIn_ExistingActivePerson_CallsProvisioningOnceAndSignsIn()
    {
        // Arrange — EnsurePersonAsync is the single find-or-create call for every sign-in (no separate
        // directory read ahead of it, unlike the retired TPS-backed pipeline).
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "known@leadingedje.com", "Known User");
        var service = CreateService(new RecordingUserRoleService(), new AuthReturnUrlOptions(), provisioning);
        var (context, _) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync("known@leadingedje.com", null, [], context, null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        provisioning.Calls.Count.ShouldBe(1);
        provisioning.Calls[0].Email.ShouldBe("known@leadingedje.com");
    }

    [Fact]
    public async Task CompleteSignIn_BootstrapConfiguredEmailWithZeroGroups_StillHoldsSuperAdmin()
    {
        // Arrange — THE regression guard for the reconcile trap: SyncFromProfileAsync DELETES every
        // user_roles row absent from the desired set, so the bootstrap seeder's SuperAdmin grant is
        // destroyed on first login unless BuildRoles re-asserts it here (D-02).
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "boss@leadingedje.com");
        var roles = new RecordingUserRoleService();
        var service = CreateService(
            roles, new AuthReturnUrlOptions(), provisioning,
            new BootstrapAdminOptions { SuperAdmins = ["boss@leadingedje.com"] });
        var (context, auth) = CreateContext();

        // Act — ZERO groups in the assertion.
        var outcome = await service.CompleteSignInAsync("boss@leadingedje.com", null, [], context, null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        roles.SyncedRoles.ShouldContain(RolePolicy.SuperAdmin);
        PrivilegesOf(auth).ShouldContain(RolePolicy.SuperAdmin);
    }

    [Fact]
    public async Task CompleteSignIn_BootstrapEmailDifferingByCaseAndWhitespace_StillGrantsSuperAdmin()
    {
        // Arrange
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "boss@leadingedje.com");
        var roles = new RecordingUserRoleService();
        var service = CreateService(
            roles, new AuthReturnUrlOptions(), provisioning,
            new BootstrapAdminOptions { SuperAdmins = ["  BOSS@LeadingEDJE.com  "] });
        var (context, _) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync("boss@leadingedje.com", null, [], context, null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        roles.SyncedRoles.ShouldContain(RolePolicy.SuperAdmin);
    }

    [Fact]
    public async Task CompleteSignIn_EmailNotInBootstrapList_GetsEdjErBaselineWithoutSuperAdmin()
    {
        // Arrange — the config grant must not be a blanket elevation.
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "peon@leadingedje.com");
        var roles = new RecordingUserRoleService();
        var service = CreateService(
            roles, new AuthReturnUrlOptions(), provisioning,
            new BootstrapAdminOptions { SuperAdmins = ["boss@leadingedje.com"] });
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync("peon@leadingedje.com", null, [], context, null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        roles.SyncedRoles.ShouldBe([RolePolicy.EDJEr]);
        PrivilegesOf(auth).ShouldBe([RolePolicy.EDJEr]);
    }

    [Fact]
    public async Task CompleteSignIn_BootstrapOptionsUnset_GrantsNoSuperAdminToAnyone()
    {
        // Arrange — byte-identical to today when the feature is not configured (D-04).
        var provisioning = FakePersonProvisioningService.Existing(Guid.NewGuid(), "boss@leadingedje.com");
        var roles = new RecordingUserRoleService();
        var service = CreateService(roles, new AuthReturnUrlOptions(), provisioning);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync("boss@leadingedje.com", null, [], context, null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        roles.SyncedRoles.ShouldNotContain(RolePolicy.SuperAdmin);
        PrivilegesOf(auth).ShouldNotContain(RolePolicy.SuperAdmin);
    }

    /// <summary>Records provisioning calls and replays a scripted outcome (or throws).</summary>
    private sealed class FakePersonProvisioningService(
        Func<string?, string?, string, PersonProvisionResult> respond) : IPersonProvisioningService
    {
        public List<(string? Email, string? DisplayName, string Source)> Calls { get; } = [];

        /// <summary>Display-name persistence calls (P2): what SignInService asked to be stored, and for whom.</summary>
        public List<(Guid EdjeId, string? AssertionName, string EmailFallback)> DisplayNameCalls { get; } = [];

        /// <summary>What <see cref="EnsureDisplayNameAsync"/> returns — the name the session claim must carry.</summary>
        public string? DisplayNameToReturn { get; set; }

        public Task<PersonProvisionResult> EnsurePersonAsync(string? email, string? displayName, string source)
        {
            Calls.Add((email, displayName, source));
            return Task.FromResult(respond(email, displayName, source));
        }

        public Task<string> EnsureDisplayNameAsync(
            Guid edjeId, string? assertionName, string resolvedDisplayName)
        {
            DisplayNameCalls.Add((edjeId, assertionName, resolvedDisplayName));
            return Task.FromResult(
                DisplayNameToReturn
                ?? (string.IsNullOrWhiteSpace(assertionName) ? resolvedDisplayName : assertionName));
        }

        public Task TryFillMissingDisplayNameAsync(Guid edjeId, string? candidateName) =>
            Task.CompletedTask;

        public static FakePersonProvisioningService Creating(Guid edjeId, string displayName) =>
            new((email, _, _) => PersonProvisionResult.ForCreated(edjeId, email ?? string.Empty, displayName));

        /// <summary>An already-known active row, mirroring PersonProvisioningService's own
        /// email-substitution fallback when <paramref name="displayName"/> is unset.</summary>
        public static FakePersonProvisioningService Existing(Guid edjeId, string email, string? displayName = null) =>
            new((_, _, _) => PersonProvisionResult.ForExisting(edjeId, email, displayName ?? email));

        public static FakePersonProvisioningService ReportingInactive() =>
            new((email, _, _) => PersonProvisionResult.ForInactive(email ?? string.Empty));

        public static FakePersonProvisioningService RejectingEverything() =>
            new((_, _, _) => PersonProvisionResult.ForRejected());

        public static FakePersonProvisioningService Throwing() =>
            new((_, _, _) => throw new InvalidOperationException("provisioning blew up"));
    }

    /// <summary>Fake auth service capturing the session sign-in so the stamped claims can be asserted.</summary>
    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignedInPrincipal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    /// <summary>Records the sign-in role sync; all other members are unused no-ops.</summary>
    private sealed class RecordingUserRoleService : IUserRoleService
    {
        public List<string> SyncedRoles { get; } = [];

        public Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync")
        {
            SyncedRoles.AddRange(privileges);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> HasRoleAsync(Guid edjeId, string role) => Task.FromResult(false);
        public Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles) => Task.FromResult(false);
        public Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
        public Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees) =>
            Task.FromResult(0);
    }
}
