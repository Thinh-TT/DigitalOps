using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class OpenApiTests(OpenApiApiFactory factory)
    : IClassFixture<OpenApiApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    [Fact]
    public async Task Development_exposes_swagger_ui_and_openapi_document()
    {
        var swaggerResponse = await _client.GetAsync("/swagger/index.html");
        var openApiResponse = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
        Assert.Contains(
            "DigitalOps API",
            await swaggerResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(
            "application/json",
            openApiResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Openapi_describes_bearer_security_dtos_enums_and_problem_details()
    {
        using var document = JsonDocument.Parse(
            await _client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;

        Assert.Equal("DigitalOps API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var protectedOperation = root
            .GetProperty("paths")
            .GetProperty("/_test/authorization/business")
            .GetProperty("get");
        Assert.Equal(
            1,
            protectedOperation.GetProperty("security").GetArrayLength());
        Assert.True(
            protectedOperation
                .GetProperty("security")[0]
                .TryGetProperty("Bearer", out _));

        var anonymousOperation = root
            .GetProperty("paths")
            .GetProperty("/_test/errors/validation")
            .GetProperty("post");
        Assert.False(anonymousOperation.TryGetProperty("security", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var requestSchema = schemas.GetProperty(nameof(ErrorProbeRequest));
        Assert.True(requestSchema.GetProperty("properties").TryGetProperty("displayName", out _));
        Assert.True(requestSchema.GetProperty("properties").TryGetProperty("status", out var status));
        Assert.Contains("Active", ResolveEnumValues(status, schemas));
        Assert.Contains("Inactive", ResolveEnumValues(status, schemas));

        var responses = anonymousOperation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("400", out var validationResponse));
        Assert.Contains(
            nameof(ValidationProblemDetails),
            validationResponse.GetRawText());

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out var loginPath));
        Assert.False(loginPath.GetProperty("post").TryGetProperty("security", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/me", out var mePath));
        Assert.True(mePath.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/auth/change-password",
                out var changePasswordPath));
        Assert.True(
            changePasswordPath.GetProperty("post").TryGetProperty("security", out _));

        Assert.True(schemas.TryGetProperty("LoginRequest", out _));
        Assert.True(schemas.TryGetProperty("LoginResponse", out _));
        Assert.True(schemas.TryGetProperty("CurrentUserResponse", out _));
        Assert.True(schemas.TryGetProperty("ChangePasswordRequest", out _));

        Assert.True(paths.TryGetProperty("/api/v1/staff", out var staffPath));
        Assert.True(staffPath.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(staffPath.GetProperty("post").TryGetProperty("security", out _));
        var staffQueryParameters = staffPath
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("activeOnly", staffQueryParameters);
        Assert.Contains("page", staffQueryParameters);
        Assert.Contains("pageSize", staffQueryParameters);
        Assert.True(
            paths.TryGetProperty("/api/v1/staff/{id}", out var staffDetailPath));
        Assert.True(staffDetailPath.TryGetProperty("get", out _));
        Assert.True(staffDetailPath.TryGetProperty("patch", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/staff/{id}/roles",
                out var staffRolesPath));
        Assert.True(staffRolesPath.TryGetProperty("put", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/staff/{id}/reset-password",
                out var resetPasswordPath));
        Assert.True(
            resetPasswordPath
                .GetProperty("post")
                .GetProperty("responses")
                .TryGetProperty("204", out _));

        Assert.True(schemas.TryGetProperty("StaffCreateRequest", out _));
        Assert.True(schemas.TryGetProperty("StaffUpdateRequest", out _));
        Assert.True(schemas.TryGetProperty("RoleAssignmentRequest", out _));
        Assert.True(schemas.TryGetProperty("ResetPasswordRequest", out _));
        Assert.True(schemas.TryGetProperty("StaffResponse", out _));

        Assert.True(paths.TryGetProperty("/api/v1/members", out var membersPath));
        Assert.True(membersPath.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(membersPath.GetProperty("post").TryGetProperty("security", out _));
        var memberQueryParameters = membersPath
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("q", memberQueryParameters);
        Assert.Contains("status", memberQueryParameters);
        Assert.Contains("page", memberQueryParameters);
        Assert.Contains("pageSize", memberQueryParameters);
        Assert.True(
            paths.TryGetProperty("/api/v1/members/lookup", out var memberLookupPath));
        Assert.True(memberLookupPath.TryGetProperty("get", out _));
        Assert.True(
            paths.TryGetProperty("/api/v1/members/{id}", out var memberDetailPath));
        Assert.True(memberDetailPath.TryGetProperty("get", out _));
        Assert.True(memberDetailPath.TryGetProperty("patch", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/members/{id}/deactivate",
                out var memberDeactivatePath));
        Assert.True(
            memberDeactivatePath
                .GetProperty("post")
                .GetProperty("responses")
                .TryGetProperty("409", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/members/import-template",
                out var memberImportTemplatePath));
        Assert.True(
            memberImportTemplatePath
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .TryGetProperty(
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/members/import",
                out var memberImportPath));
        var memberImportOperation = memberImportPath.GetProperty("post");
        Assert.True(
            memberImportOperation
                .GetProperty("requestBody")
                .GetProperty("content")
                .TryGetProperty("multipart/form-data", out _));
        var memberImportResponses = memberImportOperation.GetProperty("responses");
        foreach (var statusCode in new[] { "200", "400", "413", "415", "422" })
        {
            Assert.True(memberImportResponses.TryGetProperty(statusCode, out _));
        }
        Assert.True(schemas.TryGetProperty("MemberUpsertRequest", out _));
        Assert.True(schemas.TryGetProperty("MemberResponse", out _));
        Assert.True(schemas.TryGetProperty("MemberLookupResponse", out _));
        Assert.True(schemas.TryGetProperty("MemberImportResult", out _));
        Assert.True(schemas.TryGetProperty("MemberImportRowError", out _));
        Assert.True(schemas.TryGetProperty("MemberImportProblemDetails", out _));

        Assert.True(paths.TryGetProperty("/api/v1/document-types", out var documentTypesPath));
        Assert.True(documentTypesPath.TryGetProperty("get", out var documentTypesGet));
        Assert.True(documentTypesPath.TryGetProperty("post", out _));
        var documentTypeQueryParameters = documentTypesGet
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("activeOnly", documentTypeQueryParameters);
        Assert.Contains("page", documentTypeQueryParameters);
        Assert.Contains("pageSize", documentTypeQueryParameters);
        Assert.True(paths.TryGetProperty(
            "/api/v1/document-types/{id}",
            out var documentTypeDetailPath));
        Assert.True(documentTypeDetailPath.TryGetProperty("get", out _));
        Assert.True(documentTypeDetailPath.TryGetProperty("patch", out _));

        Assert.True(paths.TryGetProperty(
            "/api/v1/document-templates",
            out var documentTemplatesPath));
        Assert.True(documentTemplatesPath.TryGetProperty("get", out var documentTemplatesGet));
        Assert.True(documentTemplatesPath.TryGetProperty("post", out var documentTemplatesPost));
        var documentTemplateQueryParameters = documentTemplatesGet
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("documentTypeId", documentTemplateQueryParameters);
        Assert.Contains("activeOnly", documentTemplateQueryParameters);
        Assert.True(documentTemplatesPost
            .GetProperty("responses")
            .TryGetProperty("422", out _));
        Assert.True(paths.TryGetProperty(
            "/api/v1/document-templates/{id}",
            out var documentTemplateDetailPath));
        Assert.True(documentTemplateDetailPath.TryGetProperty("get", out _));
        Assert.True(documentTemplateDetailPath.TryGetProperty("patch", out var templatePatch));
        Assert.True(templatePatch.GetProperty("responses").TryGetProperty("422", out _));

        Assert.True(schemas.TryGetProperty("DocumentTypeRequest", out _));
        Assert.True(schemas.TryGetProperty("DocumentTypeResponse", out _));
        Assert.True(schemas.TryGetProperty("DocumentTemplateRequest", out var templateRequest));
        Assert.Contains("formatRules", templateRequest.GetRawText());
        Assert.True(schemas.TryGetProperty("DocumentTemplateResponse", out _));
        Assert.True(schemas.TryGetProperty("DocumentTypeReference", out _));

        Assert.True(paths.TryGetProperty(
            "/api/v1/incoming-documents",
            out var incomingDocumentsPath));
        Assert.True(incomingDocumentsPath.TryGetProperty("get", out var incomingDocumentsGet));
        Assert.True(incomingDocumentsPath.TryGetProperty("post", out var incomingDocumentsPost));
        var incomingQueryParameters = incomingDocumentsGet
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        foreach (var parameter in new[]
        {
            "q",
            "documentTypeId",
            "status",
            "assignedToStaffId",
            "deadlineFrom",
            "deadlineTo",
            "page",
            "pageSize"
        })
        {
            Assert.Contains(parameter, incomingQueryParameters);
        }
        Assert.True(incomingDocumentsPost.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(incomingDocumentsPost.GetProperty("responses").TryGetProperty("400", out _));

        Assert.True(paths.TryGetProperty(
            "/api/v1/incoming-documents/{id}",
            out var incomingDocumentDetailPath));
        Assert.True(incomingDocumentDetailPath.TryGetProperty("get", out var incomingDetailGet));
        Assert.True(incomingDocumentDetailPath.TryGetProperty("patch", out var incomingDetailPatch));
        Assert.True(incomingDetailGet.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(incomingDetailPatch.GetProperty("responses").TryGetProperty("409", out _));

        Assert.True(paths.TryGetProperty(
            "/api/v1/incoming-documents/{id}/complete",
            out var incomingCompletePath));
        var incomingCompleteResponses = incomingCompletePath
            .GetProperty("post")
            .GetProperty("responses");
        foreach (var statusCode in new[] { "200", "401", "403", "404", "409" })
        {
            Assert.True(incomingCompleteResponses.TryGetProperty(statusCode, out _));
        }

        Assert.True(paths.TryGetProperty(
            "/api/v1/incoming-documents/{id}/assignment-suggestion",
            out var assignmentSuggestionPath));
        var assignmentSuggestionResponses = assignmentSuggestionPath
            .GetProperty("post")
            .GetProperty("responses");
        foreach (var statusCode in new[] { "200", "401", "403", "404", "409", "503" })
        {
            Assert.True(assignmentSuggestionResponses.TryGetProperty(statusCode, out _));
        }

        Assert.True(paths.TryGetProperty(
            "/api/v1/incoming-documents/{id}/assignment",
            out var assignmentPath));
        var assignmentOperation = assignmentPath.GetProperty("post");
        foreach (var statusCode in new[] { "200", "400", "401", "403", "404", "409" })
        {
            Assert.True(assignmentOperation
                .GetProperty("responses")
                .TryGetProperty(statusCode, out _));
        }

        Assert.True(schemas.TryGetProperty("IncomingDocumentCreateRequest", out _));
        Assert.True(schemas.TryGetProperty("IncomingDocumentUpdateRequest", out _));
        Assert.True(schemas.TryGetProperty("AssignmentSuggestionResponse", out _));
        Assert.True(schemas.TryGetProperty("AssignmentConfirmRequest", out _));
        Assert.True(schemas.TryGetProperty("IncomingDocumentResponse", out var incomingResponse));
        Assert.Contains("attachments", incomingResponse.GetRawText());
        Assert.True(schemas.TryGetProperty("IncomingStaffReference", out _));
        Assert.True(schemas.TryGetProperty("AttachmentResponse", out var attachmentResponse));
        Assert.DoesNotContain("fileUrl", attachmentResponse.GetRawText());
        Assert.DoesNotContain("storageKey", attachmentResponse.GetRawText());
        Assert.DoesNotContain("extractedText", attachmentResponse.GetRawText());
        Assert.DoesNotContain("extractionError", attachmentResponse.GetRawText());
        Assert.True(schemas.TryGetProperty("ExtractionStatus", out var extractionStatus));
        Assert.Equal(
            new[] { "Pending", "Processing", "Succeeded", "Failed", "Unsupported" },
            ResolveEnumValues(extractionStatus, schemas));
        Assert.True(schemas.TryGetProperty("IncomingDocumentStatus", out var incomingStatus));
        Assert.Equal(
            new[] { "New", "InProgress", "Completed", "Overdue" },
            ResolveEnumValues(incomingStatus, schemas));

        Assert.True(paths.TryGetProperty(
            "/api/v1/incoming-documents/{incomingDocumentId}/attachments",
            out var incomingAttachmentPath));
        var uploadOperation = incomingAttachmentPath.GetProperty("post");
        Assert.True(uploadOperation
            .GetProperty("requestBody")
            .GetProperty("content")
            .TryGetProperty("multipart/form-data", out var multipart));
        Assert.Contains("file", multipart.GetRawText());
        foreach (var statusCode in new[]
        {
            "201", "400", "401", "403", "404", "409", "413", "415", "500"
        })
        {
            Assert.True(uploadOperation
                .GetProperty("responses")
                .TryGetProperty(statusCode, out _));
        }

        Assert.True(paths.TryGetProperty(
            "/api/v1/attachments/{id}/download",
            out var attachmentDownloadPath));
        Assert.True(attachmentDownloadPath.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty(
            "/api/v1/attachments/{id}",
            out var attachmentDeletePath));
        Assert.True(attachmentDeletePath.TryGetProperty("delete", out var attachmentDelete));
        Assert.True(attachmentDelete
            .GetProperty("responses")
            .TryGetProperty("204", out _));

        Assert.True(paths.TryGetProperty("/api/v1/outgoing-documents", out var outgoingPath));
        var outgoingGet = outgoingPath.GetProperty("get");
        var outgoingQueryParameters = outgoingGet.GetProperty("parameters")
            .EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()).ToArray();
        foreach (var parameter in new[] { "q", "templateId", "relatedIncomingDocumentId", "relatedMemberId", "status", "draftedByStaffId", "dateFrom", "dateTo", "page", "pageSize" })
        {
            Assert.Contains(parameter, outgoingQueryParameters);
        }
        Assert.True(outgoingPath.GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.TryGetProperty("/api/v1/outgoing-documents/{id}", out var outgoingDetailPath));
        Assert.True(outgoingDetailPath.GetProperty("get").GetProperty("responses").TryGetProperty("404", out _));
        var outgoingPatchResponses = outgoingDetailPath.GetProperty("patch").GetProperty("responses");
        foreach (var statusCode in new[] { "200", "400", "401", "403", "404", "409" })
        {
            Assert.True(outgoingPatchResponses.TryGetProperty(statusCode, out _));
        }

        Assert.True(paths.TryGetProperty("/api/v1/outgoing-documents/{id}/ai-draft", out var outgoingAiDraftPath));
        var outgoingAiDraftResponses = outgoingAiDraftPath.GetProperty("post").GetProperty("responses");
        foreach (var statusCode in new[] { "200", "400", "401", "403", "404", "409", "503" })
        {
            Assert.True(outgoingAiDraftResponses.TryGetProperty(statusCode, out _));
        }

        Assert.True(paths.TryGetProperty(
            "/api/v1/outgoing-documents/{outgoingDocumentId}/reviews",
            out var outgoingReviewsPath));
        var outgoingReviewPostResponses = outgoingReviewsPath.GetProperty("post").GetProperty("responses");
        foreach (var statusCode in new[] { "200", "401", "403", "404", "409", "503" })
        {
            Assert.True(outgoingReviewPostResponses.TryGetProperty(statusCode, out _));
        }
        var outgoingReviewGet = outgoingReviewsPath.GetProperty("get");
        var outgoingReviewParameters = outgoingReviewGet.GetProperty("parameters")
            .EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()).ToArray();
        Assert.Contains("page", outgoingReviewParameters);
        Assert.Contains("pageSize", outgoingReviewParameters);

        Assert.True(paths.TryGetProperty(
            "/api/v1/outgoing-documents/{outgoingDocumentId}/approval",
            out var outgoingApprovalPath));
        var outgoingApprovalResponses = outgoingApprovalPath.GetProperty("post").GetProperty("responses");
        foreach (var statusCode in new[] { "200", "400", "401", "403", "404", "409" })
        {
            Assert.True(outgoingApprovalResponses.TryGetProperty(statusCode, out _));
        }

        Assert.True(paths.TryGetProperty("/api/v1/outgoing-documents/{outgoingDocumentId}/attachments", out var outgoingAttachmentPath));
        Assert.True(outgoingAttachmentPath.GetProperty("post").GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        Assert.True(schemas.TryGetProperty("OutgoingDocumentCreateRequest", out _));
        Assert.True(schemas.TryGetProperty("OutgoingDocumentUpdateRequest", out _));
        Assert.True(schemas.TryGetProperty("AiDraftRequest", out _));
        Assert.True(schemas.TryGetProperty("OutgoingDocumentResponse", out _));
        Assert.True(schemas.TryGetProperty("ReviewResponse", out _));
        Assert.True(schemas.TryGetProperty("ApprovalDecisionRequest", out _));
        Assert.True(schemas.TryGetProperty("ApprovalDecision", out var approvalDecision));
        Assert.Equal(new[] { "Approve", "Return" }, ResolveEnumValues(approvalDecision, schemas));
        Assert.True(schemas.TryGetProperty("ReviewSource", out var reviewSource));
        Assert.Equal(new[] { "Rule", "AI", "Hybrid" }, ResolveEnumValues(reviewSource, schemas));
        Assert.True(schemas.TryGetProperty("ReviewResult", out var reviewResult));
        Assert.Equal(new[] { "Failed", "Passed" }, ResolveEnumValues(reviewResult, schemas));
        Assert.True(schemas.TryGetProperty("OutgoingDocumentStatus", out var outgoingStatus));
        Assert.Equal(new[] { "Editing", "AiDraft", "PendingReview", "ReviewFailed", "PendingApproval", "Approved", "Archived" }, ResolveEnumValues(outgoingStatus, schemas));
    }

    private static IReadOnlyCollection<string> ResolveEnumValues(
        JsonElement schema,
        JsonElement schemas)
    {
        if (schema.TryGetProperty("enum", out var inlineValues))
        {
            return inlineValues
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
        }

        if (!schema.TryGetProperty("$ref", out var referenceElement))
        {
            throw new InvalidOperationException(
                $"Enum schema was not emitted as an enum or reference: {schema.GetRawText()}");
        }

        var reference = referenceElement.GetString()!;
        var schemaName = reference[(reference.LastIndexOf('/') + 1)..];
        var referencedSchema = schemas.GetProperty(schemaName);
        if (!referencedSchema.TryGetProperty("enum", out var referencedValues))
        {
            throw new InvalidOperationException(
                $"Referenced enum schema did not include values: {referencedSchema.GetRawText()}");
        }

        return referencedValues
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
    }
}

public sealed class OpenApiApiFactory : DigitalOpsApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => services
            .AddControllers()
            .AddApplicationPart(typeof(ErrorProbeController).Assembly));
    }
}
