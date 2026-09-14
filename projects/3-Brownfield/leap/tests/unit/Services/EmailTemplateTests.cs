using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Covers the branded email bodies and the notification payload they render from.
/// </summary>
/// <remarks>
/// The templates are transport-agnostic, so they are covered apart from whichever sender puts them
/// on the wire.
/// </remarks>
public class EmailTemplateTests
{
    [Fact]
    public void LateReminder_ContainsEmployeeNameAndDate()
    {
        // Act
        var (html, plainText) = EmailTemplates.LateReminder(
            "Alice Smith", new DateOnly(2026, 3, 14), "https://app.example.com");

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("March 14, 2026");
        html.ShouldContain("https://app.example.com");
        html.ShouldContain("Open Timesheet");
        plainText.ShouldContain("Alice Smith");
        plainText.ShouldContain("March 14, 2026");
    }

    [Fact]
    public void UnsubmittedReport_ContainsManagerGrouping()
    {
        // Arrange
        var unsubmitted = new List<(string ManagerName, IReadOnlyList<string> EmployeeNames)>
        {
            ("Bob Manager", new List<string> { "Alice", "Charlie" }),
            ("Carol Manager", new List<string> { "Dave" })
        };

        // Act
        var (html, plainText) = EmailTemplates.UnsubmittedReport(
            unsubmitted, new DateOnly(2026, 3, 14), "https://app.example.com");

        // Assert
        html.ShouldContain("Bob Manager");
        html.ShouldContain("Alice");
        html.ShouldContain("Charlie");
        html.ShouldContain("Carol Manager");
        html.ShouldContain("Dave");
        html.ShouldContain("Open Dashboard");
        plainText.ShouldContain("Bob Manager");
        plainText.ShouldContain("Dave");
    }

    [Fact]
    public void BankTimeException_ContainsBalanceDetails()
    {
        // Act
        var (html, plainText) = EmailTemplates.BankTimeException(
            "Alice Smith", 120.5m, 40.0m, "https://app.example.com");

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("120.5");
        html.ShouldContain("40.0");
        html.ShouldContain("View Balances");
        plainText.ShouldContain("Alice Smith");
        plainText.ShouldContain("120.5");
    }

    [Fact]
    public void BankTimeException_WithoutPto_OmitsPtoLine()
    {
        // Act
        var (html, _) = EmailTemplates.BankTimeException(
            "Alice Smith", 120.5m, null, "https://app.example.com");

        // Assert
        html.ShouldNotContain("PTO Remaining");
    }

    [Fact]
    public void Reopened_ContainsReasonAndDate()
    {
        // Act
        var (html, plainText) = EmailTemplates.Reopened(
            "Alice Smith", new DateOnly(2026, 3, 14), "Missing project entries",
            "https://app.example.com");

        // Assert
        html.ShouldContain("Alice Smith");
        html.ShouldContain("March 14, 2026");
        html.ShouldContain("Missing project entries");
        html.ShouldContain("Open Timesheet");
        plainText.ShouldContain("Missing project entries");
    }

    [Fact]
    public void AllTemplates_ProduceValidHtmlWithPlainTextDerivable()
    {
        // Act
        var templates = new[]
        {
            EmailTemplates.LateReminder("Test", new DateOnly(2026, 1, 1), "https://app.example.com"),
            EmailTemplates.BankTimeException("Test", 100m, 50m, "https://app.example.com"),
            EmailTemplates.Reopened("Test", new DateOnly(2026, 1, 1), "reason", "https://app.example.com")
        };

        // Assert
        foreach (var (html, plainText) in templates)
        {
            html.ShouldContain("<!DOCTYPE html>");
            html.ShouldContain("LeadingEDJE Timesheet");
            plainText.ShouldNotBeNullOrWhiteSpace();
            plainText.ShouldNotContain("<");  // no HTML tags in plain text
        }
    }

    [Fact]
    public void AllTemplates_ContainAppLink()
    {
        // Act
        var appUrl = "https://timesheet.leadingedje.com";
        var templates = new[]
        {
            EmailTemplates.LateReminder("Test", new DateOnly(2026, 1, 1), appUrl),
            EmailTemplates.BankTimeException("Test", 100m, 50m, appUrl),
            EmailTemplates.Reopened("Test", new DateOnly(2026, 1, 1), "reason", appUrl)
        };

        // Assert
        foreach (var (html, _) in templates)
        {
            html.ShouldContain(appUrl);
        }
    }

    [Fact]
    public void NotificationPayload_Extended_WithCategoryBreakdown()
    {
        // Act
        var payload = new NotificationPayload(
            "Subject", "Body",
            CategoryBreakdown: new List<CategoryTotal>
            {
                new("Client A", 20m),
                new("PTO", 8m)
            },
            OooEvents: new List<string> { "PTO: March 14" },
            Over40PayOut: 2.5m,
            Over40Banked: 1.5m);

        // Assert
        payload.CategoryBreakdown.ShouldNotBeNull();
        payload.CategoryBreakdown.Count.ShouldBe(2);
        payload.OooEvents.ShouldNotBeNull();
        payload.OooEvents.Count.ShouldBe(1);
        payload.Over40PayOut.ShouldBe(2.5m);
        payload.Over40Banked.ShouldBe(1.5m);
    }

    [Fact]
    public void NotificationPayload_NewFields_DefaultToNull()
    {
        // Act
        var payload = new NotificationPayload("Subject", "Body");

        // Assert
        payload.CategoryBreakdown.ShouldBeNull();
        payload.OooEvents.ShouldBeNull();
        payload.Over40PayOut.ShouldBeNull();
        payload.Over40Banked.ShouldBeNull();
    }
}
