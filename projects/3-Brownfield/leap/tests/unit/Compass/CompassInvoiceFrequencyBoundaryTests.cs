using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The published Compass Directory boundary's invoice-frequencies read (spec 009 Slice 2; FR-010).
/// </summary>
/// <remarks>
/// Hits <c>/api/compass/v1/invoice-frequencies</c> over <see cref="TestWebApplicationFactory"/>, the
/// same HTTP surface a real out-of-process consumer would call. This is the boundary's own transport,
/// distinct from the admin configuration surface's <c>InvoiceFrequencyTypeDto</c> (ADR-008: two
/// surfaces, two contracts).
/// </remarks>
public class CompassInvoiceFrequencyBoundaryTests : IClassFixture<TestWebApplicationFactory>
{
    private const int ActiveTypeId = 1;
    private const int InactiveTypeId = 2;

    private readonly TestWebApplicationFactory _factory;

    public CompassInvoiceFrequencyBoundaryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private const string Route = "/api/compass/v1/invoice-frequencies";

    [Fact]
    public async Task Get_ReturnsOnlyActiveInvoiceFrequencyTypes()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassInvoiceFrequencyDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.ShouldHaveSingleItem();
        dtos[0].Id.ShouldBe(ActiveTypeId);
        dtos[0].TypeName.ShouldBe("Monthly");
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<InvoiceFrequencyType>().Any())
        {
            return;
        }

        context.Set<InvoiceFrequencyType>().AddRange(
            new InvoiceFrequencyType { Id = ActiveTypeId, TypeName = "Monthly", IsActive = true },
            new InvoiceFrequencyType { Id = InactiveTypeId, TypeName = "Retired", IsActive = false });

        context.SaveChanges();
    }
}
