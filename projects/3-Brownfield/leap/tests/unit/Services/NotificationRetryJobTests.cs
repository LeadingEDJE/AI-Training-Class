using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Jobs;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using LeadingEDJE.Leap.Api.Platform.Services.Slack;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class NotificationRetryJobTests : IDisposable
{
    private static readonly Guid TestEdjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly InMemoryNotificationLogRepository _notifLogRepo;
    private readonly MockSlackClient _slackClient;
    private readonly MockEmailSender _emailSender;
    private readonly LeapDbContext _context;
    private readonly IServiceProvider _serviceProvider;

    public NotificationRetryJobTests()
    {
        _notifLogRepo = new InMemoryNotificationLogRepository();
        _slackClient = new MockSlackClient();
        _emailSender = new MockEmailSender();

        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://timesheet.leadingedje.com"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<Platform.Interfaces.INotificationLogRepository>(_notifLogRepo);
        services.AddSingleton<Platform.Interfaces.ISlackClient>(_slackClient);
        services.AddSingleton<Platform.Interfaces.IEmailSender>(_emailSender);
        services.AddSingleton(_context);
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private NotificationRetryJob CreateJob()
    {
        var factory = new TestServiceScopeFactory(_serviceProvider);
        return new NotificationRetryJob(factory);
    }

    private static IJobExecutionContext CreateContext() => new TestJobExecutionContext("RetryEvery5min");

    [Fact]
    public async Task Execute_RetriesFailedNotifications()
    {
        // Arrange
        await _notifLogRepo.AddAsync(new NotificationLog
        {
            EmployeeId = TestEdjeId.ToString(),
            NotificationType = "late_reminder_Fri5pm",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = "email",
            Status = "Failed",
            RecipientEmail = "test@leadingedje.com",
            RetryCount = 0,
            CreatedAt = DateTime.UtcNow.AddMinutes(-3)
        });

        var job = CreateJob();

        // Act
        await job.Execute(CreateContext());

        // Assert - should attempt re-dispatch (email sent)
        _emailSender.SentEmails.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Execute_DoesNotRetryNotificationsWithRetryCountGe1()
    {
        // Arrange
        await _notifLogRepo.AddAsync(new NotificationLog
        {
            EmployeeId = TestEdjeId.ToString(),
            NotificationType = "late_reminder_Fri5pm",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = "email",
            Status = "Failed",
            RecipientEmail = "test@leadingedje.com",
            RetryCount = 1, // Already retried once
            CreatedAt = DateTime.UtcNow.AddMinutes(-3)
        });

        var job = CreateJob();

        // Act
        await job.Execute(CreateContext());

        // Assert - should NOT retry
        _emailSender.SentEmails.Count.ShouldBe(0);
    }

    private sealed class TestServiceScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestServiceScope(provider);

        private sealed class TestServiceScope(IServiceProvider provider) : IServiceScope
        {
            public IServiceProvider ServiceProvider => provider;
            public void Dispose() { }
        }
    }
}
