using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HotelManagement.Api.OpenApi;

public static class OpenApiSetup
{
    private const string DocumentUrl = "/openapi/v1.json";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<ApiInfoTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerRequirementTransformer>();
        });

    /// <summary>Serves the OpenAPI document and Swagger UI at /swagger.</summary>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi(DocumentUrl).AllowAnonymous();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(DocumentUrl, "Hotel Management API v1");
            options.DocumentTitle = "Hotel Management API";
            options.EnablePersistAuthorization(); // keep the token across page reloads
            options.DisplayRequestDuration();
        });
        return app;
    }
}

internal sealed class ApiInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Hotel Management API",
            Version = "v1",
            Description = "Multi-tenant hotel management. Call `POST /api/auth/login` (or `/register`), " +
                          "then click **Authorize** and paste the `accessToken`.",
        };
        return Task.CompletedTask;
    }
}

/// <summary>Declares the JWT bearer scheme, which gives Swagger UI its Authorize button.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the accessToken from /api/auth/login (without the 'Bearer ' prefix).",
        };
        return Task.CompletedTask;
    }
}

/// <summary>Marks every endpoint that needs a token (all except [AllowAnonymous] ones) as requiring the bearer scheme.</summary>
internal sealed class BearerRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName, context.Document)] = [],
        });
        return Task.CompletedTask;
    }
}
