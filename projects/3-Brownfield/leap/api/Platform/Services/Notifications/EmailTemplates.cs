using System.Text;
using System.Text.RegularExpressions;

namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// Generates branded HTML and plain-text email templates for all notification types (reminders, approvals, balance adjustments, etc.).
/// </summary>
public static partial class EmailTemplates
{
    private const string BrandColor = "#1B4332";

    /// <summary>Builds the late-reminder email (HTML + plain-text pair) for an employee who has not yet submitted.</summary>
    public static (string Html, string PlainText) LateReminder(string employeeName, DateOnly weekEndDate, string appUrl)
    {
        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">Timesheet Reminder</h2>
            <p>Hi {Encode(employeeName)},</p>
            <p>Your timesheet for the week ending <strong>{weekEndDate:MMMM d, yyyy}</strong> has not been submitted.</p>
            <p>Please submit your timesheet as soon as possible.</p>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">Open Timesheet</a>
            </p>
            """, appUrl);

        return (html, HtmlToPlainText(html));
    }

    /// <summary>Builds the SuperAdmin unsubmitted-report email grouped by manager for a given week.</summary>
    public static (string Html, string PlainText) UnsubmittedReport(
        IReadOnlyList<(string ManagerName, IReadOnlyList<string> EmployeeNames)> unsubmitted,
        DateOnly weekEndDate,
        string appUrl)
    {
        var rows = new StringBuilder();
        foreach (var (managerName, employees) in unsubmitted)
        {
            foreach (var emp in employees)
            {
                rows.Append($"""
                    <tr>
                      <td style="padding: 8px; border: 1px solid #ddd;">{Encode(managerName)}</td>
                      <td style="padding: 8px; border: 1px solid #ddd;">{Encode(emp)}</td>
                    </tr>
                    """);
            }
        }

        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">Unsubmitted Timesheets Report</h2>
            <p>The following employees have not submitted their timesheets for the week ending <strong>{weekEndDate:MMMM d, yyyy}</strong>:</p>
            <table style="border-collapse: collapse; width: 100%;">
              <thead>
                <tr style="background-color: #f5f5f5;">
                  <th style="padding: 8px; border: 1px solid #ddd; text-align: left;">Manager</th>
                  <th style="padding: 8px; border: 1px solid #ddd; text-align: left;">Employee</th>
                </tr>
              </thead>
              <tbody>
                {rows}
              </tbody>
            </table>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">Open Dashboard</a>
            </p>
            """, appUrl);

