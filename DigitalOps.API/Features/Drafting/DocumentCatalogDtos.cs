using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentTypeRequest
{
    private string? _code;
    private string? _name;
    private string? _description;
    private bool? _isActive;

    [StringLength(50)]
    public string? Code
    {
        get => _code;
        set
        {
            _code = value;
            HasCode = true;
        }
    }

    [StringLength(150)]
    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            HasName = true;
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            _description = value;
            HasDescription = true;
        }
    }

    public bool? IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            HasIsActive = true;
        }
    }

    [JsonIgnore]
    public bool HasCode { get; private set; }

    [JsonIgnore]
    public bool HasName { get; private set; }

    [JsonIgnore]
    public bool HasDescription { get; private set; }

    [JsonIgnore]
    public bool HasIsActive { get; private set; }
}

public sealed class DocumentTemplateRequest
{
    private Guid? _documentTypeId;
    private string? _name;
    private string? _templateContent;
    private JsonElement? _formatRules;
    private bool? _isActive;

    public Guid? DocumentTypeId
    {
        get => _documentTypeId;
        set
        {
            _documentTypeId = value;
            HasDocumentTypeId = true;
        }
    }

    [StringLength(200)]
    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            HasName = true;
        }
    }

    public string? TemplateContent
    {
        get => _templateContent;
        set
        {
            _templateContent = value;
            HasTemplateContent = true;
        }
    }

    public JsonElement? FormatRules
    {
        get => _formatRules;
        set
        {
            _formatRules = value;
            HasFormatRules = true;
        }
    }

    public bool? IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            HasIsActive = true;
        }
    }

    [JsonIgnore]
    public bool HasDocumentTypeId { get; private set; }

    [JsonIgnore]
    public bool HasName { get; private set; }

    [JsonIgnore]
    public bool HasTemplateContent { get; private set; }

    [JsonIgnore]
    public bool HasFormatRules { get; private set; }

    [JsonIgnore]
    public bool HasIsActive { get; private set; }
}

public sealed record DocumentTypeReference(
    Guid Id,
    string Code,
    string Name);

public sealed record DocumentTypeResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record DocumentTemplateResponse(
    Guid Id,
    DocumentTypeReference DocumentType,
    string Name,
    string TemplateContent,
    JsonElement FormatRules,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class DocumentTypeListQuery
{
    [FromQuery(Name = "activeOnly")]
    public bool? ActiveOnly { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class DocumentTemplateListQuery
{
    [FromQuery(Name = "documentTypeId")]
    public Guid? DocumentTypeId { get; init; }

    [FromQuery(Name = "activeOnly")]
    public bool? ActiveOnly { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
