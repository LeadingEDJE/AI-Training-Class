namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// The closed set of reasons the sign-in pipeline can deny an identity, and the user-facing copy the
/// access-denied page renders for each.
/// </summary>
/// <remarks>
/// These strings are a contract, not labels: <c>web/timesheet/tests/e2e/saml/saml-login.spec.ts</c>
/// asserts <see cref="UnauthorizedDomain"/> and <see cref="InactivePersonRecord"/> verbatim against
/// the rendered page, so changing one means changing that spec in the same commit and running
/// <c>make test-e2e-saml</c> — the <c>verify</c> and <c>test-e2e</c> targets do not cover it. The
/// set is closed because the page reads its reason from the <c>msg</c> query parameter, which any
/// visitor can craft: encoding stops injected markup but not attacker-authored prose, and phishing
/// copy inside a genuine LE-branded page on the real domain is the risk. Unrecognised reasons
/// render the generic denial.
/// </remarks>
public static class SignInDenialReasons
{
    /// <summary>The email domain is not the authorized one.</summary>
    public const string UnauthorizedDomain = "Unauthorized domain";

    /// <summary>The assertion could not be read, or provisioning could not complete.</summary>
    public const string SignInFailed = "Sign-in failed";

    /// <summary>A person record exists but is deactivated; the denial survives any privileged group.</summary>
    public const string InactivePersonRecord = "Inactive Person Record";

    /// <summary>No person record could be resolved or created for the identity.</summary>
    public const string NoMatchingPersonRecord = "No Matching Person Record";

    /// <summary>Rendered when no reason is supplied, or the supplied one is not in <see cref="All"/>.</summary>
    public const string Generic = "Access denied";

    /// <summary>Every reason the pipeline produces. The page renders no others.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        UnauthorizedDomain,
        SignInFailed,
        InactivePersonRecord,
        NoMatchingPersonRecord,
    ];
}
