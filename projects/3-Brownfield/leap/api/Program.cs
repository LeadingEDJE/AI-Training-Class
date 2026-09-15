// LeadingEDJE Timesheet v2 API Entry Point
using System.Text.Json.Serialization;
using Amazon.SimpleEmail;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;
using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;
using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;
using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using LeadingEDJE.Leap.Api.Platform.Endpoints;
using LeadingEDJE.Leap.Api.Platform.Endpoints.Admin;
using LeadingEDJE.Leap.Api.Platform.HealthChecks;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Jobs;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using LeadingEDJE.Leap.Api.Platform.Services.OpenApi;
using LeadingEDJE.Leap.Api.Platform.Services.Slack;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Quartz;
using Scalar.AspNetCore;
using SlackNet;
using Sustainsys.Saml2;
using Sustainsys.Saml2.AspNetCore2;
using Sustainsys.Saml2.Metadata;

var builder = WebApplication.CreateBuilder(args);

// Local developer override (Option A real-Google-SAML path, mirrors TPS). Added AFTER the default
// sources (appsettings.json → appsettings.{Environment}.json → env vars → args) so its values win,
// letting a developer drop the "Timesheet (local)" Saml2 EntityId/IdP/metadata here without editing
// tracked config. optional:true keeps CI, E2E, and every deployed env byte-identical — the file is
// gitignored and never present there. See appsettings.Local.json.example.
// Development-ONLY (mirrors TPS): as the last source it outranks even WebApplicationFactory
// UseSetting overrides, so loading it under the "Testing" environment would leak a developer's
// real local SAML config into test hosts and break hermetic tests (e.g. the /auth/login 503
// SAML-unconfigured contract).
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("Startup");
void LogStartupChoice(string service, string mode, string reason) =>
    startupLogger.LogInformation("Using {Mode} {Service} ({Reason})", mode, service, reason);

builder.Services.AddOpenApi();

// A second, dedicated OpenAPI document for the versioned /api/compass/v1 boundary (#141). It is the
// SAME endpoints already in the default "v1" document, filtered to just the versioned Compass
// contract, so the OpenAPI diff gate has a stable, minimal surface to diff — one that does not move
// when an unrelated timesheet route changes. The default document is untouched (the frontend still
// generates its types from /openapi/v1.json). Kept in lockstep with the route prefix in
// api/Modules/Compass/Endpoints/*; the CompassV1OpenApiDocumentTests assert the isolation.
//
// AddOperationTransformer<PrivateHandlerXmlDocOperationTransformer>() (spec 009 T081): the built-in
// XML-comment support does not reach a `private static` minimal-API handler — its compile-time doc
// cache omits every private member, confirmed by direct experiment (see the transformer's own
// remarks). This fills in
// `summary`/`description` from the SAME XML doc comments via a second, accessibility-blind source (the
// compiled XML doc FILE) — it only ever fills a null value, so it changes nothing for a handler the
// built-in path already covers. Registered on compass-v1 only (this feature's scope); the default
// document is untouched by this change, same as everything else about the compass-v1 document.
builder.Services.AddOpenApi("compass-v1", options =>
{
    options.ShouldInclude = apiDescription =>
        apiDescription.RelativePath?.StartsWith("api/compass/v1/", StringComparison.OrdinalIgnoreCase)
        ?? false;
    options.AddOperationTransformer<PrivateHandlerXmlDocOperationTransformer>();
});

builder.Services.AddValidation();
builder.Services.AddProblemDetails();

// A request body that cannot bind answers 400 in EVERY environment, including Development.
//
// `RouteHandlerOptions.ThrowOnBadRequest` defaults to TRUE under Development and FALSE everywhere
// else, so without this line the same malformed request produces a 400 in Production and in the
// test host but an unhandled BadHttpRequestException -> 500 locally. That inconsistency is not
// hypothetical: it is what `compass-clients.critical.spec.ts` hit when asserting FR-021's rule that
// a client request carrying a status member is REJECTED rather than ignored — the unit suite and
// production both said 400, and only the local E2E stack said 500.
//
// Aligning Development to what the other two already do makes the contract observable where it is
// actually exercised. Nothing is lost: the binding failure is still logged with its full exception
// detail, it simply stops escaping as a server error.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var host = builder.Configuration["POSTGRES_HOST"] ?? "127.0.0.1";
var port = builder.Configuration["POSTGRES_PORT"] ?? "5432";
var database = builder.Configuration["POSTGRES_DB"] ?? "leap_dev";
var user = builder.Configuration["POSTGRES_USER"] ?? "timesheet";
var password = builder.Configuration["POSTGRES_PASSWORD"] ?? "";
var connectionString = $"Host={host};Port={port};Database={database};Username={user};Password={password}";

