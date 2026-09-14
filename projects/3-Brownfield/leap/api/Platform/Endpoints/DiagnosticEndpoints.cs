namespace LeadingEDJE.Leap.Api.Platform.Endpoints;

/// <summary>
/// Exposes <c>GET /_diag/secrets</c> so operators can confirm Google Docs, Slack and SES
/// configuration reached the running pod.
/// </summary>
/// <remarks>
/// Returns presence flags only, never the secret values, and is registered only outside Production,
/// so a production deployment answers 404. Remove it once the check is superseded by a pod-level
/// secret audit.
/// </remarks>
public static class DiagnosticEndpoints
{
    /// <summary>
    /// Maps the diagnostic endpoints under <c>/_diag</c>. Call only inside an
    /// <c>IsDevelopment()</c> branch so production deployments return 404.
    /// </summary>
    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/_diag")
            .WithTags("Diagnostics");

        group.MapGet("/secrets", GetSecretsPresence);

        return app;
    }

    private static IResult GetSecretsPresence(IConfiguration config)
    {
        var google = !string.IsNullOrWhiteSpace(config["GoogleCredentials"]) ? "present" : "absent";
        var slack = !string.IsNullOrWhiteSpace(config["Slack:BotToken"]) ? "present" : "absent";
        // Keyed on the SES transport's own setting. Nothing sets Email:SmtpHost any more, so a flag
        // reading it would answer "absent" in every environment.
        var ses = !string.IsNullOrWhiteSpace(config["Email:SesRegion"]) ? "present" : "absent";
        return Results.Ok(new { google, slack, ses });
    }
}
