using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace DigitalOps.API.Features.Review;

public sealed record ReviewCitationResponse(
    Guid ChunkId,
    Guid DocumentId,
    Guid VersionId,
    string Title,
    string? DocumentNumber,
    string? DocumentType,
    string? Issuer,
    string SourceUrl,
    string SourceTrustTier,
    string SourceVersion,
    string LegalStatus,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsEffectivityUnknown,
    double Score);

public sealed class ReviewListQuery
{
    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record ReviewResponse(
    Guid Id,
    Guid OutgoingDocumentId,
    int AttemptNo,
    ReviewSource ReviewSource,
    OutgoingStaffReference? ReviewedByStaff,
    string ContentSnapshot,
    ReviewResult ReviewResult,
    IReadOnlyList<ReviewIssueResponse> ReviewIssues,
    IReadOnlyList<ReviewCitationResponse> Citations,
    DateTime ReviewedAt,
    OutgoingDocumentStatus DocumentStatus);

public enum ReviewOperationFailure
{
    None,
    NotFound,
    Forbidden,
    Conflict,
    ServiceUnavailable
}

public sealed record ReviewOperationResult<T>(
    T? Value,
    ReviewOperationFailure Failure,
    string? Detail)
{
    public bool Succeeded => Failure == ReviewOperationFailure.None;

    public static ReviewOperationResult<T> Success(T value) =>
        new(value, ReviewOperationFailure.None, null);

    public static ReviewOperationResult<T> NotFound() =>
        new(default, ReviewOperationFailure.NotFound, null);

    public static ReviewOperationResult<T> Forbidden(string detail) =>
        new(default, ReviewOperationFailure.Forbidden, detail);

    public static ReviewOperationResult<T> Conflict(string detail) =>
        new(default, ReviewOperationFailure.Conflict, detail);

    public static ReviewOperationResult<T> ServiceUnavailable(string detail) =>
        new(default, ReviewOperationFailure.ServiceUnavailable, detail);
}

public interface IOutgoingDocumentReviewService
{
    Task<ReviewOperationResult<ReviewResponse>> CreateAsync(
        Guid outgoingDocumentId,
        Guid callerStaffId,
        CancellationToken cancellationToken = default);

    Task<ReviewOperationResult<PagedResponse<ReviewResponse>>> GetListAsync(
        Guid outgoingDocumentId,
        ReviewListQuery query,
        CancellationToken cancellationToken = default);
}