// ---------------------------------------------------------------------------------------------
// One-shot database bootstrap mode (Phase 51).
//
// `dotnet LeadingEDJE.Leap.Api.dll --ensure-database` waits for the database server, creates the
// configured database if it is absent, and exits. It is what the Helm migration hook Job runs as an
// init container, ahead of the EF migration bundle -- the bundle cannot create the database it
// migrates, and it does not wait for one either (measured: it exits 1 in about two seconds against
// an unreachable server, with no retry at all).
//
// The branch sits HERE, immediately after the connection inputs are read and before the database
// context, authentication, health checks and the scheduler are registered, so this mode starts none
// of them. It returns before `builder.Build()`, so no web host is ever created.
//
// Blocking on the task rather than using a top-level `await` is deliberate: a top-level await turns
// the generated entry point into an async Main, and `WebApplicationFactory<Program>` -- which the
// unit and integration suites both host through -- resolves that entry point reflectively. This
// path runs before any host or synchronisation context exists, so blocking here cannot deadlock,
// and it keeps a one-shot console mode from changing the signature the test host depends on.
if (args.Contains("--ensure-database", StringComparer.Ordinal))
{
    // Configurable so an unusual server can be accommodated without a code change; `postgres` is the
    // maintenance database every PostgreSQL installation ships with.
    var maintenanceDatabase = builder.Configuration["StartupTasks:MaintenanceDatabase"] ?? "postgres";
    var maintenanceConnectionString =
        $"Host={host};Port={port};Database={maintenanceDatabase};Username={user};Password={password}";

    // Same parse-and-default shape as the StartupTasks:SeedReferenceData gate below: absent or
    // unparseable means ON. The orchestrator owns that decision so it is unit-testable, which is why
    // the raw string is handed over rather than a bool.
    var waitAttempts = int.TryParse(builder.Configuration["StartupTasks:EnsureDatabaseWaitAttempts"], out var parsedAttempts) && parsedAttempts > 0
        ? parsedAttempts
        : 60;
    var waitDelaySeconds = int.TryParse(builder.Configuration["StartupTasks:EnsureDatabaseWaitDelaySeconds"], out var parsedDelay) && parsedDelay > 0
        ? parsedDelay
        : 5;

    var bootstrapper = new DatabaseBootstrapper(
        new NpgsqlDatabaseBootstrapCommands(maintenanceConnectionString),
        new DatabaseBootstrapOptions(
            builder.Configuration["StartupTasks:EnsureDatabase"],
            database,
            host,
            port,
            waitAttempts,
            TimeSpan.FromSeconds(waitDelaySeconds)),
        startupLoggerFactory.CreateLogger<DatabaseBootstrapper>());

    var bootstrapResult = bootstrapper.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    // ONLY an unreachable server is a failure. A rejected name, a lost race and a missing
    // create-database right all exit zero on purpose -- the migration bundle that runs next fails
    // loudly if the database genuinely is not there, so the degradation cannot hide a real problem.
    var bootstrapFailed = bootstrapResult.Outcome == DatabaseBootstrapOutcome.FailedServerUnreachable;
    var bootstrapLevel = bootstrapResult.Outcome switch
    {
        DatabaseBootstrapOutcome.FailedServerUnreachable => LogLevel.Error,
        DatabaseBootstrapOutcome.RejectedInvalidName => LogLevel.Error,
        DatabaseBootstrapOutcome.DegradedNoCreatePrivilege => LogLevel.Warning,
        DatabaseBootstrapOutcome.SkippedGateDisabled => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    startupLogger.Log(
        bootstrapLevel,
        "Database bootstrap finished: {Outcome} after {Attempts} attempt(s). {Message}",
        bootstrapResult.Outcome,
        bootstrapResult.Attempts,
        bootstrapResult.Message);

    // Set the exit code and RETURN rather than calling Environment.Exit, so the console logger's
    // buffered output is flushed by the `using` on the logger factory before the process ends. A
    // hard exit here loses the one log line a deploy engineer needs.
    Environment.ExitCode = bootstrapFailed ? 1 : 0;
    return;
}

builder.Services.AddDbContext<LeapDbContext>(options =>
    options.UseLeapPostgres(connectionString));

// Phase 41: persist the Data Protection key ring so an auth cookie encrypted by one API replica can
// be decrypted by any other. Without this, each pod generates an ephemeral in-memory key ring, so a
// load-balancer reroute or a pod restart silently invalidates every live cookie session (users bounce
// to login). SetApplicationName pins the key-derivation purpose to a single value across all replicas
// AND environments, so the shared ring is actually shared (the default discriminator is the
// content-root path, which differs per container). See DataProtectionPersistenceTests.
//
// Phase 45 (D-02): shared-key-FAMILY mode for preview sign-in. When Auth:DataProtectionKeyDirectory is
// set (Helm 45-04 mounts a read-only /dataprotection-keys secret and sets Auth__DataProtectionKeyDirectory),
// the stable dev host and its *-pr-N preview siblings all read ONE pre-provisioned ring from the file
// system so a cookie issued by dev decrypts on a preview. DisableAutomaticKeyGeneration keeps that ring
// read-only — nobody rotates or pollutes it at runtime. NOTE (RESEARCH Pitfall 6): the default key
// lifetime is 90 days, so the shared key must be generated with a long explicit lifetime or rotated per
// the runbook in docs/ops/preview-auth.md (plan 45-06). Unset → the local/default PersistKeysToDbContext
// behavior is byte-identical to Phase 41.
// ===========================================================================================
// FROZEN — DO NOT RENAME "timesheet-v2". IT IS NOT A PRODUCT NAME.
// ===========================================================================================
// This string is the Data Protection KEY-DERIVATION PURPOSE. Treat it as a permanent opaque
// crypto constant that happens to be spelled like the old module name. Changing it:
//   1. invalidates EVERY live session immediately — every signed-in user is bounced to login,
//      because a cookie encrypted under the old purpose cannot be decrypted under the new one;
//   2. SEVERS the shared preview key-ring family (Phase 45 / D-02) — the stable dev host and its
//      *-pr-N preview siblings all derive from this single value, so a preview stops being able
//      to decrypt a cookie issued by dev and preview sign-in silently breaks;
//   3. is NOT caught by the build, the unit suite, the linters, or any coverage gate.
// It survived the Phase 48 LEAP rename deliberately, and it must survive every future tidy-up.
// The only mechanical guard is an integration test that asserts this exact literal:
// tests/integration/Endpoints/DataProtectionPersistenceTests.cs. If that test ever fails after
// an "obvious" consistency edit here, the test is right and the edit is wrong.
// ===========================================================================================
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("timesheet-v2");
var dataProtectionKeyDirectory = builder.Configuration["Auth:DataProtectionKeyDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyDirectory))
{
    dataProtection
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyDirectory))
        .DisableAutomaticKeyGeneration();
}
else
{
    dataProtection.PersistKeysToDbContext<LeapDbContext>();
}

builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IEmployeeDirectory, PersonEmployeeDirectory>();
builder.Services.AddScoped<IAuditService, AuditService>();
// Scoped, NOT Singleton (#528). It holds IEnumerable<IAuditTrailAccessRule>, and the rules are
// scoped because they read through scoped repositories -- a singleton here captures every one of
// them, and no gate in this repository catches that. See AuditTrailAccessPolicy's own remarks.
builder.Services.AddScoped<IAuditTrailAccessPolicy, AuditTrailAccessPolicy>();
builder.Services.AddScoped<IAuditRetentionService, AuditRetentionService>();
builder.Services.AddScoped<IPersonRepository, PersonRepository>();
builder.Services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
// The Timesheet-era channel-preference dispatcher (EmployeeAttribute-backed) is gone with the
// module; nothing left in the running application calls NotifyAsync, so the no-op double is the
// registration rather than an unused real implementation.
builder.Services.AddScoped<INotificationService, NullNotificationService>();
builder.Services.AddScoped<IUserRoleRepository, UserRoleRepository>();
builder.Services.AddScoped<IUserRoleService, UserRoleService>();
builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
builder.Services.AddScoped<ISystemSettingService, SystemSettingService>();
builder.Services.AddScoped<IAuthorizationResolver, CompositeAuthorizationResolver>();
builder.Services.AddMemoryCache();

var slackBotToken = builder.Configuration["Slack:BotToken"];
if (!string.IsNullOrEmpty(slackBotToken))
{
    builder.Services.AddSingleton<ISlackApiClient>(
        new SlackServiceBuilder()
            .UseApiToken(slackBotToken)
            .GetApiClient());
    builder.Services.AddSingleton<ISlackClient, SlackClient>();
    LogStartupChoice("Slack client", "real", "bot token configured");
}
else
{
    builder.Services.AddSingleton<ISlackClient, MockSlackClient>();
    LogStartupChoice("Slack client", "mock", "no bot token configured");
}
builder.Services.Configure<EmailEnvironmentOptions>(
    builder.Configuration.GetSection(EmailEnvironmentOptions.SectionName));

