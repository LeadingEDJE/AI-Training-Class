using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

// Compass's own entities. Aliased rather than imported bare, matching CompassAdminEdjerEndpointsTests:
// this project has a `.Compass` namespace, so an unqualified import reads as though it referred to that.
using CompassClient = LeadingEDJE.Leap.Api.Modules.Compass.Client;
using CompassClientAssignment = LeadingEDJE.Leap.Api.Modules.Compass.ClientAssignment;
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;
using CompassSowType = LeadingEDJE.Leap.Api.Modules.Compass.SowType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// US4/#68 — the coach hears about it, once. <c>contracts/coach-notification.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// The message literals are asserted character-for-character, in the same spirit as the SAML
/// denial-reason strings. The contract says so explicitly: changing any literal means updating its test
/// in the same commit. That includes the apostrophe in <c>&lt;EDJEr name&gt;'s</c>.
/// </para>
/// <para>
/// Why these live in the integration project rather than as unit tests. The trigger is a state
/// TRANSITION — <c>EndDate</c> moving from a real SQL <c>NULL</c> to a value, for the first time ever —
/// and the once-ever guarantee is derived from rows in <c>public.notification_logs</c>. Both are claims
/// about the database, and an in-memory double would only prove the double behaves as written.
/// </para>
/// <para>
/// The idempotency case worth reading twice is SC-014. The platform log's natural key is
/// <c>(EmployeeId, NotificationType, PeriodWeekStart)</c>, so one EDJEr with two assignments ending on
/// the same day would collapse into a single email under a naive implementation. The contract requires
/// the entity identity to travel inside <c>NotificationType</c> precisely so it does not.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CoachNotificationTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CoachNotificationTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string AssignmentRoute = "/api/compass/assignments";

    private static readonly DateOnly StartDate = new(2024, 4, 1);
    private static readonly DateOnly EndDate = new(2025, 9, 30);

    /// <summary>The end date as the body renders it — mm/dd/yyyy, per the repo-wide mandate in #234.</summary>
    private const string EndDateRendered = "09/30/2025";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The captured outbox. <c>MockEmailSender</c> is the registered singleton whenever
    /// <c>Email:SesRegion</c> is unset, which is every test host.
    /// </summary>
    private MockEmailSender Outbox()
    {
        var sender = Services.GetRequiredService<IEmailSender>();
        sender.ShouldBeOfType<MockEmailSender>(
            "these assertions are meaningless against a real sender; if this fails, Email:SesRegion leaked"
        );

        var outbox = (MockEmailSender)sender;

        // The sender is a singleton and the database is truncated per test, so the outbox has to be
        // cleared explicitly or one test's sends are counted by the next.
        outbox.SentEmails.Clear();
        return outbox;
    }

    // ------------------------------------------------- FR-025 / AC-29: the first end-dating notifies

    [Fact]
    public async Task EndDatingAnAssignmentForTheFirstTime_SendsExactlyOneEmailToTheCoach()
    {
        // Arrange
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();
        var client = _factory.AsCompassOps();

        // Act
        var response = await EndDateAsync(client, fixture.AssignmentId, EndDate);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var sent = outbox.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe(fixture.CoachEmail, "the coach is the recipient");
    }

    [Fact]
    public async Task TheAssignmentEndEmail_MatchesTheContractCharacterForCharacter()
    {
        // Arrange — contract §2. The apostrophe in "<EDJEr name>'s" is part of the assertion.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();

        // Act
        await EndDateAsync(_factory.AsCompassOps(), fixture.AssignmentId, EndDate);

        // Assert
        var sent = outbox.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("coach.mentor@example.test");
        sent.Subject.ShouldBe("Ada Lovelace Assignment Change");
        sent.Body.ShouldBe(
            $"Ada Lovelace's assignment at Buckeye Mutual is coming to an end on {EndDateRendered}"
        );
        // AC-43/FR-029 (#531): from no-reply@, NOT the app-wide Timesheet Email:FromAddress; address-only.
        var from = sent.From.ShouldNotBeNull();
        from.Address.ShouldBe("no-reply@leadingedje.com");
        from.DisplayName.ShouldBe("");
    }

    [Fact]
    public async Task TheAssignmentEnd_PersistsTheRenderedMessageAndSender_RoundTrippingToPostgres()
    {
        // Arrange — #532: the retry worker re-sends from the notification_log row, so the rendered subject,
        // body and no-reply@ sender must actually round-trip to real Postgres (the new columns), not merely
        // to an in-memory fake. This is the only assertion that exercises the Npgsql mapping of those columns.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        Outbox();

        // Act
        await EndDateAsync(_factory.AsCompassOps(), fixture.AssignmentId, EndDate);

        // Assert — read the row back from the database, not the outbox.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var row = await db.Set<NotificationLog>().SingleAsync(Token);
        row.Subject.ShouldBe("Ada Lovelace Assignment Change");
        row.Body.ShouldBe(
            $"Ada Lovelace's assignment at Buckeye Mutual is coming to an end on {EndDateRendered}"
        );
        row.FromAddress.ShouldBe("no-reply@leadingedje.com");
        row.FromName.ShouldBe("");
    }

    [Fact]
    public async Task TheAssignmentEndEmail_GoesToTheCoachAloneWithNoCopiedRecipients()
    {
        // Arrange — contract §2: sole recipient, no cc, no bcc. Not Ops, not Sales, not the EDJEr.
        // IEmailSender carries one address, so the assertion is that exactly one send occurred to
        // exactly the coach — and specifically NOT to the EDJEr, who is the plausible mistake.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();

        // Act
        await EndDateAsync(_factory.AsCompassOps(), fixture.AssignmentId, EndDate);

        // Assert
        outbox.SentEmails.Count.ShouldBe(1);
        outbox.SentEmails.ShouldAllBe(email => email.To == "coach.mentor@example.test");
        outbox.SentEmails.ShouldNotContain(email => email.To == "ada.lovelace@example.test");
    }

    [Fact]
    public async Task TheAssignmentEndEmail_ContainsNoLinkBackIntoCompass()
    {
        // Arrange — FR-033/AC-43. Worth an explicit test rather than trusting the body literal: the
        // platform's EmailTemplates wraps every message in a layout that embeds an app URL, so reusing
        // one of those helpers would silently violate this.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();

        // Act
        await EndDateAsync(_factory.AsCompassOps(), fixture.AssignmentId, EndDate);

        // Assert
        var sent = outbox.SentEmails.ShouldHaveSingleItem();
        sent.Body.ShouldNotContain("http", Case.Insensitive);
        sent.Body.ShouldNotContain("://");
        sent.Body.ShouldNotContain("localhost", Case.Insensitive);
    }

    // ------------------------------------------------------- FR-027 / AC-34: once, and only once

    [Fact]
    public async Task EditingTheAssignmentAfterTheFirstEndDating_SendsNoFurtherEmail()
    {
        // Arrange — the once-ever rule. Two further edits, including moving the end date again, which is
        // the edit most likely to look like a fresh trigger.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();
        var client = _factory.AsCompassOps();

        await EndDateAsync(client, fixture.AssignmentId, EndDate);
        outbox.SentEmails.Count.ShouldBe(1, "the arrangement depends on the first send happening");

        // Act
        await EndDateAsync(client, fixture.AssignmentId, EndDate.AddMonths(1));
        await EndDateAsync(client, fixture.AssignmentId, EndDate.AddMonths(2));

        // Assert
        outbox.SentEmails.Count.ShouldBe(1, "FR-027: later edits do not re-notify");
    }

    [Fact]
    public async Task ClearingTheEndDateThenSettingItAgain_SendsNoSecondEmail()
    {
        // Arrange — the contract's named edge case: "first time" means once EVER, not once per
        // NULL-to-value transition. A guard that only looked at the transition would send twice here.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();
        var client = _factory.AsCompassOps();

        await EndDateAsync(client, fixture.AssignmentId, EndDate);
        outbox.SentEmails.Count.ShouldBe(1);

        // Act
        await EndDateAsync(client, fixture.AssignmentId, null);
        await EndDateAsync(client, fixture.AssignmentId, EndDate);

        // Assert
        outbox.SentEmails.Count.ShouldBe(1, "once ever, not once per transition");
    }

    // ------------------------------------------------------------ SC-014 / FR-027b: the collision

    [Fact]
    public async Task TwoAssignmentsForOneEdjerEndedTheSameDay_SendTwoEmails()
    {
        // Arrange — SC-014, and the reason the entity identity has to travel inside NotificationType.
        // The platform log's key is (EmployeeId, NotificationType, PeriodWeekStart); with a shared type
        // string these two notifications collide on all three columns and the second is suppressed.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var secondClientId = await SeedClientAsync("Granville Retail Group");
        var secondAssignmentId = await SeedAssignmentAsync(fixture.EdjerId, secondClientId);
        var outbox = Outbox();
        var client = _factory.AsCompassOps();

        // Act — same EDJEr, same day, two different engagements.
        await EndDateAsync(client, fixture.AssignmentId, EndDate);
        await EndDateAsync(client, secondAssignmentId, EndDate);

        // Assert
        outbox.SentEmails.Count.ShouldBe(2, "one per assignment, not one per EDJEr per week");
        outbox.SentEmails.ShouldAllBe(email => email.To == "coach.mentor@example.test");
        outbox
            .SentEmails.Select(email => email.Body)
            .Distinct()
            .Count()
            .ShouldBe(2, "each names its own client");
    }

    // ---------------------------------------------- FR-026 / AC-32: an extension SOW notifies too

    [Fact]
    public async Task AddingAnExtensionSow_SendsOneEmailWithTheExtensionBody()
    {
        // Arrange — contract §2's second body.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();
        var client = _factory.AsCompassOps();

        // Act
        var response = await AddSowAsync(client, fixture.AssignmentId, CompassSowType.SowExtension);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var sent = outbox.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("coach.mentor@example.test");
        sent.Subject.ShouldBe(
            "Ada Lovelace Assignment Change",
            "contract §2: the subject is the same for both triggers"
        );
        sent.Body.ShouldBe(
            $"Ada Lovelace has received an updated SOW at Buckeye Mutual through {EndDateRendered}"
        );
        sent.Body.ShouldNotContain("http", Case.Insensitive);
    }

    // ------------------------------------------------- FR-028 / AC-31: an initial contract does not

    [Fact]
    public async Task AddingAnInitialContractSow_SendsNoEmail()
    {
        // Arrange — FR-028. The first contract period is the engagement starting, not changing.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();

        // Act
        var response = await AddSowAsync(
            _factory.AsCompassOps(),
            fixture.AssignmentId,
            CompassSowType.InitialContract
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        outbox.SentEmails.ShouldBeEmpty("an initial contract notifies nobody");
    }

    [Fact]
    public async Task EditingAnExtensionSowAfterItWasAdded_SendsNoFurtherEmail()
    {
        // Arrange — FR-027/AC-34 for the SOW trigger, mirroring the assignment case above.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        var outbox = Outbox();
        var client = _factory.AsCompassOps();

        var created = await AddSowAsync(client, fixture.AssignmentId, CompassSowType.SowExtension);
        var sowId = await SowIdOfAsync(fixture.AssignmentId);
        outbox.SentEmails.Count.ShouldBe(1, "the arrangement depends on the add notifying");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{AssignmentRoute}/{fixture.AssignmentId}/sows/{sowId}",
            new UpdateSowRequest(
                CompassSowType.SowExtension,
                RateIncrease: true,
                StartDate,
                EndDate.AddMonths(3),
                "revised"
            ),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        outbox.SentEmails.Count.ShouldBe(1, "editing a period does not re-notify");
    }

    // -------------------------------------------------------- the log, per contract §4 and FR-035

    [Fact]
    public async Task ASentNotification_IsRecordedInTheLogWithTheEntityIdentityInItsType()
    {
        // Arrange — the once-ever rule is derived from these rows, so their shape is load-bearing
        // (Obligation O-1). Asserting it here is what makes the derivation legible to the next reader.
        await ResetDatabaseAsync();
        var fixture = await SeedCoachedEdjerAsync();
        Outbox();

        // Act
        await EndDateAsync(_factory.AsCompassOps(), fixture.AssignmentId, EndDate);

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var logs = await db.Set<NotificationLog>().AsNoTracking().ToListAsync(Token);

        var log = logs.ShouldHaveSingleItem();
        log.NotificationType.ShouldContain(
            fixture.AssignmentId.ToString(),
            Case.Insensitive,
            "contract §4: the entity identity travels in NotificationType"
        );
        log.NotificationType.ShouldStartWith("compass.", Case.Insensitive);
        log.EmployeeId.ShouldBe(fixture.EdjerId.ToString());
        log.Status.ShouldBe("Sent");
        log.Channel.ShouldBe("email");
        log.RecipientEmail.ShouldBe("coach.mentor@example.test");
    }

    // ============================================================ US5/#69 — no coach is not an error

    [Fact]
    public async Task EndDatingTheAssignmentOfAnEdjerWithNoCoach_SendsNothingAndStillSucceeds()
    {
        // Arrange — FR-034, AC-43, J14a. CoachEmployeeId is nullable, so this is an ordinary case rather
        // than a theoretical one: internal, non-billable staff are exactly the EDJErs without a coach.
        // Implemented as a failure — an exception, a validation error, a fallback recipient — it would
        // make ending their assignment impossible, which is why US5 is its own story.
        await ResetDatabaseAsync();
        var fixture = await SeedUncoachedEdjerAsync();
        var outbox = Outbox();

        // Act
        var response = await EndDateAsync(_factory.AsCompassOps(), fixture.AssignmentId, EndDate);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "the end-dating itself must succeed");
        outbox.SentEmails.ShouldBeEmpty("no email, and no substitute recipient");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassClientAssignment>()
            .AsNoTracking()
            .SingleAsync(assignment => assignment.Id == fixture.AssignmentId, Token);
        stored.EndDate.ShouldBe(EndDate, "the action the notice describes actually happened");
    }

    [Fact]
    public async Task AddingAnExtensionSowForAnEdjerWithNoCoach_SendsNothingAndStillRecordsTheSow()
    {
        // Arrange — J16b, the same branch reached through the other trigger.
        await ResetDatabaseAsync();
        var fixture = await SeedUncoachedEdjerAsync();
        var outbox = Outbox();

        // Act
        var response = await AddSowAsync(
            _factory.AsCompassOps(),
            fixture.AssignmentId,
            CompassSowType.SowExtension
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the SOW is recorded regardless");
        outbox.SentEmails.ShouldBeEmpty();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var sows = await db.Set<LeadingEDJE.Leap.Api.Modules.Compass.Sow>()
            .AsNoTracking()
            .ToListAsync(Token);
        sows.ShouldHaveSingleItem().SowType.ShouldBe(CompassSowType.SowExtension);
    }

    [Fact]
    public async Task ANoCoachSkip_IsRecordedAsSkipped_AndIsDistinguishableFromASend()
    {
        // Arrange — FR-035. The skip is silent to the USER and visible to OPERATORS: it must be tellable
        // apart from a delivery failure, because a skip is a normal outcome and a failure is not. The row
        // is written rather than omitted precisely so the distinction survives.
        await ResetDatabaseAsync();
        var uncoached = await SeedUncoachedEdjerAsync();
        var coached = await SeedCoachedEdjerAsync();
        Outbox();
        var client = _factory.AsCompassOps();

        // Act — one of each, so the assertion is a contrast rather than a single row in isolation.
        await EndDateAsync(client, uncoached.AssignmentId, EndDate);
        await EndDateAsync(client, coached.AssignmentId, EndDate);

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var logs = await db.Set<NotificationLog>().AsNoTracking().ToListAsync(Token);

        logs.Count.ShouldBe(2, "a skip is recorded, not omitted");

        var skipped = logs.Where(log => log.Status == "Skipped").ShouldHaveSingleItem();
        skipped.EmployeeId.ShouldBe(uncoached.EdjerId.ToString());
        skipped.RecipientEmail.ShouldBeNull("there was nobody to address it to");
        skipped.ErrorMessage.ShouldBeNull(
            "a skip is not a failure -- an error message here would misreport a normal outcome"
        );

        var sent = logs.Where(log => log.Status == "Sent").ShouldHaveSingleItem();
        sent.EmployeeId.ShouldBe(coached.EdjerId.ToString());
        sent.RecipientEmail.ShouldBe("coach.mentor@example.test");
    }

    [Fact]
    public async Task AnUncoachedEdjer_IsNotRetriedOnALaterEdit()
    {
        // Arrange — the skip claims the idempotency slot like any other outcome. Without that, every
        // later edit would re-attempt a notice for an EDJEr who will never have a recipient.
        await ResetDatabaseAsync();
        var fixture = await SeedUncoachedEdjerAsync();
        Outbox();
        var client = _factory.AsCompassOps();

        await EndDateAsync(client, fixture.AssignmentId, EndDate);

        // Act
        await EndDateAsync(client, fixture.AssignmentId, EndDate.AddMonths(1));

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var logs = await db.Set<NotificationLog>().AsNoTracking().ToListAsync(Token);
        logs.ShouldHaveSingleItem().Status.ShouldBe("Skipped");
    }

    // ----------------------------------------------------------------------------------- arrangement

    private sealed record CoachedEdjer(
        int EdjerId,
        int CoachId,
        string CoachEmail,
        int ClientId,
        int AssignmentId
    );

    /// <summary>
    /// Seeds a coach, an EDJEr who reports to them, a client, and one open-ended assignment.
    /// </summary>
    private async Task<CoachedEdjer> SeedCoachedEdjerAsync()
    {
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var coachId = await SeedEdjerAsync(
            employeeTypeId,
            "Grace",
            "Mentor",
            "coach.mentor@example.test",
            coachEmployeeId: null
        );
        var edjerId = await SeedEdjerAsync(
            employeeTypeId,
            "Ada",
            "Lovelace",
            "ada.lovelace@example.test",
            coachEmployeeId: coachId
        );
        var clientId = await SeedClientAsync("Buckeye Mutual");
        var assignmentId = await SeedAssignmentAsync(edjerId, clientId);

        return new CoachedEdjer(edjerId, coachId, "coach.mentor@example.test", clientId, assignmentId);
    }

    /// <summary>Seeds an EDJEr with NO coach, a client, and one open-ended assignment (US5).</summary>
    private async Task<CoachedEdjer> SeedUncoachedEdjerAsync()
    {
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var edjerId = await SeedEdjerAsync(
            employeeTypeId,
            "Rowan",
            "Ashby",
            $"rowan.ashby.{Guid.NewGuid():N}@example.test",
            coachEmployeeId: null
        );
        var clientId = await SeedClientAsync("Internal Projects");
        var assignmentId = await SeedAssignmentAsync(edjerId, clientId);

        return new CoachedEdjer(edjerId, CoachId: 0, CoachEmail: string.Empty, clientId, assignmentId);
    }

    private async Task<int> SeedEmployeeTypeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new CompassEmployeeType
        {
            TypeName = $"Type-{Guid.NewGuid():N}"[..20],
            IsActive = true,
        };
        db.Set<CompassEmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(Token);

        return employeeType.Id;
    }

    private async Task<int> SeedEdjerAsync(
        int employeeTypeId,
        string firstName,
        string lastName,
        string email,
        int? coachEmployeeId
    )
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var edjer = new CompassEmployee
        {
            FirstName = firstName,
            LastName = lastName,
            HireDate = new DateOnly(2020, 1, 6),
            Email = email,
            EmployeeTypeId = employeeTypeId,
            CoachEmployeeId = coachEmployeeId,
            StateOfResidence = "OH",
            IsActive = true,
            TimesheetRequired = true,
        };
        db.Set<CompassEmployee>().Add(edjer);
        await db.SaveChangesAsync(Token);

        return edjer.Id;
    }

    private async Task<int> SeedClientAsync(string clientName)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var client = new CompassClient { ClientName = clientName, IsInternal = false };
        db.Set<CompassClient>().Add(client);
        await db.SaveChangesAsync(Token);

        return client.Id;
    }

    /// <summary>Inserts an OPEN-ENDED assignment — the state the end-dating trigger transitions from.</summary>
    private async Task<int> SeedAssignmentAsync(int employeeId, int clientId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var assignment = new CompassClientAssignment
        {
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = StartDate,
            EndDate = null,
        };
        db.Set<CompassClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }

    private async Task<int> SowIdOfAsync(int assignmentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        return await db.Set<LeadingEDJE.Leap.Api.Modules.Compass.Sow>()
            .AsNoTracking()
            .Where(sow => sow.ClientAssignmentId == assignmentId)
            .Select(sow => sow.Id)
            .FirstAsync(Token);
    }

    private static Task<HttpResponseMessage> EndDateAsync(
        HttpClient client,
        int assignmentId,
        DateOnly? endDate
    ) =>
        client.PutAsJsonAsync(
            $"{AssignmentRoute}/{assignmentId}",
            new UpdateAssignmentRequest(StartDate, endDate, null),
            Token
        );

    private static Task<HttpResponseMessage> AddSowAsync(
        HttpClient client,
        int assignmentId,
        CompassSowType sowType
    ) =>
        client.PostAsJsonAsync(
            $"{AssignmentRoute}/{assignmentId}/sows",
            new CreateSowRequest(sowType, RateIncrease: false, StartDate, EndDate, null),
            Token
        );
}
