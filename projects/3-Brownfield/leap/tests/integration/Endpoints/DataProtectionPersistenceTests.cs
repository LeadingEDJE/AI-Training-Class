using System.Net;
using System.Text.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Proves the cookie session survives an API replica switch. A cookie is encrypted with the Data
/// Protection key ring; if that ring is per-instance (the ASP.NET default — ephemeral, in-memory),
/// a second replica cannot decrypt a cookie the first replica issued, so users are silently logged
/// out whenever the load balancer routes them to a different pod or a pod restarts. The fix is to
/// persist the key ring to MySQL (<c>PersistKeysToDbContext&lt;LeapDbContext&gt;</c>) and pin
/// the application name so every replica shares one ring.
///
/// This fixture owns a single PostgreSQL container and stands up TWO independent Program.cs hosts
/// against it (each with its own in-memory Data Protection cache), simulating two replicas behind one LB.
/// </summary>
public sealed class DataProtectionPersistenceTests : IAsyncLifetime
{
    private const string SessionCookiePrefix = "Timesheet=";
    private const string ExternalCookiePrefix = "Timesheet-External=";
    private const string SuperAdminGroup = "Timesheet-SuperAdmin-dev";

    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16")
        .WithUsername("timesheet")
        .WithPassword("testpass")
        .WithDatabase("timesheet_test")
        .Build();

    private DataProtectionSignInFactory _instanceOne = null!;
    private DataProtectionSignInFactory _instanceTwo = null!;
    private readonly string _contentRootOne = Path.Combine(Path.GetTempPath(), $"dp-replica-1-{Guid.NewGuid():N}");
    private readonly string _contentRootTwo = Path.Combine(Path.GetTempPath(), $"dp-replica-2-{Guid.NewGuid():N}");

    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAndAwaitHostConnectivityAsync();

        var optionsBuilder = new DbContextOptionsBuilder<LeapDbContext>();
        optionsBuilder.UseNpgsql(_postgresContainer.GetConnectionString())
                      .UseSnakeCaseNamingConvention();
        await PostgreSqlContainerExtensions.MigrateWithRetryAsync(async () =>
        {
            await using var migrationContext = new LeapDbContext(optionsBuilder.Options);
            await migrationContext.Database.MigrateAsync();
        });