// The concrete sender is registered under its own type, and IEmailSender resolves to it or to the
// environment stamp below. Registering the concrete type is what lets the decorator reach it -- and
// what lets a test host reach the outbox once the stamp is in front of it. Email:SesRegion is the one
// key that selects the SES API transport under the pod's IRSA role; blank -- the chart default, every
// local run, and every PR preview, which cannot assume that role -- means the in-memory outbox.
var sesSettings = SesApiEmailSender.ReadSettings(builder.Configuration);
if (sesSettings is not null)
{
    // Built with the configured region, never the SDK's default chain: Email:SesRegion pins where the
    // send goes and the container's AWS_REGION is a different region. No explicit credentials -- the
    // default chain reads the web-identity token EKS projects into the pod. Reasoning on
    // SesApiEmailSender.CreateClient; region detail in docs/ops/email-environment-labelling.md.
    builder.Services.AddSingleton<IAmazonSimpleEmailService>(_ =>
        SesApiEmailSender.CreateClient(sesSettings));
    builder.Services.AddSingleton<SesApiEmailSender>();
    builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<SesApiEmailSender>());
    LogStartupChoice("email sender", "SES API", $"region {sesSettings.Region}");
}
else
{
    builder.Services.AddSingleton<MockEmailSender>();
    builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<MockEmailSender>());
    LogStartupChoice("email sender", "mock", "no Email:SesRegion configured");
}

// Issue #327: label outgoing mail with the environment that sent it, and let a non-production
// environment stop sending. Issue #592 adds the recipient redirect and the fail-closed suppression
// behind it. Installed ONLY when one of those is actually in play, so an environment asking for none
// of them keeps the exact object graph it has today -- production included, and every test host,
// whose Compass outbox assertion resolves IEmailSender and expects to find a MockEmailSender rather
// than something wrapping one.
var emailEnvironment =
    builder.Configuration.GetSection(EmailEnvironmentOptions.SectionName).Get<EmailEnvironmentOptions>()
    ?? new EmailEnvironmentOptions();

// Derived from the same settings read that selected the transport above, rather than a second lookup
// of the key -- the two must not be able to disagree about whether a real transport exists.
var emailTransportIsReal = sesSettings is not null;

// The fourth reason to install, and the one that must not be keyed on the label: a non-production
// environment that configures the SES transport and nothing else has to reach the decorator to be
// suppressed, and the label cannot answer that -- values.yaml pins ASPNETCORE_ENVIRONMENT: Staging
// for deployed dev and every preview, so an absent label must not read as "production". That chart
// pin, not IsProduction(), is what guards a deployment; see EmailEnvironmentOptions.RedirectAllTo.
var emailNeedsNonProductionGuard = !builder.Environment.IsProduction() && emailTransportIsReal;

// The fifth reason to install: a non-production From sender. Without this condition the option could
// be configured and do nothing -- an environment that set only this key would have its mail leave as
// the production sender and be refused by SES, with nothing in the log to say why. Not keyed on
// IsProduction(): production returns from inside SendAsync before the sender is read, so installing
// there changes nothing.
if (!emailEnvironment.Enabled
    || !string.IsNullOrWhiteSpace(emailEnvironment.EnvironmentLabel)
    || !string.IsNullOrWhiteSpace(emailEnvironment.RedirectAllTo)
    || !string.IsNullOrWhiteSpace(emailEnvironment.NonProductionFromAddress)
    || emailNeedsNonProductionGuard)
{
    builder.Services.AddSingleton<IEmailSender>(sp => new EnvironmentStampingEmailSender(
        // Resolving the concrete registration, NOT IEmailSender: asking the container for the
        // interface here would hand back the decorator being built and recurse until the stack ends.
        emailTransportIsReal
            ? sp.GetRequiredService<SesApiEmailSender>()
            : sp.GetRequiredService<MockEmailSender>(),
        sp.GetRequiredService<IOptions<EmailEnvironmentOptions>>(),
        sp.GetRequiredService<IHostEnvironment>(),
        sp.GetRequiredService<ILogger<EnvironmentStampingEmailSender>>()));

    LogStartupChoice(
        "email environment stamp",
        emailEnvironment.Enabled ? emailEnvironment.EnvironmentLabel ?? string.Empty : "delivery DISABLED",
        EmailEnvironmentReason(emailEnvironment, emailNeedsNonProductionGuard));

    static string EmailEnvironmentReason(EmailEnvironmentOptions options, bool needsGuard)
    {
        if (!options.Enabled)
        {
            return "Email:Enabled is false -- no mail will be delivered";
        }

        if (!string.IsNullOrWhiteSpace(options.RedirectAllTo))
        {
            return "Email:RedirectAllTo configured -- non-production mail is redirected there";
        }

        if (needsGuard)
        {
            return "non-production with a real email transport and no Email:RedirectAllTo -- mail "
                + "will be SUPPRESSED";
        }

        if (string.IsNullOrWhiteSpace(options.EnvironmentLabel))
        {
            // Reached only when NonProductionFromAddress is the sole reason to install, since one of
            // the five conditions must hold to get here at all. Without this branch the line would
            // claim a label that is not configured.
            return "Email:NonProductionFromAddress configured -- non-production mail is sent as "
                + "that sender";
        }

        return "Email:EnvironmentLabel configured";
    }
}
// Prevent BackgroundService exceptions from killing the host
builder.Services.Configure<HostOptions>(opts =>
    opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// IHttpClientFactory must be available for HTTP consumers (e.g. SlackApprovalProcessorService).
// Historically it was registered only as a side effect of the now-deleted HTTP-TPS branch's
// AddHttpClient<ITpsLiveClientService,...>, so register the factory infrastructure explicitly here.
builder.Services.AddHttpClient();
builder.Services.AddScoped<ReferenceDataSeeder>();
// Quick task 260729-sqc (D-01): bootstrap SuperAdmin seeder — runs in ALL environments, gated only by
// the Bootstrap:SuperAdmins config list (see the ApplicationStarted seeding call site below).
builder.Services.AddScoped<BootstrapSuperAdminSeeder>();
// The readiness probe uses a raw NpgsqlConnection (SELECT 1) kept isolated from the EF
// stack — a broken request path must not be able to mask or trigger readiness failures.
// Do not "consolidate" by passing LeapDbContext into NpgsqlHealthCheck.
builder.Services.AddHealthChecks()
    .AddCheck<LiveHealthCheck>("live", tags: ["live"])
    .AddCheck<NpgsqlHealthCheck>("postgres", tags: ["db", "ready"]);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Auth infrastructure
builder.Services.AddHttpContextAccessor();
// A request-scoped resolution picks CurrentUserContext; a scope with no HTTP request — a Quartz job —
// gets BackgroundUserContext, so a background IDirectory read resolves its viewer tier at Baseline
// instead of throwing "No HTTP context". Identity still throws there; only privilege resolution is
// made background-safe.
builder.Services.AddScoped<ICurrentUserContext>(sp =>
    sp.GetRequiredService<IHttpContextAccessor>().HttpContext is null
        ? new BackgroundUserContext()
        : ActivatorUtilities.CreateInstance<CurrentUserContext>(sp));

// CompassBusinessDate's clock.
builder.Services.AddSingleton(TimeProvider.System);

// Compass (Phase 49) — the module registers itself in ONE call rather than scattering its
// registrations through the flat block above. That is the convention this module introduces.
builder.Services.AddCompassServices();

builder.Services.Configure<DevBypassOptions>(opts =>
{
    builder.Configuration.GetSection("Auth:DevBypass").Bind(opts);
    opts.ClientId = builder.Configuration["Auth:ClientId"] ?? string.Empty;
});

// Phase 41: Google SAML config binding + pure mapping core + sign-in pipeline.
// Bind the Saml2/GoogleAuth sections and register the mapping + sign-in services. The SAML handler
// (AddSaml2, below) and the /auth endpoints consume these. Both sections ship empty in
// appsettings.json (SAML-off) and are populated per environment.
builder.Services.Configure<LeadingEDJE.Leap.Api.Platform.Auth.Saml2Options>(builder.Configuration.GetSection("Saml2"));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("GoogleAuth"));

