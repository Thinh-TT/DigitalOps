using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.OutgoingDocuments;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Search;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentKind
{
    Incoming = 1,
    Outgoing = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentSearchMatchSource
{
    Summary = 1,
    Title = 2,
    Content = 3,
    AiDraftContent = 4,
    Attachment = 5
}

public sealed class DocumentSearchQuery : IValidatableObject
{
    [FromQuery(Name = "q")]
    [Required(ErrorMessage = "Từ khóa tìm kiếm là bắt buộc.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Từ khóa tìm kiếm phải có tối thiểu 2 ký tự.")]
    public string Q { get; init; } = string.Empty;

    [FromQuery(Name = "documentKind")]
    public DocumentKind? DocumentKind { get; init; }

    [FromQuery(Name = "documentTypeId")]
    public Guid? DocumentTypeId { get; init; }

    [FromQuery(Name = "incomingStatus")]
    public IncomingDocumentStatus? IncomingStatus { get; init; }

    [FromQuery(Name = "outgoingStatus")]
    public OutgoingDocumentStatus? OutgoingStatus { get; init; }

    [FromQuery(Name = "dateFrom")]
    public DateOnly? DateFrom { get; init; }

    [FromQuery(Name = "dateTo")]
    public DateOnly? DateTo { get; init; }

    [FromQuery(Name = "matchSource")]
    public DocumentSearchMatchSource? MatchSource { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải lớn hơn hoặc bằng 1.")]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100, ErrorMessage = "Kích thước trang phải từ 1 đến 100.")]
    public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Q) || Q.Trim().Length < 2)
        {
            yield return new ValidationResult(
                "Từ khóa tìm kiếm phải có tối thiểu 2 ký tự.",
                [nameof(Q)]);
        }

        if (DateFrom is not null
            && DateTo is not null
            && DateFrom > DateTo)
        {
            yield return new ValidationResult(
                "Ngày bắt đầu không được sau ngày kết thúc.",
                [nameof(DateFrom), nameof(DateTo)]);
        }
    }
}

public sealed record DocumentSearchResult(
    DocumentKind DocumentKind,
    Guid DocumentId,
    string ReferenceNumber,
    string Title,
    string DocumentType,
    DateOnly DocumentDate,
    DocumentSearchMatchSource MatchSource,
    string Snippet,
    double Score);