        Directory.CreateDirectory(_contentRootOne);
        Directory.CreateDirectory(_contentRootTwo);
        _instanceOne = new DataProtectionSignInFactory(_postgresContainer.GetConnectionString(), _contentRootOne);
        _instanceTwo = new DataProtectionSignInFactory(_postgresContainer.GetConnectionString(), _contentRootTwo);
        // Force each host to build so the DI container and middleware pipeline are ready.
        _ = _instanceOne.Services;
        _ = _instanceTwo.Services;
    }

    public async ValueTask DisposeAsync()
    {
        await _instanceOne.DisposeAsync();
        await _instanceTwo.DisposeAsync();
        await _postgresContainer.DisposeAsync();
        try
        {
            Directory.Delete(_contentRootOne, recursive: true);
            Directory.Delete(_contentRootTwo, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone — nothing to clean up.
        }
    }

    [Fact]
    public async Task SignIn_PersistsDataProtectionKeyToMySql()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email.
        var email = $"dpkeys+{Guid.NewGuid():N}@leadingedje.com";

        // Act — a successful sign-in issues an ENCRYPTED session cookie, forcing the Data Protection
        // key ring to be created and (once persistence is wired) written to the shared database.
        var response = await SignInViaCallbackAsync(_instanceOne, email, SuperAdminGroup);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        ExtractSessionCookie(response).ShouldNotBeNull();

        // Assert — the key ring is durable in MySQL, not just in this replica's memory.
        var keyCount = await CountDataProtectionKeysAsync();
        keyCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SessionCookieFromOneInstance_IsAcceptedByASecondInstance()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email.
        var email = $"crossinstance+{Guid.NewGuid():N}@leadingedje.com";

        // Act — sign in on instance ONE and capture its encrypted session cookie.
        var loginResponse = await SignInViaCallbackAsync(_instanceOne, email, SuperAdminGroup);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
        var sessionCookie = ExtractSessionCookie(loginResponse);
        sessionCookie.ShouldNotBeNull();

        // Replay the SAME cookie against instance TWO — a distinct host with its own in-memory Data
        // Protection cache. Without a shared, persisted key ring, instance two cannot decrypt the
        // ticket and returns 401 (the replica-switch logout bug this plan fixes).
        var clientTwo = _instanceTwo.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meRequest.Headers.Add("Cookie", sessionCookie);
        var me = await clientTwo.SendAsync(meRequest, TestContext.Current.CancellationToken);

        // Assert — instance two accepts the cookie and returns the same identity. The email match is
        // the proof of identity continuity; the auto-provisioned EdjeId is minted by instance one and
        // not known ahead of time, so there is nothing to compare it against beforehand.
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("email").GetString().ShouldBe(email);
        Guid.Parse(body.GetProperty("edjeId").GetString()!).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task FileSystemKeyDirectory_SharedRing_CrossFactoryDecryptSucceeds()
    {
        // Arrange — a pre-provisioned shared key directory (the ops keygen procedure: one key,
        // generated once). This doubles as proof that a hand-provisioned ring is what the API reads.
        var keyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        // Clear the DB ring so the RED (config-ignored -> PersistKeysToDbContext) path is forced to
        // write a fresh DB key, which the DB-count assertion below then catches.
        await ClearDbKeysAsync();
        PreGenerateFileSystemKey(keyDir);
        var preCount = CountKeyFiles(keyDir);

        DataProtectionSignInFactory factoryA = null!;
        DataProtectionSignInFactory factoryB = null!;
        try
        {
            factoryA = NewFileSystemFactory(keyDir);
            factoryB = NewFileSystemFactory(keyDir);
            _ = factoryA.Services;
            _ = factoryB.Services;

            var email = $"fskeys+{Guid.NewGuid():N}@leadingedje.com";

            // Act — sign in on A (encrypts with the shared file-system ring), replay to B.
            var loginResponse = await SignInViaCallbackAsync(factoryA, email, SuperAdminGroup);
            loginResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            var sessionCookie = ExtractSessionCookie(loginResponse);
            sessionCookie.ShouldNotBeNull();

            var clientB = factoryB.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
            var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            meRequest.Headers.Add("Cookie", sessionCookie);
            var me = await clientB.SendAsync(meRequest, TestContext.Current.CancellationToken);

            // Assert — B decrypts A's cookie via the shared file-system ring...
            me.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = JsonDocument.Parse(
                await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
            body.GetProperty("email").GetString().ShouldBe(email);

            // ...and NOTHING was persisted to the DB ring (proves the file-system branch is active,
            // not PersistKeysToDbContext — this is the RED discriminator).
            (await CountDataProtectionKeysAsync()).ShouldBe(0);
            CountKeyFiles(keyDir).ShouldBe(preCount);
        }
        finally
        {
            if (factoryA is not null)
            {
                await factoryA.DisposeAsync();
            }

            if (factoryB is not null)
            {
                await factoryB.DisposeAsync();
            }

            TryDeleteDirectory(keyDir);
        }
    }

    [Fact]
    public async Task FileSystemKeyDirectory_ServingRequests_GeneratesNoNewKeys()
    {
        // Arrange — pre-provision exactly one key; DisableAutomaticKeyGeneration must keep it at one.
        var keyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        await ClearDbKeysAsync();
        PreGenerateFileSystemKey(keyDir);
        var preCount = CountKeyFiles(keyDir);
        preCount.ShouldBeGreaterThan(0);

        DataProtectionSignInFactory factoryA = null!;
        DataProtectionSignInFactory factoryB = null!;
        try
        {
            factoryA = NewFileSystemFactory(keyDir);
            factoryB = NewFileSystemFactory(keyDir);

            var email = $"nokeygen+{Guid.NewGuid():N}@leadingedje.com";

            // Act — both factories serve requests (A signs in, B probes with the cookie).
            var loginResponse = await SignInViaCallbackAsync(factoryA, email, SuperAdminGroup);
            loginResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            var sessionCookie = ExtractSessionCookie(loginResponse);
            sessionCookie.ShouldNotBeNull();

            var clientB = factoryB.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
            var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            meRequest.Headers.Add("Cookie", sessionCookie);
            await clientB.SendAsync(meRequest, TestContext.Current.CancellationToken);

            // Assert — the key directory is untouched (DisableAutomaticKeyGeneration honored) and
            // nothing leaked into the DB ring.
            CountKeyFiles(keyDir).ShouldBe(preCount);
            (await CountDataProtectionKeysAsync()).ShouldBe(0);
        }
        finally
        {
            if (factoryA is not null)
            {
                await factoryA.DisposeAsync();
            }

            if (factoryB is not null)
            {
                await factoryB.DisposeAsync();
            }

            TryDeleteDirectory(keyDir);
        }
    }

    // --- Harness ---------------------------------------------------------------------------------

    private DataProtectionSignInFactory NewFileSystemFactory(string keyDir)
    {
        // Content root nested UNDER keyDir so recursive cleanup of keyDir removes it too. The
        // key-file glob (CountKeyFiles) is non-recursive top-level "key-*.xml", so this "host-*"
        // subdirectory never counts toward the ring.
        var contentRoot = Path.Combine(keyDir, $"host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        return new(_postgresContainer.GetConnectionString(), contentRoot, keyDir);
    }

    // Ops keygen procedure proof: create exactly one active key in the directory via a standalone
    // provider pinned to the SAME application name the app uses, then force key-ring materialization
    // with a single Protect() call. This is what a human/operator runs once to seed the shared ring.
    private static void PreGenerateFileSystemKey(string keyDir)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDir))
            .SetApplicationName("timesheet-v2");
        using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("keygen");
        _ = protector.Protect("seed");
    }

    private static int CountKeyFiles(string keyDir) =>
        Directory.Exists(keyDir) ? Directory.GetFiles(keyDir, "key-*.xml").Length : 0;

    private async Task ClearDbKeysAsync()
    {
        using var scope = _instanceOne.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM data_protection_keys";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone — nothing to clean up.
        }
    }


    private async Task<HttpResponseMessage> SignInViaCallbackAsync(
        DataProtectionSignInFactory factory, string email, string groups)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var externalResponse = await client.GetAsync(
            $"/test-only/sign-in-external?email={Uri.EscapeDataString(email)}&groups={Uri.EscapeDataString(groups)}",
            TestContext.Current.CancellationToken);
        externalResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var externalCookie = ExtractExternalSchemeCookie(externalResponse);

        var callbackRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login-callback");
        if (externalCookie is not null)
        {
            callbackRequest.Headers.Add("Cookie", externalCookie);
        }

        return await client.SendAsync(callbackRequest, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountDataProtectionKeysAsync()
    {
        using var scope = _instanceOne.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM data_protection_keys";
            var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return Convert.ToInt32(result);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(h => h.StartsWith(SessionCookiePrefix))?.Split(';').First();

    private static string? ExtractExternalSchemeCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(h => h.StartsWith(ExternalCookiePrefix))?.Split(';').First();

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [];
}