// ADR-007 audit retention. Nothing sets this today, so every environment runs the ADR's two-year
// window; the section exists so an operator can change it without a deploy (issue #86). There is
// deliberately no setting for business records -- "never purged" is the absence of a policy, and a
// configurable one would invite someone to put a number in it.
builder.Services.Configure<AuditRetentionOptions>(
    builder.Configuration.GetSection(AuditRetentionOptions.SectionName));
// Reject an unusable window BEFORE serving traffic, for the same reason the role-mapping validator
// below does: a typo would otherwise leave the app running with the purge quietly disabled -- the job
// refuses, logs one line, and audit rows accumulate forever. A retention policy that has silently
// stopped working is worse than a deploy that failed, because nobody finds out.
AuditRetentionOptionsValidator.Validate(
    builder.Configuration.GetSection(AuditRetentionOptions.SectionName).Get<AuditRetentionOptions>()
        ?? new AuditRetentionOptions());
// Phase 49: reject an unknown or cross-module role string BEFORE the app serves traffic. A typo in a
// group mapping otherwise grants nothing silently, and a Compass group mapped to a bare timesheet role
// would grant that role *in timesheet* -- the one direction that fails OPEN. Fail fast, never
// log-and-continue.
GroupRoleMappingValidator.Validate(
    builder.Configuration.GetSection("GoogleAuth").Get<GoogleAuthOptions>() ?? new GoogleAuthOptions());
// Quick task 260729-sqc (D-04): per-environment bootstrap SuperAdmin list. Bound from its own
// "Bootstrap" section (NOT "Auth", which AuthReturnUrlOptions already binds), so the Helm runtime form
// is Bootstrap__SuperAdmins__0, __1, … Unset/empty = complete no-op, which is why nothing is added to
// appsettings.json or appsettings.Development.json.
builder.Services.Configure<BootstrapAdminOptions>(
    builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
// Phase 45 (D-02): sibling-origin open-redirect allow-list for SignInService.SafeReturnUrl. Bound from
// the "Auth" section so Helm runtime Auth__AllowedReturnOrigins / Auth__AllowedReturnHostSuffix values
// flow in. Absent = feature off (local paths only) — no appsettings.json entries by design.
builder.Services.Configure<LeadingEDJE.Leap.Api.Platform.Auth.AuthReturnUrlOptions>(
    builder.Configuration.GetSection("Auth"));
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
// Quick task 260729-sqc: the shared find-or-create core for app-minted people rows. Used by the
// bootstrap SuperAdmin seeder at startup AND by SignInService's first-sign-in auto-create branch, so
// both paths share one implementation of "never resurrect a deactivated person, never duplicate".
builder.Services.AddScoped<IPersonProvisioningService, PersonProvisioningService>();
builder.Services.AddScoped<ISignInService, SignInService>();
builder.Services.AddScoped<IImpersonationService, ImpersonationService>();

// Phase 41: cookie session (primary) + external SAML holding cookie + config-gated Sustainsys
// challenge. JWT Bearer + the AutoSelect policy scheme are DELETED — users authenticate by cookie
// only; the raw SAML assertion never becomes a session (it signs into the short-lived external
// scheme, and SignInService.CompleteSignIn mints the real session only after all checks pass).
// Production/deployed (never SameAsRequest): a lost/spoofed X-Forwarded-Proto must not let the
// cookie be reissued without Secure. Development ONLY relaxes to SameAsRequest so the plain-http
// local/E2E stack works in EVERY browser: Chromium and Firefox treat http://localhost as a secure
// context and send Secure cookies anyway, but WebKit/Safari does NOT — a hard `Always` here silently
// drops the session cookie on every WebKit request over plain http, 401-ing the whole app (this was
// the webkit-critical E2E meltdown). Dev never sits behind a hostile proxy, so SameAsRequest is safe
// there; deployed envs run ASPNETCORE_ENVIRONMENT=Production and keep the strict Always policy.
var sessionSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
var authBuilder = builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = AuthConstants.Settings.LeadingEdjeAuthenticationType;
})
.AddCookie(AuthConstants.Settings.LeadingEdjeAuthenticationType, options =>
{
    options.Cookie.Name = AuthConstants.Settings.SessionCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = sessionSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Phase 45 (D-02): config-gated parent-domain scope (port of TPS AuthDomain semantics). A
    // dot-prefixed value (e.g. ".dev.leadingedje.com") widens the session cookie so the *-pr-N
    // preview siblings receive the cookie the stable dev host issued. Dot-prefix-gated: an unset or
    // non-dot value leaves the cookie host-only, so localhost and dev-machine flows are untouched.
    var cookieDomain = builder.Configuration["Auth:CookieDomain"];
    if (!string.IsNullOrWhiteSpace(cookieDomain) && cookieDomain.StartsWith('.'))
    {
        options.Cookie.Domain = cookieDomain;
    }
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.LoginPath = new PathString("/auth/login");
    options.AccessDeniedPath = new PathString("/auth/access-denied");
    // API callers get status codes, not redirects: the SPA probes /api/* and handles 401/403 itself.
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
})
.AddCookie(AuthConstants.Settings.ExternalScheme, options =>
{
    // Short-lived holding pen for the raw, unvalidated SAML assertion — read once by the
    // login-callback then signed out. Never used for authorization.
    options.Cookie.Name = AuthConstants.Settings.ExternalCookieName;
    options.Cookie.HttpOnly = true;
    // Same Dev-relaxation as the session cookie above (see rationale): strict Always in deployed
    // Production, SameAsRequest in Development so the plain-http local SAML flow works in WebKit too.
    options.Cookie.SecurePolicy = sessionSecurePolicy;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
});

ConfigureSaml2(authBuilder, builder.Configuration);

