using System.Reflection;
using System.Reflection.Emit;

using LeadingEDJE.Leap.Api.Platform.Services.OpenApi;

using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services.OpenApi;

/// <summary>
/// Direct unit coverage for <see cref="PrivateHandlerXmlDocOperationTransformer.ParseDocComments"/> --
/// the file-parsing half of the transformer that <c>LoadDocComments</c> cannot exercise without a real
/// assembly on disk. Found in adversarial code review of spec 009 Phase 5: the original
/// <c>XDocument.Load</c> call had no guard against a malformed or mid-write XML doc file, so a
/// concurrent build catching the file half-written would crash the whole OpenAPI document generation
/// instead of degrading to "no doc comment applied" the way a missing file already did.
/// </summary>
public class PrivateHandlerXmlDocOperationTransformerTests
{
    [Fact]
    public void ParseDocComments_MissingFile_ReturnsEmptyMap()
    {
        // Arrange
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");

        // Act
        var result = PrivateHandlerXmlDocOperationTransformer.ParseDocComments(xmlPath);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void ParseDocComments_MalformedXml_ReturnsEmptyMapInsteadOfThrowing()
    {
        // Arrange -- an unclosed element, the shape a concurrent build could leave mid-write.
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        File.WriteAllText(xmlPath, "<doc><members><member name=\"M:Foo.Bar\"><summary>Oops");

        try
        {
            // Act
            var result = PrivateHandlerXmlDocOperationTransformer.ParseDocComments(xmlPath);

            // Assert
            result.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public void ParseDocComments_WellFormedMethodMember_ReturnsSummaryAndDescription()
    {
        // Arrange
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        File.WriteAllText(
            xmlPath,
            """
            <doc>
              <members>
                <member name="M:Foo.Bar.Baz">
                  <summary>Gets the thing.</summary>
                  <remarks>Detailed remarks.</remarks>
                </member>
              </members>
            </doc>
            """);

        try
        {
            // Act
            var result = PrivateHandlerXmlDocOperationTransformer.ParseDocComments(xmlPath);

            // Assert
            result.ShouldContainKey("M:Foo.Bar.Baz");
            result["M:Foo.Bar.Baz"].Summary.ShouldBe("Gets the thing.");
            result["M:Foo.Bar.Baz"].Description.ShouldBe("Detailed remarks.");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public void BuildDocCommentId_NoParameters_ReturnsIdWithoutParens()
    {
        // Arrange
        var method = typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.NoParameters))!;

        // Act
        var id = PrivateHandlerXmlDocOperationTransformer.BuildDocCommentId(method);

        // Assert -- FixtureHandlers is nested, so its FullName uses '+'; BuildDocCommentId normalizes
        // that the same way the compiler's own doc-comment IDs do, hence the '.' here.
        var declaringTypeName = typeof(FixtureHandlers).FullName!.Replace('+', '.');
        id.ShouldBe($"M:{declaringTypeName}.{nameof(FixtureHandlers.NoParameters)}");
    }

    [Fact]
    public void BuildDocCommentId_ClosedParameterTypes_ReturnsIdWithParameterList()
    {
        // Arrange
        var method = typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.WithParameters))!;

        // Act
        var id = PrivateHandlerXmlDocOperationTransformer.BuildDocCommentId(method);

        // Assert
        var declaringTypeName = typeof(FixtureHandlers).FullName!.Replace('+', '.');
        id.ShouldBe(
            $"M:{declaringTypeName}.{nameof(FixtureHandlers.WithParameters)}"
            + "(System.String,System.Int32)");
    }

    [Fact]
    public void BuildDocCommentId_MethodWithNoDeclaringType_ReturnsNullRatherThanAGuess()
    {
        // Arrange -- DynamicMethod.DeclaringType always returns null (it is not a member of any real
        // type), the one shape of MethodInfo this codebase can hand BuildDocCommentId with a null
        // declaring type -- endpoint handlers are always real instance/static methods on a real type.
        var method = new DynamicMethod("Anonymous", typeof(void), Type.EmptyTypes);

        // Act
        var id = PrivateHandlerXmlDocOperationTransformer.BuildDocCommentId(method);

        // Assert
        id.ShouldBeNull();
    }

    [Fact]
    public void BuildDocCommentId_OpenGenericParameter_ReturnsNullRatherThanAGuess()
    {
        // Arrange -- a bare generic method parameter has no closed FullName, the shape this narrow
        // re-implementation declines rather than mis-encode.
        var method = typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.WithGenericParameter))!;

        // Act
        var id = PrivateHandlerXmlDocOperationTransformer.BuildDocCommentId(method);

