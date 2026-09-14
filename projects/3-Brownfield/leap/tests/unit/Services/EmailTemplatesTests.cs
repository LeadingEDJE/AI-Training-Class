using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class EmailTemplatesTests
{
    private const string AppUrl = "https://timesheet.leadingedje.com";

    [Fact]
    public void LateReminder_ReturnsHtmlAndPlainText()
    {
        // Arrange & Act
        var (html, plainText) = EmailTemplates.LateReminder("Alice Smith", new DateOnly(2026, 3, 14), AppUrl);

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("March 14, 2026");
        html.ShouldContain("Timesheet Reminder");
        html.ShouldContain(AppUrl);
        html.ShouldContain("<!DOCTYPE html>");
        plainText.ShouldContain("Alice Smith");
        plainText.ShouldNotContain("<");
    }

    [Fact]
    public void UnsubmittedReport_RendersManagerEmployeeTable()
    {
        // Arrange
        var unsubmitted = new List<(string ManagerName, IReadOnlyList<string> EmployeeNames)>
        {
            ("Manager A", new List<string> { "Employee 1", "Employee 2" }),
            ("Manager B", new List<string> { "Employee 3" })
        };

        // Act
        var (html, plainText) = EmailTemplates.UnsubmittedReport(
            unsubmitted, new DateOnly(2026, 3, 14), AppUrl);

        // Assert
        html.ShouldContain("Manager A");
        html.ShouldContain("Employee 1");
        html.ShouldContain("Employee 2");
        html.ShouldContain("Manager B");
        html.ShouldContain("Employee 3");
        html.ShouldContain("Unsubmitted Timesheets Report");
        plainText.ShouldNotContain("<tr>");
    }

    [Fact]
    public void BankTimeException_WithPto_IncludesPtoLine()
    {
        // Arrange & Act
        var (html, _) = EmailTemplates.BankTimeException("Alice", 120.5m, 40.0m, AppUrl);

        // Assert
        html.ShouldContain("120.5");
        html.ShouldContain("40.0");
        html.ShouldContain("PTO Remaining");
        html.ShouldContain("Bank Time Exception");
    }

    [Fact]
    public void BankTimeException_WithoutPto_OmitsPtoLine()
    {
        // Arrange & Act
        var (html, _) = EmailTemplates.BankTimeException("Alice", 120.5m, null, AppUrl);

        // Assert
        html.ShouldContain("120.5");
        html.ShouldNotContain("PTO Remaining");
    }

    [Fact]
    public void Reopened_IncludesReasonAndEmployeeName()
    {
        // Arrange & Act
        var (html, plainText) = EmailTemplates.Reopened(
            "Alice Smith", new DateOnly(2026, 3, 14), "Missing entries", AppUrl);

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("Missing entries");
        html.ShouldContain("Timesheet Reopened");
        plainText.ShouldContain("Missing entries");
    }

    [Fact]
    public void Unsubmit_RendersCorrectTemplate()
    {
        // Arrange & Act
        var (html, plainText) = EmailTemplates.Unsubmit(
            "Alice Smith", new DateOnly(2026, 3, 14), AppUrl);

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("Timesheet Unsubmitted");
        html.ShouldContain("Draft status");
        plainText.ShouldNotContain("<");
    }

    [Fact]
    public void Comment_IncludesCommentTextInBlockquote()
    {
        // Arrange & Act
        var (html, plainText) = EmailTemplates.Comment(
            "Alice Smith", "Please add project codes", new DateOnly(2026, 3, 14), AppUrl);

        // Assert
        html.ShouldContain("Please add project codes");
        html.ShouldContain("Manager Comment");
        html.ShouldContain("blockquote");
        plainText.ShouldContain("Please add project codes");
    }

    [Fact]
    public void AllApproved_RendersProcessInvoicingLink()
    {
        // Arrange & Act
        var (html, plainText) = EmailTemplates.AllApproved(new DateOnly(2026, 3, 14), AppUrl);

        // Assert
        html.ShouldContain("All Timesheets Approved");
        html.ShouldContain("Process Invoicing");
        html.ShouldContain("March 14, 2026");
        plainText.ShouldNotContain("<");
    }

    [Fact]
    public void BalanceAdjustment_RendersAllFields()
    {
        // Arrange & Act
        var (html, plainText) = EmailTemplates.BalanceAdjustment(
            "Alice Smith", "PTO: -8h, Bank: +8h", "Bob Manager", "Year-end adjustment", AppUrl);

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("PTO: -8h, Bank: +8h");
        html.ShouldContain("Bob Manager");
        html.ShouldContain("Year-end adjustment");
        html.ShouldContain("Balance Adjustment");
        plainText.ShouldNotContain("<");
    }

    [Fact]
    public void HtmlToPlainText_StripsTagsAndDecodesEntities()
    {
        // Arrange
        var html = "<p>Hello &amp; <strong>World</strong></p><br/><p>Test &lt;value&gt;</p>";

        // Act
        var text = EmailTemplates.HtmlToPlainText(html);

        // Assert
        text.ShouldNotContain("<p>");
        text.ShouldNotContain("<strong>");
        text.ShouldContain("Hello & World");
        text.ShouldContain("Test <value>");
    }

    [Fact]
    public void HtmlToPlainText_HandlesQuotesAndApostrophes()
    {
        // Arrange
        var html = "<p>She said &quot;hello&quot; and it&#39;s fine &nbsp; end</p>";

        // Act
        var text = EmailTemplates.HtmlToPlainText(html);

        // Assert
        text.ShouldContain("She said \"hello\"");
        text.ShouldContain("it's fine");
    }

    [Fact]
    public void LateReminder_HtmlEncodesSpecialCharacters()
    {
        // Arrange & Act
        var (html, _) = EmailTemplates.LateReminder("O'Brien & Sons", new DateOnly(2026, 3, 14), AppUrl);

        // Assert
        html.ShouldContain("O&#39;Brien &amp; Sons");
        html.ShouldNotContain("O'Brien & Sons");
    }
}