// ─── Migration principal (feature 010) ───────────────────────────────────────────────────────
// The bulk TPS -> Compass migration writes through the API, and NOTHING else here can authenticate
// a script in a deployed environment: SAML needs an interactive browser, and DevBypass and
// stub-login are both hard-off outside Development.
//
// CONFIG-GATED, AND THAT IS THE SECURITY PROPERTY. With no token configured the scheme is not
// registered at all -- not registered-but-rejecting -- so an environment that never runs a
// migration gains no new authentication surface. Registering unconditionally and relying on
// MigrationPrincipalOptions.Matches() returning false would be strictly worse: it would put a live
// bearer-token code path in front of every request in production for no reason.
//
// The token is supplied BY REFERENCE; this repository never holds the value. The stable dev
// environment reads it from the TPS_MIGRATION_TOKEN *repository* secret, injected into the chart
// Secret at deploy time (deploy-aws.yml -> HELM_SET_SECRET_VALUES -> secret-ref.yaml) and reaching
// the container through envFrom. That is the same path Slack__BotToken and the SMTP credentials
// already take, and it was chosen over an ExternalSecret deliberately: an unset or misspelled
// GitHub secret renders an EMPTY value, which lands back on the no-token branch below and changes
// nothing, whereas a misnamed Secrets Manager entry blocks the rollout outright.
//
// PR previews are deliberately NOT given a token -- values.yaml's empty default stands, so they gain
// no bearer-token surface.
//
// PRODUCTION IS NOT WIRED YET (values-production.yaml is a draft that no workflow references). When
// it is, the token belongs in Secrets Manager under `/chat-edje/<env>/` alongside the data-protection
// key ring -- that prefix is mandatory, because the external-secrets operator role holds
// secretsmanager:GetSecretValue ONLY beneath it. A bare secret name is AccessDenied, the
// ExternalSecret never materialises, and the rollout blocks entirely (0/1 available -> timeout)
// rather than degrading. Do NOT widen the IAM policy to fix a naming problem.
builder.Services.Configure<MigrationPrincipalOptions>(
    builder.Configuration.GetSection("Auth:MigrationPrincipal")
);

var migrationPrincipalToken = builder.Configuration["Auth:MigrationPrincipal:Token"];

if (!string.IsNullOrWhiteSpace(migrationPrincipalToken))
{
    authBuilder.AddScheme<AuthenticationSchemeOptions, MigrationPrincipalAuthenticationHandler>(
        MigrationPrincipal.Scheme,
        displayName: null,
        configureOptions: _ => { }
    );

    // ⚠️ A FORWARDING POLICY SCHEME, NOT A SCHEME LIST ON THE POLICIES. This distinction cost a
    // real regression before it was understood, so it is worth stating.
    //
    // The obvious approach — naming [cookie, MigrationPrincipal] on each Compass policy — makes
    // authorization RE-AUTHENTICATE through those schemes, discarding whatever HttpContext.User
    // already held. DevBypass sets the user DIRECTLY (it is middleware, not a scheme), so naming
    // schemes silently broke it: with a migration token configured, every Compass route answered
    // 401 for a local developer. Observed against a running stack, not theorised.
    //
    // Forwarding instead keeps the policies scheme-less, so they read HttpContext.User and DevBypass
    // keeps working — while a request carrying a bearer token is routed to the migration handler.
    // Requests without one forward to the cookie scheme, so browser behaviour is byte-identical to
    // what it was before this block existed.
    //
    // The whole block is config-gated, so an environment with no migration token has no policy
    // scheme, no forwarding, and no change of any kind.
    // ⚠️ THE NON-BEARER FALLBACK IS THE SCHEME THIS HOST HAD CONFIGURED, not a hard-coded constant.
    // It read AuthConstants.Settings.LeadingEdjeAuthenticationType, which is correct in every
    // deployed environment because the cookie scheme IS the default there -- and wrong for any host
    // that configures its own. Overriding DefaultAuthenticateScheme below then silently redirected
    // every ordinary request to a scheme the host had not chosen, and they answered 401.
    //
    // Found by an integration test that configures a token and then makes a perfectly ordinary
    // Compass write (CompassProvenanceThroughTheApiTests): its host registers TestAuthHandler as the
    // default, exactly as a future host might, and both non-bearer cases failed. Same shape as the
    // regression the forwarding scheme was introduced to fix -- a symptom visible ONLY when a
    // migration token is configured, which is a state almost nothing exercises.
    var nonBearerScheme = AuthConstants.Settings.LeadingEdjeAuthenticationType;

    authBuilder.AddPolicyScheme(
        MigrationPrincipal.ForwardingScheme,
        displayName: null,
        options =>
            options.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString()
                    .StartsWith("Bearer ", StringComparison.Ordinal)
                    ? MigrationPrincipal.Scheme
                    // Read per-request, so it sees the value PostConfigure resolved at startup.
                    : nonBearerScheme
    );

    builder.Services.PostConfigure<AuthenticationOptions>(options =>
    {
        // Captured BEFORE the override, or the forwarding scheme would forward to itself.
        nonBearerScheme =
            options.DefaultAuthenticateScheme
            ?? options.DefaultScheme
            ?? AuthConstants.Settings.LeadingEdjeAuthenticationType;

        options.DefaultAuthenticateScheme = MigrationPrincipal.ForwardingScheme;
    });
}

// All-or-nothing SAML registration (ported from TPS ConfigureSaml2/ResolveMetadataLocation): the
// Sustainsys handler is only wired once the Workspace admin has supplied SP EntityId + IdP EntityId
// + metadata, so the app still boots (SAML-off) with the empty placeholder config shipped in
// appsettings.json. The raw assertion signs into the external holding scheme, never the session.
static void ConfigureSaml2(AuthenticationBuilder authBuilder, IConfiguration configuration)
{
    var spEntityId = configuration["Saml2:EntityId"];
    var idpEntityId = configuration["Saml2:IdentityProvider:EntityId"];
    var idpMetadataLocation = ResolveMetadataLocation(configuration);

    if (string.IsNullOrWhiteSpace(spEntityId)
        || string.IsNullOrWhiteSpace(idpEntityId)
        || string.IsNullOrWhiteSpace(idpMetadataLocation))
    {
        return;
    }

    authBuilder.AddSaml2(options =>
    {
        // Without this the handler would sign the raw assertion into the default (session) scheme
        // before CompleteSignIn's domain/person/role checks ever run.
        options.SignInScheme = AuthConstants.Settings.ExternalScheme;
        options.SPOptions.EntityId = new EntityId(spEntityId);

        var idp = new IdentityProvider(new EntityId(idpEntityId), options.SPOptions)
        {
            MetadataLocation = idpMetadataLocation,
            LoadMetadata = true
        };
        options.IdentityProviders.Add(idp);

        // Normal sign-in stays silent; the switch-account path stashes this flag so Google re-prompts
        // and offers the account chooser instead of reusing the current session (#671).
        options.Notifications.AuthenticationRequestCreated = (request, _, relayData) =>
        {
            if (relayData.TryGetValue(AuthConstants.Saml.SwitchAccountRelayKey, out var v) && v == "true")
            {
                request.ForceAuthentication = true;
            }
        };
    });
}

// MetadataLocation (URL or file path) wins for local dev; deployed envs deliver the metadata as a
// raw MetadataXml secret which Sustainsys cannot consume directly, so it is written to a temp file
// once at startup and that path is returned instead.
static string? ResolveMetadataLocation(IConfiguration configuration)
{
    var explicitLocation = configuration["Saml2:IdentityProvider:MetadataLocation"];
    if (!string.IsNullOrWhiteSpace(explicitLocation))
    {
        return explicitLocation;
    }

    var metadataXml = configuration["Saml2:IdentityProvider:MetadataXml"];
    if (string.IsNullOrWhiteSpace(metadataXml))
    {
        return null;
    }

    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timesheet-saml2-idp-metadata.xml");
    System.IO.File.WriteAllText(path, metadataXml);
    return path;
}