        // Assert
        id.ShouldBeNull();
    }

    /// <summary>Reflection targets for <see cref="PrivateHandlerXmlDocOperationTransformer.BuildDocCommentId"/>.</summary>
    private static class FixtureHandlers
    {
        public static void NoParameters()
        {
        }

        public static void WithParameters(string name, int count)
        {
        }

        public static void WithGenericParameter<T>(T value)
        {
        }
    }

    [Fact]
    public async Task TransformAsync_BothAlreadySet_LeavesThemUntouched()
    {
        // Arrange -- the built-in generator already answered this operation; the transformer must
        // not touch context at all, so an otherwise-empty context is deliberate here.
        var operation = new OpenApiOperation { Summary = "Existing summary", Description = "Existing description" };
        var context = BuildContext(relativePath: null, endpointMetadata: []);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldBe("Existing summary");
        operation.Description.ShouldBe("Existing description");
    }

    [Fact]
    public async Task TransformAsync_AdminSurfaceRoute_LeavesSummaryAndDescriptionNull()
    {
        // Arrange -- the admin configuration surface is out of scope for this transformer, see its
        // class-level remarks. Deliberately no MethodInfo in endpointMetadata: the admin check must
        // short-circuit before endpoint metadata is ever inspected.
        var operation = new OpenApiOperation();
        var context = BuildContext(relativePath: "api/compass/v1/admin/edjers", endpointMetadata: []);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldBeNull();
        operation.Description.ShouldBeNull();
    }

    [Fact]
    public async Task TransformAsync_NoMethodInfoInEndpointMetadata_LeavesSummaryAndDescriptionNull()
    {
        // Arrange -- a route with no MethodInfo among its endpoint metadata (nothing for this
        // transformer to look up a doc comment for).
        var operation = new OpenApiOperation();
        var context = BuildContext(relativePath: "api/compass/v1/edjers", endpointMetadata: ["not a MethodInfo"]);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldBeNull();
        operation.Description.ShouldBeNull();
    }

    [Fact]
    public async Task TransformAsync_MethodHasOpenGenericParameter_LeavesSummaryAndDescriptionNull()
    {
        // Arrange -- BuildDocCommentId declines a bare generic parameter and returns null; TransformAsync
        // must treat that exactly like "no doc comment available", not throw or guess.
        var operation = new OpenApiOperation();
        var method = typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.WithGenericParameter))!;
        var context = BuildContext(relativePath: "api/compass/v1/edjers", endpointMetadata: [method]);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldBeNull();
        operation.Description.ShouldBeNull();
    }

    [Fact]
    public async Task TransformAsync_NoMatchingXmlDocEntry_LeavesSummaryAndDescriptionNull()
    {
        // Arrange -- a real, resolvable MethodInfo whose declaring type lives in THIS (test) assembly.
        // The test project builds with GenerateDocumentationFile off, so no XML doc file exists for it
        // and the lookup must come back empty rather than throw.
        var operation = new OpenApiOperation();
        var method = typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.NoParameters))!;
        var context = BuildContext(relativePath: "api/compass/v1/edjers", endpointMetadata: [method]);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldBeNull();
        operation.Description.ShouldBeNull();
    }

    [Fact]
    public async Task TransformAsync_MatchingXmlDocEntry_FillsSummaryFromTheCompiledDocFile()
    {
        // Arrange -- a real API-assembly method, so LeadingEDJE.Leap.Api.xml carries a real <summary>.
        // ParseDocComments is the target because it has no <remarks>, which is what proves Description
        // stays null when the doc comment has none. Giving it one fails this test; keep it summary-only.
        var operation = new OpenApiOperation();
        var method = typeof(PrivateHandlerXmlDocOperationTransformer).GetMethod(
            nameof(PrivateHandlerXmlDocOperationTransformer.ParseDocComments),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var context = BuildContext(relativePath: "api/compass/v1/edjers", endpointMetadata: [method]);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldNotBeNull();
        operation.Description.ShouldBeNull();
    }

    [Fact]
    public async Task TransformAsync_DeclaringTypesAssemblyHasNoLocation_LeavesSummaryAndDescriptionNull()
    {
        // Arrange -- an in-memory-only dynamic assembly (never saved to disk) reports an empty
        // Location, the one shape LoadDocComments' "no location" guard exists for. A real endpoint
        // handler's assembly always has a location; this proves the guard degrades to "no doc comment"
        // instead of throwing on Path.ChangeExtension(string.Empty, ".xml").
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PrivateHandlerXmlDocOperationTransformerTests.Dynamic"), AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("DynamicModule");
        var typeBuilder = moduleBuilder.DefineType("DynamicType", TypeAttributes.Public);
        var methodBuilder = typeBuilder.DefineMethod(
            "DynamicMethod", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        methodBuilder.GetILGenerator().Emit(OpCodes.Ret);
        var dynamicType = typeBuilder.CreateType();
        var method = dynamicType.GetMethod("DynamicMethod")!;
        method.DeclaringType!.Assembly.Location.ShouldBe(string.Empty);

        var operation = new OpenApiOperation();
        var context = BuildContext(relativePath: "api/compass/v1/edjers", endpointMetadata: [method]);

        // Act
        await new PrivateHandlerXmlDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        // Assert
        operation.Summary.ShouldBeNull();
        operation.Description.ShouldBeNull();
    }

    private static OpenApiOperationTransformerContext BuildContext(string? relativePath, object[] endpointMetadata)
    {
        return new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
            Document = new OpenApiDocument(),
            Description = new ApiDescription
            {
                RelativePath = relativePath,
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata },
            },
        };
    }

    [Fact]
    public void ParseDocComments_NonMethodMembers_AreSkipped()
    {
        // Arrange -- types (T:), properties (P:) and fields (F:) are handled by the built-in schema
        // transformer already; only M: entries back an OpenAPI operation.
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        File.WriteAllText(
            xmlPath,
            """
            <doc>
              <members>
                <member name="T:Foo.Bar">
                  <summary>A type, not a method.</summary>
                </member>
              </members>
            </doc>
            """);

        try
        {
            // Act
            var result = PrivateHandlerXmlDocOperationTransformer.ParseDocComments(xmlPath);

            // Assert
            result.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }
}
