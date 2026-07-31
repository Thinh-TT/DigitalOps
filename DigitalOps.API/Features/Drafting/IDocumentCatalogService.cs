using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.Drafting;

public interface IDocumentCatalogService
{
    Task<PagedResponse<DocumentTypeResponse>> GetDocumentTypesAsync(
        DocumentTypeListQuery query,
        CancellationToken cancellationToken = default);

    Task<DocumentTypeResponse?> GetDocumentTypeAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<DocumentCatalogResult<DocumentTypeResponse>> CreateDocumentTypeAsync(
        DocumentTypeRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentCatalogResult<DocumentTypeResponse>> UpdateDocumentTypeAsync(
        Guid id,
        DocumentTypeRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<DocumentTemplateResponse>> GetDocumentTemplatesAsync(
        DocumentTemplateListQuery query,
        CancellationToken cancellationToken = default);

    Task<DocumentTemplateResponse?> GetDocumentTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<DocumentCatalogResult<DocumentTemplateResponse>> CreateDocumentTemplateAsync(
        DocumentTemplateRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentCatalogResult<DocumentTemplateResponse>> UpdateDocumentTemplateAsync(
        Guid id,
        DocumentTemplateRequest request,
        CancellationToken cancellationToken = default);
}

public enum DocumentCatalogFailure
{
    None,
    NotFound,
    Validation,
    FormatRulesValidation,
    Conflict
}

public sealed record DocumentCatalogResult<T>(
    T? Value,
    DocumentCatalogFailure Failure,
    IReadOnlyDictionary<string, string[]> Errors,
    string? Detail)
{
    public bool Succeeded => Failure == DocumentCatalogFailure.None;

    public static DocumentCatalogResult<T> Success(T value) =>
        new(value, DocumentCatalogFailure.None, EmptyErrors, null);

    public static DocumentCatalogResult<T> NotFound() =>
        new(default, DocumentCatalogFailure.NotFound, EmptyErrors, null);

    public static DocumentCatalogResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, DocumentCatalogFailure.Validation, errors, null);

    public static DocumentCatalogResult<T> FormatRulesValidation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, DocumentCatalogFailure.FormatRulesValidation, errors, null);

    public static DocumentCatalogResult<T> Conflict(string detail) =>
        new(default, DocumentCatalogFailure.Conflict, EmptyErrors, detail);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}