// Helper: resolve roles via DI-injectable IAuthorizationResolver (JWT claims + local user_roles DB)
static Func<AuthorizationHandlerContext, Task<bool>> RequireLocalRole(params string[] roles) =>
    async ctx =>
    {
        var httpContext = (ctx.Resource as HttpContext)
            ?? throw new InvalidOperationException("No HttpContext");
        var resolver = httpContext.RequestServices.GetRequiredService<IAuthorizationResolver>();
        return await resolver.HasAnyRoleAsync(ctx.User, roles);
    };

builder.Services.AddAuthorization(options =>
{
    // Single-role policies (resolved from local user_roles table)
    options.AddPolicy(RolePolicy.EDJEr, p => p.RequireAssertion(RequireLocalRole("EDJEr")));
    options.AddPolicy(RolePolicy.Manager, p => p.RequireAssertion(RequireLocalRole("Manager")));
    options.AddPolicy(RolePolicy.TimesheetProcessor, p => p.RequireAssertion(RequireLocalRole("TimesheetProcessor")));
    options.AddPolicy(RolePolicy.Accounting, p => p.RequireAssertion(RequireLocalRole("Accounting")));
    options.AddPolicy(RolePolicy.HR, p => p.RequireAssertion(RequireLocalRole("HR")));
    options.AddPolicy(RolePolicy.Ops, p => p.RequireAssertion(RequireLocalRole("Ops")));
    options.AddPolicy(RolePolicy.PayrollProcessor, p => p.RequireAssertion(RequireLocalRole("PayrollProcessor")));
    options.AddPolicy(RolePolicy.Admin, p => p.RequireAssertion(RequireLocalRole("Admin")));
    options.AddPolicy(RolePolicy.SuperAdmin, p => p.RequireAssertion(RequireLocalRole("SuperAdmin")));

    // Compound policies
    options.AddPolicy(RolePolicy.ManagerOrProcessor, p =>
        p.RequireAssertion(RequireLocalRole("Manager", "TimesheetProcessor", "SuperAdmin")));
    options.AddPolicy(RolePolicy.HROrSuperAdmin, p =>
        p.RequireAssertion(RequireLocalRole("HR", "SuperAdmin")));
    options.AddPolicy(RolePolicy.ProcessorOrAdmin, p =>
        p.RequireAssertion(RequireLocalRole("TimesheetProcessor", "Admin", "SuperAdmin")));
    options.AddPolicy(RolePolicy.OpsOrSuperAdmin, p =>
        p.RequireAssertion(RequireLocalRole("Ops", "SuperAdmin")));
    options.AddPolicy(RolePolicy.AccountingOrSuperAdmin, p =>
        p.RequireAssertion(RequireLocalRole("Accounting", "SuperAdmin")));
    options.AddPolicy(RolePolicy.PayrollProcessorOrSuperAdmin, p =>
        p.RequireAssertion(RequireLocalRole("PayrollProcessor", "SuperAdmin")));
    options.AddPolicy(RolePolicy.InvoiceAccess, p =>
        p.RequireAssertion(RequireLocalRole("Accounting", "TimesheetProcessor", "SuperAdmin")));
    options.AddPolicy(RolePolicy.BalanceAccess, p =>
        p.RequireAssertion(RequireLocalRole("Ops", "HR", "SuperAdmin")));
    options.AddPolicy(RolePolicy.ReportDownload, p =>
        p.RequireAssertion(RequireLocalRole("Accounting", "TimesheetProcessor", "HR", "Ops", "PayrollProcessor", "SuperAdmin")));
});

// Compass (Phase 49) — the module registers its own policies in ONE call. This is the convention
// Compass introduces; the flat blocks above are left as they are, deliberately, because this phase
// establishes the pattern rather than refactoring what exists. Compass policies accept ONLY Compass
// role strings: no timesheet role grants anything in Compass, and vice versa.
builder.Services.AddCompassAuthorization();

// Quartz.NET scheduled jobs
builder.Services.AddQuartz(q =>
{
    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // ADR-007 audit retention (#145). Daily at 03:00 Eastern -- outside business hours.
    // Daily rather than weekly because a daily pass deletes a day's worth at a time; a weekly one
    // would let seven days accumulate and make each pass seven times the work for no benefit.
    var auditRetentionKey = new JobKey("AuditRetentionJob");
    q.AddJob<AuditRetentionJob>(opts => opts.WithIdentity(auditRetentionKey));
    q.AddTrigger(opts => opts.ForJob(auditRetentionKey).WithIdentity("AuditRetentionDaily3am")
        .WithCronSchedule("0 0 3 ? * *", x => x.InTimeZone(easternZone)));

    var retryJobKey = new JobKey("NotificationRetryJob");
    q.AddJob<NotificationRetryJob>(opts => opts.WithIdentity(retryJobKey));
    q.AddTrigger(opts => opts.ForJob(retryJobKey).WithIdentity("RetryEvery5min")
        .WithSimpleSchedule(x => x.WithIntervalInMinutes(5).RepeatForever()));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Forwarded headers (ported from TPS-737): honor only X-Forwarded-For and X-Forwarded-Proto — NOT
// XForwardedHost, which is a spoof vector when honored from any caller. KnownNetworks/KnownProxies
// are cleared: this is SAFE ONLY because the deployed ingress is scoped to the load balancer (no
// untrusted caller can set these headers). Without it Request.Scheme=http and Sustainsys emits an
// http ACS URL in the AuthnRequest, so Google rejects the https callback ("Unsolicited responses").
// Verified per-environment in Plan 41-06.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var seedReferenceDataOnStartup = !bool.TryParse(
    app.Configuration["StartupTasks:SeedReferenceData"],
    out var seedReferenceData)
    || seedReferenceData;

// THE INVERTED DEFAULT IS THE POINT: absent or unparseable means OFF, the opposite of the gate above.
// Reference data is required for the application to function, so defaulting it ON is a safety net.
// The Compass directory is 97 invented EDJErs and 28 invented clients; writing those into a database is
// a data decision, and a missing value must never be read as consent to make it. Production sets
// nothing and therefore gets nothing.
//
// Why a config flag rather than the IsDevelopment() branch below: deployed dev runs Staging, so an
// environment-gated seeder leaves the seven compass.* tables empty with no way in short of flipping
// ASPNETCORE_ENVIRONMENT — which would also switch on /auth/stub-login and DevBypass on an
// internet-reachable host. Same shape and same reasoning as the Bootstrap:SuperAdmins gate below,
// which exists because a Development-only seeder once meant nobody could sign in to a fresh deploy.
var seedCompassDirectoryOnStartup =
    bool.TryParse(app.Configuration["StartupTasks:SeedCompassDirectory"], out var seedCompassDirectory)
    && seedCompassDirectory;

// Forwarded headers MUST run first so downstream middleware (auth, Sustainsys) sees the real scheme.
app.UseForwardedHeaders();

// Deployed-envs-only scheme override (Auth:ForceHttpsScheme, set via Helm runtime values). The EKS
// gateway overwrites X-Forwarded-Proto on the /auth and /Saml2 routes it sends directly to the API,
// so UseForwardedHeaders above cannot recover https there — see ForcedHttpsSchemeMiddleware. Runs
// AFTER UseForwardedHeaders so it wins regardless of what the proxy chain delivered.
if (bool.TryParse(app.Configuration["Auth:ForceHttpsScheme"], out var forceHttps) && forceHttps)
{
    app.UseMiddleware<ForcedHttpsSchemeMiddleware>();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("Default");

app.UseAuthentication();

// Dev bypass runs AFTER UseAuthentication so a real cookie session always wins (the
// already-authenticated skip is now effective) and never overrides it. It is also
// opt-out-able via Auth:DevBypass:Enabled (default true): the E2E stack sets it to
// false so the suite runs on real cookie sessions and unauthenticated requests are
// observable (logout invalidation, 401/redirect) instead of masked by the dev profile.
var devBypassEnabled = app.Configuration.GetValue("Auth:DevBypass:Enabled", true);
if (app.Environment.IsDevelopment() && devBypassEnabled)
{
    app.UseMiddleware<DevBypassMiddleware>();
}

app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("LeadingEDJE Timesheet API")
           .WithTheme(ScalarTheme.Saturn);
});