        return (html, HtmlToPlainText(html));
    }

    /// <summary>Builds the bank-time-exception alert email summarising current bank-time balance and (optionally) PTO remaining.</summary>
    public static (string Html, string PlainText) BankTimeException(
        string employeeName,
        decimal bankTimeBalance,
        decimal? ptoRemaining,
        string appUrl)
    {
        var ptoLine = ptoRemaining.HasValue
            ? $"<p>PTO Remaining: <strong>{ptoRemaining.Value:F1} hours</strong></p>"
            : "";

        var html = WrapInLayout($"""
            <h2 style="color: #b45309;">Bank Time Exception Alert</h2>
            <p>Employee <strong>{Encode(employeeName)}</strong> has a bank time balance that requires attention.</p>
            <p>Current Bank Time Balance: <strong>{bankTimeBalance:F1} hours</strong></p>
            {ptoLine}
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">View Balances</a>
            </p>
            """, appUrl);

        return (html, HtmlToPlainText(html));
    }

    /// <summary>Builds the "timesheet reopened" email including the manager-supplied reason.</summary>
    public static (string Html, string PlainText) Reopened(
        string employeeName,
        DateOnly weekEndDate,
        string reason,
        string appUrl)
    {
        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">Timesheet Reopened</h2>
            <p>Hi {Encode(employeeName)},</p>
            <p>Your timesheet for the week ending <strong>{weekEndDate:MMMM d, yyyy}</strong> has been reopened.</p>
            <p><strong>Reason:</strong> {Encode(reason)}</p>
            <p>Please review and resubmit your timesheet.</p>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">Open Timesheet</a>
            </p>
            """, appUrl);

        return (html, HtmlToPlainText(html));
    }

    /// <summary>Builds the manager-facing "timesheet unsubmitted" email sent when an employee reverts to draft.</summary>
    public static (string Html, string PlainText) Unsubmit(
        string employeeName, DateOnly weekEndDate, string appUrl)
    {
        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">Timesheet Unsubmitted</h2>
            <p><strong>{Encode(employeeName)}</strong> has unsubmitted their timesheet for the week ending <strong>{weekEndDate:MMMM d, yyyy}</strong>.</p>
            <p>The timesheet has been returned to Draft status and will need to be resubmitted.</p>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">View Timesheet</a>
            </p>
            """, appUrl);
        return (html, HtmlToPlainText(html));
    }

    /// <summary>Builds the "manager comment on timesheet" email including the comment body.</summary>
    public static (string Html, string PlainText) Comment(
        string employeeName, string commentText, DateOnly weekEndDate, string appUrl)
    {
        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">Manager Comment on Timesheet</h2>
            <p>Hi {Encode(employeeName)},</p>
            <p>Your manager left a comment on your timesheet for the week ending <strong>{weekEndDate:MMMM d, yyyy}</strong>:</p>
            <blockquote style="border-left: 4px solid {BrandColor}; padding: 8px 16px; margin: 16px 0; color: #555;">{Encode(commentText)}</blockquote>
            <p>Please review and address the comment.</p>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">View Timesheet</a>
            </p>
            """, appUrl);
        return (html, HtmlToPlainText(html));
    }

    /// <summary>Builds the "all timesheets approved — ready to invoice" email sent to invoicing processors.</summary>
    public static (string Html, string PlainText) AllApproved(DateOnly weekEndDate, string appUrl)
    {
        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">All Timesheets Approved</h2>
            <p>All timesheets for the week ending <strong>{weekEndDate:MMMM d, yyyy}</strong> have been approved and are ready for invoicing.</p>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">Process Invoicing</a>
            </p>
            """, appUrl);
        return (html, HtmlToPlainText(html));
    }


    /// <summary>Builds the "balance adjustment" notification email with the field-level changes and HR reason.</summary>
    public static (string Html, string PlainText) BalanceAdjustment(
        string employeeName,
        string changesDescription,
        string adjustedBy,
        string reason,
        string appUrl)
    {
        var html = WrapInLayout($"""
            <h2 style="color: {BrandColor};">Balance Adjustment</h2>
            <p>Hi {Encode(employeeName)},</p>
            <p>Your balance has been adjusted by <strong>{Encode(adjustedBy)}</strong>.</p>
            <p><strong>Changes:</strong> {Encode(changesDescription)}</p>
            <p><strong>Reason:</strong> {Encode(reason)}</p>
            <p style="margin-top: 24px;">
              <a href="{Encode(appUrl)}" style="background-color: {BrandColor}; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">View Balances</a>
            </p>
            """, appUrl);

        return (html, HtmlToPlainText(html));
    }

    private static string WrapInLayout(string bodyContent, string appUrl)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8" /></head>
            <body style="font-family: 'DM Sans', Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; color: #333;">
              <div style="background-color: {BrandColor}; padding: 16px 24px; border-radius: 4px 4px 0 0;">
                <h1 style="color: #fff; margin: 0; font-size: 20px;">LeadingEDJE Timesheet</h1>
              </div>
              <div style="padding: 24px; border: 1px solid #e5e5e5; border-top: none; border-radius: 0 0 4px 4px;">
                {bodyContent}
              </div>
              <div style="padding: 16px; text-align: center; color: #999; font-size: 12px;">
                <p>LeadingEDJE Timesheet System</p>
                <p><a href="{Encode(appUrl)}" style="color: #999;">Open Timesheet App</a></p>
              </div>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Strips HTML tags, decodes common HTML entities, and collapses whitespace to produce a plain-text
    /// rendering suitable for email clients that prefer the text/plain alternative.
    /// </summary>
    public static string HtmlToPlainText(string html)
    {
        // Remove HTML tags, decode entities, normalize whitespace
        var text = TagRegex().Replace(html, " ");
        text = text.Replace("&amp;", "&")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&quot;", "\"")
                   .Replace("&#39;", "'")
                   .Replace("&nbsp;", " ");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text;
    }

    private static string Encode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
