using DigitalOps.API.Shared.Errors;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AttachmentRequestSizeLimitAttribute
    : Attribute, IFilterFactory, IOrderedFilter
{
    public bool IsReusable => false;

    public int Order => int.MinValue + 100;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<AttachmentRequestSizeLimitFilter>(
            serviceProvider);
}

internal sealed class AttachmentRequestSizeLimitFilter(
    IOptions<AttachmentStorageOptions> options,
    ProblemDetailsFactory problemDetailsFactory) : IAsyncResourceFilter
{
    private const long MultipartOverheadBytes = 64 * 1024;

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var maximumRequestBytes = checked(
            options.Value.MaxFileSizeBytes + MultipartOverheadBytes);
        var request = context.HttpContext.Request;
        if (request.ContentLength > maximumRequestBytes)
        {
            var problem = problemDetailsFactory.CreateProblemDetails(
                context.HttpContext,
                StatusCodes.Status413PayloadTooLarge,
                detail: "File tải lên vượt quá dung lượng cho phép.");
            ProblemDetailsDefaults.Apply(context.HttpContext, problem);
            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge,
                ContentTypes = { "application/problem+json" }
            };
            return;
        }

        var requestSizeFeature = context.HttpContext.Features
            .Get<IHttpMaxRequestBodySizeFeature>();
        if (requestSizeFeature is { IsReadOnly: false })
        {
            requestSizeFeature.MaxRequestBodySize = maximumRequestBytes;
        }

        await next();
    }
}