// Reference-data seeding happens once the app is already listening. Doing it before app.Run()
// would block the health probes, and Kubernetes would kill the pod for failing them. Schema arrives
// before the application does — see docs/ops/pre-deploy-migrations.md.
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        // 100 x 5s = 500 seconds, deliberately LONGER than the deploy hook's own 480-second
        // activeDeadlineSeconds (deploy/helm/leap/values.yaml, migrations.activeDeadlineSeconds).
        //
        // In database.mode=pod a first install creates the database as one of the release's own
        // resources, so the hook that builds the schema can still be working while this application
        // is already serving — the first attempts below legitimately find no tables. Sizing the
        // budget above the hook's deadline means that whenever the hook can succeed at all, this
        // loop is still trying; a shorter budget would leave a fresh environment with no reference
        // data and no error anyone notices.
        const int maxRetries = 100;
        const int delaySeconds = 5;

        if (!seedReferenceDataOnStartup)
        {
            logger.LogInformation("Startup reference data seeding skipped because StartupTasks:SeedReferenceData is false");
            return;
        }

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                // Issue #503 split the former single ReferenceDataSeeder along its ownership seam; the
                // Timesheet half (invoicing frequencies, time categories, training lookups, timesheet
                // periods) retired with the module. This seeds only the Platform residue
                // (system_settings).
                var refSeeder = scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>();
                await refSeeder.SeedAsync();
                logger.LogInformation("Reference data seeded successfully on attempt {Attempt}", attempt);

                // Quick task 260729-sqc (D-01/D-02): bootstrap SuperAdmin access for deployed
                // environments. Deliberately OUTSIDE / BEFORE the IsDevelopment() branch below:
                // deployed envs run ASPNETCORE_ENVIRONMENT=Staging|Production, and an environment gate
                // here would reproduce exactly the bug being fixed (the only seeder that filled
                // `people` was Development-only, so no one could ever sign in to a fresh deployment).
                // It is a seeder rather than an EF migration because Up(MigrationBuilder) has no
                // IConfiguration access and so cannot express a per-environment list, migrations are
                // immutable history (changing an admin later would tangle people-data into schema
                // history), and migrations are scoped to schema. Gating is
                // purely config-driven (Bootstrap:SuperAdmins) — unset is a no-op — and it inherits
                // this retry loop plus the StartupTasks:SeedReferenceData gate for free.
                var bootstrapSeeder = scope.ServiceProvider.GetRequiredService<BootstrapSuperAdminSeeder>();
                await bootstrapSeeder.SeedAsync();

                // Compass owns its own dev fixture, invoked from the composition root rather than from
                // DevelopmentSeeder: CompassBoundaryTests rule one makes this file the ONLY production
                // file outside api/Modules/Compass/ permitted to name that namespace, so calling it
                // from the Platform seeder would fail the build.
                //
                // OUTSIDE the IsDevelopment() branch above, and opt-in via
                // StartupTasks:SeedCompassDirectory (default OFF — see the gate's own comment). Local
                // development still seeds unconditionally; a deployed environment seeds only when it
                // asks to. This is the "gated" path the previous comment here pointed at, chosen over an
                // admin endpoint or a one-shot Job because it needs no cluster access: the deployed dev
                // environment denies pods/exec to the CI service account, so anything exec-shaped is a
                // prompt-only procedure that cannot run from a workflow.
                //
                // Safe on every boot. The seeder guards each lookup table and guards the directory
                // content as one unit on the presence of any employee, returning before SaveChanges when
                // there is nothing to do — so this is effectively a one-time seed that no-ops on every
                // subsequent deploy. It also resyncs the identity sequences after its explicit-ID
                // inserts, without which the first application insert would collide with 23505.
                //
                // Its date offsets are anchored at the moment it FIRST seeds, so a long-lived demo
                // environment will drift stale (the dashboard's "expiring in under 90 days" tile empties
                // out). Refresh by truncating the seven compass.* tables and letting the next boot
                // re-seed against a current anchor.
                if (app.Environment.IsDevelopment() || seedCompassDirectoryOnStartup)
                {
                    var compassContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

                    // Anchored to the BUSINESS date (America/New_York), never to DateTime.UtcNow.
                    // The seeder's offsets are relative to this anchor, and status is judged against
                    // the same business date at read time — so a UTC anchor would seed the directory a
                    // day ahead of the application between about 20:00 Eastern and midnight, making a
                    // fixture meant as "ended yesterday" end today and its client read Active.
                    // CompassBusinessDateTests fails the build if a clock is read inside the module.
                    var businessDate = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>();
                    await CompassDirectorySeeder.SeedAsync(compassContext, businessDate.Today());
                    logger.LogInformation(
                        "Compass directory seeded successfully (requested by {Trigger})",
                        app.Environment.IsDevelopment()
                            ? "the Development environment"
                            : "StartupTasks:SeedCompassDirectory");
                }

                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "Seed attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}s",
                    attempt, maxRetries, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "All {MaxRetries} seed attempts failed; reference data may be missing", maxRetries);
            }
        }
    });
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapAuthEndpoints();
// Compass (Phase 49) — one call maps the module's endpoints, matching AddCompassServices() above.
app.MapCompassEmployeeEndpoints();
// Slice 2 of spec 009 (compass-integration-boundary) — invoice frequencies, its own route group.
app.MapCompassInvoiceFrequencyEndpoints();
// Slice 3 of spec 009 (compass-integration-boundary) — client, with derived status, its own route group.
app.MapCompassClientEndpoints();
// Slice 5 of spec 009 (compass-integration-boundary) — assignment-by-id, its own route group; the
// employee-scoped and client-scoped assignment collection reads are mapped inside
// MapCompassEmployeeEndpoints()/MapCompassClientEndpoints() above instead, inheriting those groups.
//
// Called by FULLY QUALIFIED type name, not extension-method syntax (`app.MapCompassAssignmentEndpoints()`),
// because `Endpoints.Write.CompassAssignmentEndpoints` below declares a method of the IDENTICAL name on
// the SAME `WebApplication` type — with both `Endpoints` and `Endpoints.Write` imported via `using`,
// extension-method call syntax is ambiguous (CS0121). A static call through the type name is
// unambiguous regardless of what is `using`d, so BOTH registrations below use this form.
LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.CompassAssignmentEndpoints.MapCompassAssignmentEndpoints(app);

