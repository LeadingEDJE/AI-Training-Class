using System.Reflection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// The AWS STS assembly must be present at runtime, because IRSA needs it and nothing else notices
/// when it is missing.
/// </summary>
/// <remarks>
/// The pod authenticates to SES (and S3) through its IRSA role: a web-identity token exchanged via
/// STS <c>AssumeRoleWithWebIdentity</c>. The AWS SDK loads <c>AWSSDK.SecurityToken</c> by REFLECTION
/// at that moment (<c>ServiceClientHelpers.GetSDKAssembly</c> → <c>Assembly.Load</c>), so nothing
/// references it at compile time and the publish graph never includes it unless a
/// <c>PackageReference</c> forces it. Without it every AWS call that needs credentials throws
/// <c>FileNotFoundException: Could not load file or assembly 'AWSSDK.SecurityToken'</c> before a byte
/// leaves the pod — observed live in dev, where every coach-notice email failed on exactly this.
/// This test load-probes the assembly the same way the runtime does, so its absence is a red test
/// rather than a production incident.
/// </remarks>
public class AwsSecurityTokenAssemblyTests
{
    [Fact]
    public void TheStsAssembly_LoadsAtRuntime_SoIrsaCredentialResolutionCanSucceed()
    {
        // Act — the exact call the SDK makes to reach AssumeRoleWithWebIdentityCredentials.CreateClient.
        var loaded = Should.NotThrow(
            () => Assembly.Load("AWSSDK.SecurityToken"),
            "AWSSDK.SecurityToken is reflection-loaded for STS AssumeRoleWithWebIdentity; if it is not "
                + "referenced it is absent from the published image and every IRSA-authenticated AWS "
                + "call (SES, S3) fails at credential resolution");

        // Assert
        loaded.ShouldNotBeNull();
    }
}
