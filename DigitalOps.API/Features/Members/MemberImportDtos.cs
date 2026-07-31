using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Members;

public sealed class MemberImportForm
{
    [Required]
    public IFormFile? File { get; init; }
}

public sealed record MemberImportRowError(
    int RowNumber,
    string Field,
    string Message);

public sealed record MemberImportResult(
    int ImportedCount,
    int TotalRows,
    IReadOnlyList<MemberImportRowError> Errors);

public sealed class MemberImportProblemDetails : ProblemDetails
{
    public int ImportedCount { get; init; }

    public int TotalRows { get; init; }

    public IReadOnlyList<MemberImportRowError> Errors { get; init; } = [];
}
