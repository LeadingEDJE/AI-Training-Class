using LeadingEDJE.Leap.Api.Platform.Auth;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

public class ForcedHttpsSchemeMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_HttpRequest_RewritesSchemeToHttps()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        var nextCalled = false;
        var middleware = new ForcedHttpsSchemeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Request.Scheme.ShouldBe("https");
        context.Request.IsHttps.ShouldBeTrue();
        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HttpsRequest_LeavesSchemeUntouched()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        var nextCalled = false;
        var middleware = new ForcedHttpsSchemeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Request.Scheme.ShouldBe("https");
        nextCalled.ShouldBeTrue();
    }
}
