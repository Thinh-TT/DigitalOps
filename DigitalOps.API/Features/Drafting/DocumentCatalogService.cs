using System.Text.Json;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentCatalogService(
    DigitalOpsDbContext dbContext) : IDocumentCatalogService
{
    public async Task<PagedResponse<DocumentTypeResponse>> GetDocumentTypesAsync(
        DocumentTypeListQuery query,
        CancellationToken cancellationToken = default)
    {
        var documentTypes = dbContext.DocumentTypes.AsNoTracking();

        if (query.ActiveOnly == true)
        {
            documentTypes = documentTypes.Where(documentType => documentType.IsActive);
        }

        var totalCount = await documentTypes.CountAsync(cancellationToken);
        var items = await documentTypes
            .OrderBy(documentType => documentType.Code)
            .ThenBy(documentType => documentType.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(documentType => ToResponse(documentType))
            .ToArrayAsync(cancellationToken);

        return CreatePage(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<DocumentTypeResponse?> GetDocumentTypeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var documentType = await dbContext.DocumentTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return documentType is null ? null : ToResponse(documentType);
    }

    public async Task<DocumentCatalogResult<DocumentTypeResponse>> CreateDocumentTypeAsync(
        DocumentTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateDocumentTypeRequest(request, creating: true);
        if (errors.Count > 0)
        {
            return DocumentCatalogResult<DocumentTypeResponse>.Validation(errors);
        }

        var code = request.Code!.Trim();
        if (await dbContext.DocumentTypes.AnyAsync(
                documentType => documentType.Code == code,
                cancellationToken))
        {
            return DocumentCatalogResult<DocumentTypeResponse>.Conflict(
                "Mã loại văn bản đã tồn tại.");
        }

        var documentType = new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name!.Trim(),
            Description = NormalizeOptional(request.Description),
            IsActive = request.HasIsActive ? request.IsActive!.Value : true
        };
        dbContext.DocumentTypes.Add(documentType);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DocumentCatalogResult<DocumentTypeResponse>.Conflict(
                "Mã loại văn bản đã tồn tại.");
        }

        return DocumentCatalogResult<DocumentTypeResponse>.Success(ToResponse(documentType));
    }

    public async Task<DocumentCatalogResult<DocumentTypeResponse>> UpdateDocumentTypeAsync(
        Guid id,
        DocumentTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var documentType = await dbContext.DocumentTypes
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (documentType is null)
        {
            return DocumentCatalogResult<DocumentTypeResponse>.NotFound();
        }

        var errors = ValidateDocumentTypeRequest(request, creating: false);
        if (errors.Count > 0)
        {
            return DocumentCatalogResult<DocumentTypeResponse>.Validation(errors);
        }

        var code = request.HasCode ? request.Code!.Trim() : documentType.Code;
        if (code != documentType.Code
            && await dbContext.DocumentTypes.AnyAsync(
                item => item.Id != id && item.Code == code,
                cancellationToken))
        {
            return DocumentCatalogResult<DocumentTypeResponse>.Conflict(
                "Mã loại văn bản đã tồn tại.");
        }

        if (request.HasCode)
        {
            documentType.Code = code;
        }

        if (request.HasName)
        {
            documentType.Name = request.Name!.Trim();
        }

        if (request.HasDescription)
        {
            documentType.Description = NormalizeOptional(request.Description);
        }

        if (request.HasIsActive)
        {
            documentType.IsActive = request.IsActive!.Value;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DocumentCatalogResult<DocumentTypeResponse>.Conflict(
                "Mã loại văn bản đã tồn tại.");
        }

        return DocumentCatalogResult<DocumentTypeResponse>.Success(ToResponse(documentType));
    }

    public async Task<PagedResponse<DocumentTemplateResponse>> GetDocumentTemplatesAsync(
        DocumentTemplateListQuery query,
        CancellationToken cancellationToken = default)
    {
        var templates = dbContext.DocumentTemplates
            .AsNoTracking()
            .Include(template => template.DocumentType)
            .AsQueryable();

        if (query.DocumentTypeId is not null)
        {
            templates = templates.Where(
                template => template.DocumentTypeId == query.DocumentTypeId);
        }

        if (query.ActiveOnly == true)
        {
            templates = templates.Where(
                template => template.IsActive && template.DocumentType.IsActive);
        }

        var totalCount = await templates.CountAsync(cancellationToken);
        var entities = await templates
            .OrderBy(template => template.DocumentType.Code)
            .ThenBy(template => template.Name)
            .ThenBy(template => template.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        var items = entities.Select(ToResponse).ToArray();

        return CreatePage(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<DocumentTemplateResponse?> GetDocumentTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await dbContext.DocumentTemplates
            .AsNoTracking()
            .Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return template is null ? null : ToResponse(template);
    }

    public async Task<DocumentCatalogResult<DocumentTemplateResponse>> CreateDocumentTemplateAsync(
        DocumentTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateDocumentTemplateRequest(request, creating: true);
        if (errors.Count > 0)
        {
            return ToTemplateValidationResult(errors);
        }

        var documentTypeId = request.DocumentTypeId!.Value;
        var parentResult = await GetActiveDocumentTypeAsync(
            documentTypeId,
            cancellationToken);
        if (parentResult.Error is not null)
        {
            return DocumentCatalogResult<DocumentTemplateResponse>.Validation(
                SingleError("documentTypeId", parentResult.Error));
        }

        var name = request.Name!.Trim();
        if (await dbContext.DocumentTemplates.AnyAsync(
                template => template.DocumentTypeId == documentTypeId
                    && template.Name == name,
                cancellationToken))
        {
            return DocumentCatalogResult<DocumentTemplateResponse>.Conflict(
                "Tên mẫu văn bản đã tồn tại trong loại đã chọn.");
        }

        var template = new DocumentTemplate
        {
            Id = Guid.NewGuid(),
            DocumentTypeId = documentTypeId,
            DocumentType = parentResult.DocumentType!,
            Name = name,
            TemplateContent = request.TemplateContent!.Trim(),
            FormatRules = request.FormatRules!.Value.Clone(),
            IsActive = request.HasIsActive ? request.IsActive!.Value : true
        };
        dbContext.DocumentTemplates.Add(template);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DocumentCatalogResult<DocumentTemplateResponse>.Conflict(
                "Tên mẫu văn bản đã tồn tại trong loại đã chọn.");
        }

        return DocumentCatalogResult<DocumentTemplateResponse>.Success(ToResponse(template));
    }

    public async Task<DocumentCatalogResult<DocumentTemplateResponse>> UpdateDocumentTemplateAsync(
        Guid id,
        DocumentTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await dbContext.DocumentTemplates
            .Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return DocumentCatalogResult<DocumentTemplateResponse>.NotFound();
        }

        var errors = ValidateDocumentTemplateRequest(request, creating: false);
        if (errors.Count > 0)
        {
            return ToTemplateValidationResult(errors);
        }

        var documentTypeId = request.HasDocumentTypeId
            ? request.DocumentTypeId!.Value
            : template.DocumentTypeId;
        var name = request.HasName ? request.Name!.Trim() : template.Name;
        var nextIsActive = request.HasIsActive
            ? request.IsActive!.Value
            : template.IsActive;
        var documentTypeChanged = documentTypeId != template.DocumentTypeId;
        var templateActivated = !template.IsActive && nextIsActive;

        DocumentType? nextDocumentType = null;
        if (documentTypeChanged || templateActivated)
        {
            var parentResult = await GetActiveDocumentTypeAsync(
                documentTypeId,
                cancellationToken);
            if (parentResult.Error is not null)
            {
                return DocumentCatalogResult<DocumentTemplateResponse>.Validation(
                    SingleError("documentTypeId", parentResult.Error));
            }

            nextDocumentType = parentResult.DocumentType;
        }

        if ((documentTypeChanged || name != template.Name)
            && await dbContext.DocumentTemplates.AnyAsync(
                item => item.Id != id
                    && item.DocumentTypeId == documentTypeId
                    && item.Name == name,
                cancellationToken))
        {
            return DocumentCatalogResult<DocumentTemplateResponse>.Conflict(
                "Tên mẫu văn bản đã tồn tại trong loại đã chọn.");
        }

        if (documentTypeChanged)
        {
            template.DocumentTypeId = documentTypeId;
            template.DocumentType = nextDocumentType!;
        }

        if (request.HasName)
        {
            template.Name = name;
        }

        if (request.HasTemplateContent)
        {
            template.TemplateContent = request.TemplateContent!.Trim();
        }

        if (request.HasFormatRules)
        {
            template.FormatRules = request.FormatRules!.Value.Clone();
        }

        if (request.HasIsActive)
        {
            template.IsActive = nextIsActive;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DocumentCatalogResult<DocumentTemplateResponse>.Conflict(
                "Tên mẫu văn bản đã tồn tại trong loại đã chọn.");
        }

        return DocumentCatalogResult<DocumentTemplateResponse>.Success(ToResponse(template));
    }

    private async Task<(DocumentType? DocumentType, string? Error)> GetActiveDocumentTypeAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var documentType = await dbContext.DocumentTypes
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (documentType is null)
        {
            return (null, "Không tìm thấy loại văn bản.");
        }

        return documentType.IsActive
            ? (documentType, null)
            : (null, "Loại văn bản đã ngừng hoạt động.");
    }

    private static Dictionary<string, string[]> ValidateDocumentTypeRequest(
        DocumentTypeRequest request,
        bool creating)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateRequiredString(
            errors,
            "code",
            request.HasCode,
            request.Code,
            creating,
            "Vui lòng nhập mã loại văn bản.");
        ValidateRequiredString(
            errors,
            "name",
            request.HasName,
            request.Name,
            creating,
            "Vui lòng nhập tên loại văn bản.");

        if (request.HasIsActive && request.IsActive is null)
        {
            errors["isActive"] = ["Trạng thái hoạt động không được để trống."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateDocumentTemplateRequest(
        DocumentTemplateRequest request,
        bool creating)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if ((creating || request.HasDocumentTypeId)
            && (request.DocumentTypeId is null
                || request.DocumentTypeId.Value == Guid.Empty))
        {
            errors["documentTypeId"] = ["Vui lòng chọn loại văn bản."];
        }

        ValidateRequiredString(
            errors,
            "name",
            request.HasName,
            request.Name,
            creating,
            "Vui lòng nhập tên mẫu văn bản.");
        ValidateRequiredString(
            errors,
            "templateContent",
            request.HasTemplateContent,
            request.TemplateContent,
            creating,
            "Vui lòng nhập nội dung mẫu văn bản.");

        if (creating || request.HasFormatRules)
        {
            var formatErrors = ValidateFormatRules(request.FormatRules);
            if (formatErrors.Length > 0)
            {
                errors["formatRules"] = formatErrors;
            }
        }

        if (request.HasIsActive && request.IsActive is null)
        {
            errors["isActive"] = ["Trạng thái hoạt động không được để trống."];
        }

        return errors;
    }

    private static string[] ValidateFormatRules(JsonElement? formatRules)
    {
        var errors = new List<string>();
        if (formatRules is null || formatRules.Value.ValueKind == JsonValueKind.Null)
        {
            return ["Vui lòng nhập FormatRules."];
        }

        var root = formatRules.Value;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ["FormatRules phải là một JSON object."];
        }

        if (!root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var versionNumber)
            || versionNumber <= 0)
        {
            errors.Add("FormatRules.version phải là số nguyên dương.");
        }

        if (!root.TryGetProperty("rules", out var rules)
            || rules.ValueKind != JsonValueKind.Array)
        {
            errors.Add("FormatRules.rules phải là một mảng.");
            return errors.ToArray();
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"FormatRules.rules[{index}] phải là một JSON object.");
                index++;
                continue;
            }

            string? code = null;
            if (!rule.TryGetProperty("code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(codeElement.GetString()))
            {
                errors.Add($"FormatRules.rules[{index}].code phải là chuỗi không rỗng.");
            }
            else
            {
                code = codeElement.GetString()!.Trim();
                if (!codes.Add(code))
                {
                    errors.Add($"FormatRules.rules[{index}].code bị trùng: {code}.");
                }
            }

            if (!rule.TryGetProperty("required", out var required)
                || required.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                errors.Add($"FormatRules.rules[{index}].required phải là boolean.");
            }

            index++;
        }

        return errors.ToArray();
    }

    private static void ValidateRequiredString(
        IDictionary<string, string[]> errors,
        string field,
        bool wasProvided,
        string? value,
        bool creating,
        string message)
    {
        if ((creating && !wasProvided)
            || (wasProvided && string.IsNullOrWhiteSpace(value)))
        {
            errors[field] = [message];
        }
    }

    private static DocumentCatalogResult<DocumentTemplateResponse> ToTemplateValidationResult(
        IReadOnlyDictionary<string, string[]> errors) =>
        errors.ContainsKey("formatRules")
            ? DocumentCatalogResult<DocumentTemplateResponse>.FormatRulesValidation(errors)
            : DocumentCatalogResult<DocumentTemplateResponse>.Validation(errors);

    private static IReadOnlyDictionary<string, string[]> SingleError(
        string field,
        string error) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [error]
        };

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };

    private static PagedResponse<T> CreatePage<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0
                ? 0
                : (int)Math.Ceiling((double)totalCount / pageSize));

    private static DocumentTypeResponse ToResponse(DocumentType documentType) =>
        new(
            documentType.Id,
            documentType.Code,
            documentType.Name,
            documentType.Description,
            documentType.IsActive,
            documentType.CreatedAt,
            documentType.UpdatedAt);

    private static DocumentTemplateResponse ToResponse(DocumentTemplate template) =>
        new(
            template.Id,
            new DocumentTypeReference(
                template.DocumentType.Id,
                template.DocumentType.Code,
                template.DocumentType.Name),
            template.Name,
            template.TemplateContent,
            template.FormatRules.Clone(),
            template.IsActive,
            template.CreatedAt,
            template.UpdatedAt);
}
