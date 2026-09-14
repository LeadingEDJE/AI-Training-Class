namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// Makes an email's originating environment visible, lets a non-production environment stop sending,
/// and redirects non-production recipients off real addresses. Bound from the <c>Email</c> section.
/// </summary>
public class EmailEnvironmentOptions
{
    /// <summary>The configuration section this binds from, shared with the SMTP settings.</summary>
    public const string SectionName = "Email";

    /// <summary>
    /// How this environment identifies itself in outgoing mail — <c>DEV</c>, <c>PR #42</c>. Empty or
    /// absent means production, and production mail is passed through untouched.
    /// </summary>
    /// <remarks>
    /// Explicit configuration rather than <see cref="IHostEnvironment.EnvironmentName"/>, and it has to
    /// be: every deployed environment runs <c>ASPNETCORE_ENVIRONMENT: Staging</c> except production, so
    /// the host environment cannot tell deployed dev from a PR preview — exactly the distinction a
    /// reader of the email needs. Deriving the label from the host would produce mail labelled
    /// "Staging" from three different places.
    /// Treated as untrusted text when it reaches an HTML body: the preview label is operator-supplied
    /// and the web tier's sibling value (<c>EPHEMERAL_LABEL</c>) is derived from a PR title.
    /// </remarks>
    public string? EnvironmentLabel { get; set; }

    /// <summary>
    /// Whether outgoing email is actually delivered. Defaults to <see langword="true"/>, so an
    /// environment that sets nothing keeps sending exactly as it does today.
    /// </summary>
    /// <remarks>
    /// Honoured in every environment, not only in Development. A switch that silently refuses
    /// to work in production would be discovered the first time someone needed it there, and the
    /// startup log records the choice so a disabled mailer is visible rather than mysterious. The
    /// default is the safe direction: the flag has to be set to <c>false</c> to stop mail, never to
    /// keep it flowing.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The single address every non-production message goes to instead of its real recipient. Empty
    /// suppresses the send over a real transport rather than delivering it; ignored in production.
    /// </summary>
    /// <remarks>
    /// A single sink, not a domain allowlist: Compass's migrated dev directory invents
    /// <c>first.last@leadingedje.com</c> for every active EDJEr, so a domain rule would pass exactly
    /// the addresses it must stop. The intended recipient goes into the delivered body instead.
    /// Deliberately NOT keyed on <see cref="EnvironmentLabel"/> — the two answer different questions
    /// and must not be collapsed; this keys on <c>IHostEnvironment.IsProduction()</c> so an absent
    /// label cannot read as production. Residual, closed by configuration rather than by the type
    /// system: an absent <c>ASPNETCORE_ENVIRONMENT</c> also resolves to <c>Production</c>, and it is
    /// <c>values.yaml</c>'s <c>Staging</c> default that closes it. See the doc named on the sender.
    /// </remarks>
    public string? RedirectAllTo { get; set; }

    /// <summary>
    /// The address every non-production message is sent as, replacing the sender the caller asked
    /// for. Empty or absent means no override; ignored entirely in production.
    /// </summary>
    /// <remarks>
    /// A delivery constraint, not a preference: dev's account verifies only the
    /// <c>dev.leadingedje.com</c> SES identity, so a send as <c>timesheet@leadingedje.com</c> or
    /// Compass's <c>no-reply@leadingedje.com</c> is refused by SES and by the role's
    /// <c>ses:FromAddress</c> condition. It is rewritten in
    /// <see cref="EnvironmentStampingEmailSender"/> rather than the callers because AC-43/FR-029
    /// requires Compass's address verbatim. A null sender is overridden too — null means "use the
    /// configured default", a production address. Keyed on <c>IHostEnvironment.IsProduction()</c>,
    /// never on <see cref="EnvironmentLabel"/>: an absent label must not read as production.
    /// </remarks>
    public string? NonProductionFromAddress { get; set; }

    /// <summary>
    /// The display name paired with <see cref="NonProductionFromAddress"/>. Empty or absent means an
    /// address-only From.
    /// </summary>
    /// <remarks>
    /// Applied only when <see cref="NonProductionFromAddress"/> is, and applied with it:
    /// <see cref="Platform.Interfaces.EmailFrom"/> is all-or-nothing by contract, so an override never
    /// mixes its address with a caller's display name or with <c>Email:FromName</c>.
    /// </remarks>
    public string? NonProductionFromName { get; set; }
}
