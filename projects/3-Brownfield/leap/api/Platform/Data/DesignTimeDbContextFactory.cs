#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — design-time factory.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeadingEDJE.Leap.Api.Platform.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LeapDbContext>
{
    public LeapDbContext CreateDbContext(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "127.0.0.1";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        // Must stay identical to the defaults in api/Program.cs and NpgsqlHealthCheck --
        // DatabaseNameDefaultsAgreeTests fails if they drift. The shipped EF migration bundle resolves
        // its connection through this factory, so an unset POSTGRES_DB in the deploy hook would migrate
        // whatever this line names; the chart marks the value `required` so that cannot happen.
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "leap_dev";
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "timesheet";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "changeme";

        var connectionString = $"Host={host};Port={port};Database={database};Username={user};Password={password}";

        var optionsBuilder = new DbContextOptionsBuilder<LeapDbContext>()
            .UseLeapPostgres(connectionString);

        return new LeapDbContext(optionsBuilder.Options);
    }
}