// Compass configuration surfaces (feature 004). Each is its own route group under
// /api/compass/v1/admin, all gated by RolePolicy.CompassSuperAdmin through CompassAdminRouteGroup —
// NEVER RolePolicy.CompassAdmin, which also admits the read-only "Compass Admin" role (AC-44).
app.MapCompassAdminEmployeeTypeEndpoints();
app.MapCompassAdminInvoiceFrequencyTypeEndpoints();
app.MapCompassAdminEdjerEndpoints();
app.MapCompassAdminClientEndpoints();
app.MapCompassAdminSkillEndpoints();

// Contract periods (feature 010) — create, plus list and by-id reads. The reads exist because
// VERIFICATION needs them: SC-002 requires every migrated relationship checked at 100%, and a
// create-only surface makes that impossible in principle. The handler decides whether the caller IS
// the migration principal and passes that to the service, which is what gates the LegacyMigrated
// validation bypass (Principle VIII).
app.MapCompassAdminSowEndpoints();
app.MapCompassMigrationProvenanceEndpoints();

// ---- Developer tools (2026-08-26) -------------------------------------------------------------
// A Compass Super Admin can empty the five Compass operational tables from the LEAP launcher. This
// is destructive and irreversible, so the routes only EXIST where they are allowed to: when the gate
// refuses, nothing below is mapped and every developer-tools path answers 404 — the same shape as
// /_diag above, and stronger than an in-handler check, because a route that was never registered
// cannot be reached by a handler bug or a policy that stops being applied.
//
// The gate is BOTH an explicit opt-in flag AND "not Production". Read DeveloperToolsGate before
// reaching for app.Environment.IsDevelopment() here — deployed dev and every PR preview run
// ASPNETCORE_ENVIRONMENT=Staging, so IsDevelopment() would exclude the environment this was built for.
if (DeveloperToolsGate.IsEnabled(app.Configuration, app.Environment.EnvironmentName))
{
    app.MapCompassDeveloperToolsEndpoints();
    app.Logger.LogWarning(
        "Developer tools are ENABLED in environment {Environment}. "
            + "A Compass Super Admin can permanently clear all Compass data from the launcher.",
        app.Environment.EnvironmentName);
}
else if (DeveloperToolsGate.IsSuppressedByProduction(app.Configuration, app.Environment.EnvironmentName))
{
    // Configuration asked for a truncate button on a production host. Refusing is the whole point of
    // the second condition, but refusing SILENTLY would leave a real misconfiguration undiscovered
    // until someone needed the tools and found a 404.
    app.Logger.LogCritical(
        "{Key} is set to true but the environment is Production. Developer tools were NOT mapped. "
            + "Fix the configuration that enabled them.",
        DeveloperToolsGate.EnabledKey);
}

// The Compass application read surface (ADR-008) — the SPA's screens. Deliberately mapped alongside,
// not under, the versioned boundary above: these payloads are viewer-scoped and session-authenticated,
// so they are not part of the published contract out-of-monolith consumers bind to.
app.MapCompassTeamDirectoryEndpoints();
app.MapCompassClientDirectoryEndpoints();
app.MapCompassSkillsEndpoints();
// The assignment/SOW write surface (feature 006) — Compass's first audited write path. Mounted on the
// application surface (never /api/compass/v1) under RolePolicy.CompassOps, alongside the read routes.
// Fully qualified for the same reason as the versioned-boundary registration above: extension-method
// syntax is ambiguous once both Endpoints.CompassAssignmentEndpoints (spec 009 Slice 5) and
// Endpoints.Write.CompassAssignmentEndpoints (this one) are in scope.
LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write.CompassAssignmentEndpoints.MapCompassAssignmentEndpoints(app);
app.MapCompassSowEndpoints();
app.MapCompassDashboardEndpoints();
app.MapCompassReportEndpoints();
app.MapAuditLogEndpoints();
app.MapAdminUserRoleEndpoints();
app.MapAdminSystemSettingEndpoints();
app.MapImpersonateEndpoints();

// /_diag/secrets returns presence flags for Google/Slack/SMTP secrets so operators
// can confirm secret flow in non-Production environments (Development, Staging,
// ephemeral previews). Production returns 404 via this gate. Tracked for removal: #39.
if (!app.Environment.IsProduction())
{
    app.MapDiagnosticEndpoints();
}

app.Run();

/// <summary>Application entry point. Marked <c>public partial</c> so the integration-test project can reference <c>WebApplicationFactory&lt;Program&gt;</c> — standard .NET 10 top-level-statements test-hosting pattern.</summary>
public partial class Program { }

/// <summary>
/// The Npgsql-backed implementation of <see cref="IDatabaseBootstrapCommands"/>, used only by the
/// <c>--ensure-database</c> mode above.
/// </summary>
/// <remarks>
/// <para>
/// Why it lives in this file. It is composition-root code — it exists solely to serve that one
/// mode — and <c>api/Program.cs</c> is this repository's sanctioned startup-code coverage exclusion.
/// Putting the raw provider I/O here therefore adds NO new entry to the exclusion set, which is frozen
/// at 32. A new <c>api/**/*.cs</c> file would have to reach 100% line coverage from <c>tests/unit</c>
/// alone, and raw provider I/O cannot.
/// </para>
/// <para>
/// It is NOT untested — do not conclude that from the absence of unit coverage. Its real-server
/// behaviour is proven in <c>tests/integration/Data/DatabaseBootstrapperIntegrationTests.cs</c> against
/// a real PostgreSQL container, and the shipped binary is run end to end with its exit codes recorded
/// in the phase findings. Because the test project cannot reference a type declared here, that
/// integration class re-states this implementation; the recorded shipped-binary run is the check that
/// keeps the two from drifting.
/// </para>
/// </remarks>
/// <param name="maintenanceConnectionString">A connection string pointed at the maintenance database, never at the target database (which may not exist yet).</param>
internal sealed class NpgsqlDatabaseBootstrapCommands(string maintenanceConnectionString) : IDatabaseBootstrapCommands
{
    /// <inheritdoc />
    public async Task ProbeServerAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Parameterised. The seam hands over a NAME, not a fragment of SQL, precisely so this cannot
        // become concatenation without going out of its way.
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            connection);
        command.Parameters.AddWithValue("name", databaseName);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <inheritdoc />
    public async Task CreateDatabaseAsync(string quotedIdentifier, CancellationToken cancellationToken)
    {
        // A PLAIN connection with NO transaction and NO execution strategy. CREATE DATABASE cannot run
        // inside a transaction block, and an EF execution strategy would wrap the work in one — which is
        // why this deliberately does not go anywhere near the DbContext.
        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        // The identifier arrives already validated against an anchored pattern AND already quoted by
        // DatabaseBootstrapper. CREATE DATABASE cannot take a parameter, so those two locks are the
        // only thing between a chart value and arbitrary SQL.
        await using var command = new NpgsqlCommand($"CREATE DATABASE {quotedIdentifier}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
