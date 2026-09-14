namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// The narrow command seam the database bootstrap talks to. Deliberately three members and no more.
/// </summary>
/// <remarks>
/// This exists so <c>DatabaseBootstrapper</c> performs no input or output of its own and can reach
/// 100% line coverage from the unit suite without an exclusion. Every member added here must also be
/// implemented by the production adapter in <c>api/Program.cs</c>, where the per-file coverage gate
/// does not reach — so keep it at three. The create parameter is a quoted identifier, not a name:
/// <c>CREATE DATABASE</c> cannot take a parameter, so the caller validates the name against an
/// anchored pattern and quotes it first (see <c>DatabaseBootstrapper.QuoteIdentifier</c>) — two
/// independent locks. The existence check takes a plain name, and the implementation must parameterise
/// it; the signature is a name rather than SQL so concatenating it takes going out of your way.
/// </remarks>
public interface IDatabaseBootstrapCommands
{
    /// <summary>Opens a connection to the maintenance database and issues a trivial statement. Throws if the server is unreachable.</summary>
    Task ProbeServerAsync(CancellationToken cancellationToken);

    /// <summary>Returns true when a database with the given name exists. Implementations MUST pass the name as a query parameter.</summary>
    Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken);

    /// <summary>Creates the database named by an already-validated, already-quoted identifier. Must run outside any transaction.</summary>
    Task CreateDatabaseAsync(string quotedIdentifier, CancellationToken cancellationToken);
}
