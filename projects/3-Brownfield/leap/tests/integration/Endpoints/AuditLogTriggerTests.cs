using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

public class AuditLogTriggerTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Insert_AuditLog_Succeeds()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var auditLog = new AuditLog
        {
            EntityType = "TestEntity",
            EntityId = "1",
            Action = "TestAction",
            Actor = "test-user",
            TriggeredBy = "IntegrationTest",
            Reason = "Verify insert allowed",
            Changes = "{}",
            Timestamp = DateTime.UtcNow
        };

        // Act
        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var inserted = await dbContext.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityType == "TestEntity", TestContext.Current.CancellationToken);
        inserted.ShouldNotBeNull();
    }

    [Fact]
    public async Task Update_AuditLog_ThrowsMySqlError()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var auditLog = new AuditLog
        {
            EntityType = "TestEntity",
            EntityId = "2",
            Action = "TestAction",
            Actor = "test-user",
            TriggeredBy = "IntegrationTest",
            Reason = "Original reason",
            Changes = "{}",
            Timestamp = DateTime.UtcNow
        };
        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        auditLog.Reason = "Modified reason";
        var ex = await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        ex.InnerException?.Message.ShouldContain("cannot be modified", Case.Insensitive);
    }

    [Fact]
    public async Task Delete_AuditLog_ThrowsMySqlError()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var auditLog = new AuditLog
        {
            EntityType = "TestEntity",
            EntityId = "3",
            Action = "TestAction",
            Actor = "test-user",
            TriggeredBy = "IntegrationTest",
            Reason = "Will try to delete",
            Changes = "{}",
            Timestamp = DateTime.UtcNow
        };
        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        dbContext.AuditLogs.Remove(auditLog);
        var ex = await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        ex.InnerException?.Message.ShouldContain("cannot be deleted", Case.Insensitive);
    }
}
