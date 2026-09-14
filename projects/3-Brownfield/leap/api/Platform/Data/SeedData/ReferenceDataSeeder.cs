#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — seed data.
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.SeedData;

/// <summary>
/// Seeds the platform-owned production-safe reference data — the <c>system_settings</c> rows — on
/// first startup.
/// </summary>
/// <remarks>
/// Platform-owned rows only. The Timesheet-owned lookups this once seeded live in that module, so
/// that no <c>api/Platform/</c> file names a module type. The name is load-bearing despite the
/// narrowed scope: <c>docs/ops/preview-auth.md</c> and the preview deploy workflow both identify
/// <c>ReferenceDataSeeder</c> as the seeder that runs in Staging, where the development seeder does
/// not.
/// </remarks>
public class ReferenceDataSeeder(LeapDbContext context)
{
    public async Task SeedAsync()
    {
        var changed = false;

        if (!await context.SystemSettings.AnyAsync())
        {
            SeedSystemSettings();
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync();
        }
    }

    private void SeedSystemSettings()
    {
        context.SystemSettings.AddRange(
            new SystemSetting { Key = "max_hours_per_day", Value = "24", Description = "Maximum hours allowed per day entry" },
            new SystemSetting { Key = "timesheet_lock_day", Value = "Wednesday", Description = "Day of week when previous period timesheets lock" },
            new SystemSetting { Key = "fiscal_year_start", Value = "January", Description = "Month when fiscal year begins" },
            new SystemSetting { Key = "default_work_hours", Value = "8", Description = "Standard work hours per day" },
            new SystemSetting { Key = "approval_reminder_days", Value = "2", Description = "Days after submission to send approval reminder" },
            new SystemSetting { Key = "data_retention_years", Value = "3", Description = "Rolling calendar years of data retained plus current year" },
            new SystemSetting { Key = "ooo_system_url", Value = "https://ooto.leadingedje.com", Description = "URL to external OOO/OOTO system (leave empty to hide link)" }
        );
    }

}
