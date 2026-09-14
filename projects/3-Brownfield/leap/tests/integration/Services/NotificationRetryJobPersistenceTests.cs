using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlackNet.Blocks;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Services;

/// <summary>
/// <see cref="NotificationRetryJob"/> must close the persistence boundary, so the status transition and
/// the incremented <c>retry_count</c> reach PostgreSQL and the <c>RetryCount &lt; 1</c> guard engages.
/// </summary>
/// <remarks>
/// A unit test cannot catch this. The unit suite drives the job through
/// <c>InMemoryNotificationLogRepository</c>, a <c>List&lt;&gt;</c> that needs no save, so the mutation
/// "persists" in the double whether or not anything calls <c>SaveChangesAsync</c> — which is why
/// <c>NotificationRetryJobChannelTests.ASuccessfulRetry_IsRecordedAsSent</c> stayed green throughout.
/// Only a real round-trip separates a mutated tracked entity from a persisted row, and the re-read must
/// open a fresh <see cref="LeapDbContext"/>: identity resolution would hand back the tracked instance
/// and prove nothing. Every assertion here opens its own scope and reads <c>AsNoTracking</c>.
/// </remarks>
public class NotificationRetryJobPersistenceTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    // Held explicitly rather than captured from the primary constructor: the base class also takes it,
    // and capturing it into this type's state as well is CS9107. Same shape as
    // SlackUserIdCachePersistenceTests, the precedent for deriving a host inside a shared fixture.
    private readonly IntegrationTestFactory _factory = factory;

    private const string EmployeeId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Recipient = "retry.persistence@leadingedje.com";
    private const string Poisoned = "retry.poisoned@leadingedje.com";
    private const string SmtpRefused = "smtp refused the connection";
    private const string OriginalFailure = "the original failure";
    private const string SlackDown = "slack api returned ratelimited";

    [Fact]
    public async Task ASuccessfulRetry_PersistsSentAndTheIncrementedRetryCount()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var host = HostWith(new RecordingEmailSender());
        var id = await SeedFailedNotificationAsync(host);

        // Act
        await RunRetryPassAsync(host);

        // Assert -- read back through a context that never saw the mutation
        var stored = await ReadFreshAsync(host, id);
        stored.Status.ShouldBe("Sent", "the status transition must reach the database, not just the change tracker");
        stored.RetryCount.ShouldBe(1, "a retried notice ends at exactly one attempt -- not zero, not two");
        stored.SentAt.ShouldNotBeNull();
        stored.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task ARetryThatFails_PersistsTheFailureAndTheIncrementedRetryCount()
    {
        // Arrange -- the half that causes the duplicates. A retry whose failure is not recorded looks
        // exactly like one that never ran, so the next pass sends it again.
        await ResetDatabaseAsync();
        using var host = HostWith(new RecordingEmailSender(throwWith: SmtpRefused));
        var id = await SeedFailedNotificationAsync(host);

        // Act
        await RunRetryPassAsync(host);

        // Assert
        var stored = await ReadFreshAsync(host, id);
        stored.Status.ShouldBe("Failed");
        stored.RetryCount.ShouldBe(1, "the attempt was spent, and only the persisted count can say so");
        stored.ErrorMessage.ShouldBe(SmtpRefused);
    }

    [Fact]
    public async Task ASecondPassOverTheSameFailedRow_DoesNotRetryItAgain()
    {
        // Arrange -- the defect in its observable form. The job runs every five minutes over a
        // thirty-minute window; without a persisted retry_count the RetryCount < 1 guard never engages
        // and the same notice goes out roughly six times.
        await ResetDatabaseAsync();
        var emailSender = new RecordingEmailSender(throwWith: SmtpRefused);
        using var host = HostWith(emailSender);
        var id = await SeedFailedNotificationAsync(host);

        // Act -- two independent passes, each with its own scope and its own DbContext
        await RunRetryPassAsync(host);
        await RunRetryPassAsync(host);

        // Assert
        emailSender.Attempts.Count.ShouldBe(
            1,
            "the first pass spent the single permitted retry; the second must read retry_count = 1 "
                + "from PostgreSQL and skip the row");

        var stored = await ReadFreshAsync(host, id);
        stored.RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task TwoConcurrentPasses_SendTheFailedNoticeExactlyOnce()
    {
        // Arrange -- the race the sequential test above cannot reach. Two replicas run the same pass at
        // the same instant; both read retry_count = 0 before either writes, so without a claim step both
        // send. The coordinating sender parks each entrant until a second arrives (or the window lapses),
        // which forces the read-read-before-write ordering the race needs (#599).
        await ResetDatabaseAsync();
        var sender = new CoordinatingEmailSender(expectedConcurrent: 2, window: TimeSpan.FromSeconds(2));
        using var host = HostWith(sender);
        var id = await SeedFailedNotificationAsync(host);

        // Act
        await Task.WhenAll(RunRetryPassAsync(host), RunRetryPassAsync(host));

        // Assert
        sender.Sent.Count.ShouldBe(
            1,
            "the retry must be once in TOTAL, not once per replica -- the atomic claim lets exactly one "
                + "pass win the row");

        var stored = await ReadFreshAsync(host, id);
        stored.Status.ShouldBe("Sent");
        stored.RetryCount.ShouldBe(1, "the claim owns the single increment; the losing pass must not bump it to 2");
    }

    [Fact]
    public async Task ClaimThenRelease_RoundTripsRetryCountAgainstRealPostgres()
    {
        // Arrange -- the repository's atomic claim/release, exercised directly: claim once wins, a
        // second claim loses, and a release makes the row claimable again.
        await ResetDatabaseAsync();
        using var host = HostWith(new RecordingEmailSender());
        var id = await SeedFailedNotificationAsync(host);

        // Act & Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<INotificationLogRepository>();

            (await repository.TryClaimForRetryAsync(id)).ShouldBeTrue("the first claim on a retry_count = 0 row wins");
            (await repository.TryClaimForRetryAsync(id)).ShouldBeFalse("a second claim on the same row loses");

            (await ReadFreshAsync(host, id)).RetryCount.ShouldBe(1);

            await repository.ReleaseRetryClaimAsync(id);
        }

        (await ReadFreshAsync(host, id)).RetryCount.ShouldBe(0, "release makes the row claimable again");
    }

    [Fact]
    public async Task APoisonedRowWithAnOverLongReason_DoesNotStrandTheRestOfTheBatch()
    {
        // Arrange -- error_message is varchar(2000) and the job writes it with the exception's own text.
        // A send failure whose message runs long would raise 22001 on the outcome save; the job
        // truncates it so the save succeeds, the claim leaves retry_count = 1 (the spent attempt), and
        // the row behind the poisoned one is still reached. GetFailedForRetryAsync orders by created_at,
        // so the poisoned row sits at the head.
        await ResetDatabaseAsync();
        var emailSender = new RecordingEmailSender(
            throwFor: to => to == Poisoned ? new string('x', 5_000) : "smtp refused the connection");
        using var host = HostWith(emailSender);

        var poisonedId = await SeedFailedNotificationAsync(host, Poisoned, "late_reminder_Thu5pm");
        var followerId = await SeedFailedNotificationAsync(host);
        await AgeAsync(host, poisonedId, minutes: 10);
        await AgeAsync(host, followerId, minutes: 5);

        // Act
        await RunRetryPassAsync(host);

        // Assert -- the row behind the poisoned one was reached at all
        emailSender.Attempts.ShouldBe([Poisoned, Recipient]);

        var poisoned = await ReadFreshAsync(host, poisonedId);
        poisoned.RetryCount.ShouldBe(1, "an unpersistable reason must not cost the row its spent attempt");
        poisoned.ErrorMessage.ShouldNotBeNull();
        poisoned.ErrorMessage.Length.ShouldBeLessThanOrEqualTo(2000);

        var follower = await ReadFreshAsync(host, followerId);
        follower.RetryCount.ShouldBe(
            1,
            "one poisoned row at the head of the batch must not disable retry for every row behind it");
    }

    [Fact]
    public async Task ARetryWithNoUsableRecipient_PersistsSkippedRatherThanSent()
    {
        // Arrange -- #552's reachable shape: Channel="slack" with no slack_user_id, because
        // NotificationService writes that column pre-resolution. Both send branches are skipped, nothing
        // throws, and the pass used to fall through to "Sent" -- durably recording a delivery that never
        // happened, stamping sent_at, and erasing the original reason.
        await ResetDatabaseAsync();
        var emailSender = new RecordingEmailSender();
        using var host = HostWith(emailSender);
        var id = await SeedFailedNotificationAsync(host, channel: "slack");

        // Act
        await RunRetryPassAsync(host);

        // Assert
        emailSender.Attempts.ShouldBeEmpty("a Slack-channel row must not silently fall back to email");

        var stored = await ReadFreshAsync(host, id);
        stored.Status.ShouldBe("Skipped", "nothing was attempted, so the ledger must not claim a delivery");
        stored.SentAt.ShouldBeNull();
        stored.RetryCount.ShouldBe(1, "the row must leave the retry set rather than be re-examined forever");
        stored.ErrorMessage.ShouldNotBeNullOrEmpty();
        stored.ErrorMessage.ShouldContain(OriginalFailure);
    }

    [Fact]
    public async Task ARetryDeliveredByEmailWhereSlackThrows_PersistsSentAndLeavesTheRetryQuery()
    {
        // Arrange -- half of a `both` row is out on the wire when the other half throws. Failed is the
        // only status GetFailedForRetryAsync selects, so persisting Failed here queues the delivered
        // email for another send. The reason embeds the exception text, which is a new writer into the
        // varchar(2000) column, so it is worth one real round trip.
        await ResetDatabaseAsync();
        var emailSender = new RecordingEmailSender();
        using var host = HostWith(emailSender, new ThrowingSlackClient(SlackDown));
        var id = await SeedFailedNotificationAsync(host, channel: "both", slackUserId: "U12345");

        // Act
        await RunRetryPassAsync(host);

        // Assert
        emailSender.Attempts.ShouldHaveSingleItem();

        var stored = await ReadFreshAsync(host, id);
        stored.Status.ShouldBe("Sent", "the email was delivered; Failed would re-send it");
        stored.SentAt.ShouldNotBeNull();
        stored.RetryCount.ShouldBe(1);
        stored.ErrorMessage.ShouldNotBeNullOrEmpty("a partial recorded as a clean success is a lie");
        stored.ErrorMessage.ShouldContain("also wanted Slack");
        stored.ErrorMessage.ShouldContain(SlackDown);

        (await RetryCandidatesAsync(host)).ShouldBeEmpty();
    }

    /// <summary>The rows a later pass would pick up, read through the job's own query.</summary>
    private static async Task<IReadOnlyList<NotificationLog>> RetryCandidatesAsync(
        WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationLogRepository>();

        return await repository.GetFailedForRetryAsync();
    }

    /// <summary>
    /// Inserts one <c>Failed</c> row inside the thirty-minute window the job filters on, and returns its id.
    /// </summary>
    /// <remarks>
    /// <c>CreatedAt</c> is stamped to <c>UtcNow</c> by <see cref="LeapDbContext.SaveChangesAsync"/> on
    /// insert, which is inside the window by construction, so it is not set here — use
    /// <see cref="AgeAsync"/> when a test needs a deterministic order between two rows.
    /// </remarks>
    private static async Task<long> SeedFailedNotificationAsync(
        WebApplicationFactory<Program> host,
        string recipient = Recipient,
        string notificationType = "late_reminder_Fri5pm",
        string channel = "email",
        string? slackUserId = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var log = new NotificationLog
        {
            EmployeeId = EmployeeId,
            NotificationType = notificationType,
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = channel,
            Status = "Failed",
            RecipientEmail = recipient,
            SlackUserId = slackUserId,
            ErrorMessage = OriginalFailure,
            RetryCount = 0,
        };

        context.NotificationLogs.Add(log);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return log.Id;
    }

    /// <summary>
    /// Backdates a row's <c>created_at</c>, so an order-dependent test does not rely on two inserts
    /// landing on different ticks.
    /// </summary>
    /// <remarks>
    /// <c>SaveChangesAsync</c> stamps <c>created_at</c> only on insert, so a modify leaves the value
    /// set here.
    /// </remarks>
    private static async Task AgeAsync(WebApplicationFactory<Program> host, long id, int minutes)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var log = await context.NotificationLogs.SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);
        log.CreatedAt = DateTime.UtcNow.AddMinutes(-minutes);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <remarks>
    /// The job is constructed directly rather than scheduled: the integration host strips Quartz
    /// entirely (see <c>IntegrationTestFactory</c>), and a real trigger would make the test wait on a
    /// clock. It resolves everything it needs from the host's own scope factory, so the repository and
    /// the <see cref="LeapDbContext"/> underneath it are the real, PostgreSQL-backed ones.
    /// <c>Execute</c> never reads its <c>IJobExecutionContext</c>, so none is constructed.
    /// </remarks>
    private static Task RunRetryPassAsync(WebApplicationFactory<Program> host) =>
        new NotificationRetryJob(host.Services.GetRequiredService<IServiceScopeFactory>()).Execute(null!);

    private static async Task<NotificationLog> ReadFreshAsync(WebApplicationFactory<Program> host, long id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        return await context.NotificationLogs
            .AsNoTracking()
            .SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);
    }

    /// <remarks>
    /// Replaces the two senders, and nothing else. Neither shipped mock can be made to fail — the
    /// observation <c>NotificationRetryJobChannelTests</c> already records — and both are production
    /// code, so extending them would be a production edit. The data context is deliberately NOT
    /// re-registered: this host inherits the shared Testcontainers connection string.
    /// </remarks>
    private WebApplicationFactory<Program> HostWith(
        IEmailSender emailSender, ISlackClient? slackClient = null) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                Replace(services, typeof(IEmailSender), emailSender);

                if (slackClient is not null)
                {
                    Replace(services, typeof(ISlackClient), slackClient);
                }
            }));

    private static void Replace(IServiceCollection services, Type serviceType, object instance)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == serviceType).ToList())
        {
            services.Remove(descriptor);
        }

        services.AddSingleton(serviceType, instance);
    }

    /// <remarks>
    /// Fails every direct message, standing in for a Slack outage or a rate limit. Block messages throw
    /// a different exception on purpose: the notification paths under test send plain direct messages,
    /// so reaching that member would mean the test is exercising something else.
    /// </remarks>
    private sealed class ThrowingSlackClient(string message) : ISlackClient
    {
        public Task SendDirectMessageAsync(string slackUserId, NotificationPayload payload) =>
            throw new InvalidOperationException(message);

        public Task SendBlockMessageAsync(string slackUserId, IList<Block> blocks, string fallbackText) =>
            throw new NotSupportedException("the notification paths under test send plain direct messages");

        public Task<string?> ResolveSlackUserIdAsync(string email) => Task.FromResult<string?>(null);
    }

    /// <remarks>
    /// Counts attempts, and can be made to fail; the shipped mock does neither. <paramref name="throwFor"/>
    /// varies the failure by recipient, which is what lets one row in a batch be poisoned while the rest
    /// behave ordinarily.
    /// </remarks>
    private sealed class RecordingEmailSender(
        string? throwWith = null,
        Func<string, string?>? throwFor = null) : IEmailSender
    {
        public List<string> Attempts { get; } = [];

        public Task SendAsync(string toEmail, string subject, string body, EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
        {
            Attempts.Add(toEmail);

            var message = throwFor is null ? throwWith : throwFor(toEmail);

            return message is null
                ? Task.CompletedTask
                : throw new InvalidOperationException(message);
        }
    }

    /// <remarks>
    /// Thread-safe, and parks each send until <paramref name="expectedConcurrent"/> callers have arrived
    /// or <paramref name="window"/> elapses. Without a claim step both passes reach here and record;
    /// with one, only the winner arrives and proceeds after the window. The park is what makes the race
    /// deterministic rather than dependent on scheduler luck.
    /// </remarks>
    private sealed class CoordinatingEmailSender(int expectedConcurrent, TimeSpan window) : IEmailSender
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _sent = new();
        private int _entered;

        public IReadOnlyCollection<string> Sent => _sent;

        public async Task SendAsync(
            string toEmail,
            string subject,
            string body,
            EmailBodyFormat format = EmailBodyFormat.Html,
            EmailFrom? from = null)
        {
            Interlocked.Increment(ref _entered);
            var deadline = DateTime.UtcNow + window;
            while (Volatile.Read(ref _entered) < expectedConcurrent && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            _sent.Enqueue(toEmail);
        }
    }
}
