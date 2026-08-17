using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DigitalOps.API.Shared.OpenApi;

public sealed class BearerSecuritySchemeTransformer :
    IOpenApiDocumentTransformer,
    IOpenApiOperationTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "DigitalOps API",
            Description = "Digital operations API for document and member management.",
            Version = "v1"
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter a JWT access token."
        };

        if (document.Components.Schemas is not null)
        {
            foreach (var (_, schema) in document.Components.Schemas)
            {
                if (schema is OpenApiSchema openApiSchema && openApiSchema.Enum is not null)
                {
                    for (var i = openApiSchema.Enum.Count - 1; i >= 0; i--)
                    {
                        var enumItem = openApiSchema.Enum[i];
                        if (enumItem is null || enumItem.ToString() == "null")
                        {
                            openApiSchema.Enum.RemoveAt(i);
                        }
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();

        if (!allowsAnonymous && requiresAuthorization)
        {
            operation.Security ??= [];
            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(
                        SchemeName,
                        context.Document)] = []
                });
        }

        return Task.CompletedTask;
    }
}
