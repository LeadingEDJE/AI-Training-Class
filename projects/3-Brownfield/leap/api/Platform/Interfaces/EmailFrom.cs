namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// An explicit sender for a single message: the From address and its display name (may be empty). A
/// <see langword="null"/> at the <see cref="IEmailSender"/> seam means use the configured default.
/// </summary>
/// <remarks>
/// This exists so a module that must send from an address other than the app-wide
/// <c>Email:FromAddress</c> — Compass coach notices, from <c>no-reply@leadingedje.com</c> — can say so
/// per message without moving the Timesheet sender. It is all-or-nothing: when a sender is supplied
/// both its address and its display name are used, so an override never mixes a module's address with
/// the Timesheet display name.
/// </remarks>
public sealed record EmailFrom(string Address, string DisplayName = "");
