using System.IO.Compression;

namespace DigitalOps.API.Features.Attachments;

internal static class AttachmentFileValidator
{
    private static readonly HashSet<char> InvalidFileNameCharacters =
        Path.GetInvalidFileNameChars().ToHashSet();

    public static async Task<AttachmentFileValidationResult> ValidateAsync(
        Stream content,
        string fileName,
        long declaredLength,
        long maxFileSizeBytes,
        CancellationToken cancellationToken)
    {
        if (declaredLength <= 0)
        {
            return AttachmentFileValidationResult.Validation(
                "file",
                "Vui lòng chọn file có dữ liệu.");
        }

        if (declaredLength > maxFileSizeBytes)
        {
            return AttachmentFileValidationResult.PayloadTooLarge(
                $"File không được vượt quá {FormatMiB(maxFileSizeBytes)} MiB.");
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            return AttachmentFileValidationResult.Validation(
                "file",
                "Tên file không hợp lệ.");
        }

        if (sanitizedFileName.Length > 255)
        {
            return AttachmentFileValidationResult.Validation(
                "file",
                "Tên file không được vượt quá 255 ký tự.");
        }

        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
        if (!TryGetFileType(extension, out var fileType))
        {
            return AttachmentFileValidationResult.Unsupported(
                "Chỉ chấp nhận file PDF, DOCX, XLSX, JPG, JPEG hoặc PNG.");
        }

        if (!content.CanSeek)
        {
            return AttachmentFileValidationResult.Validation(
                "file",
                "Không thể kiểm tra nội dung file tải lên.");
        }

        if (content.Length > maxFileSizeBytes)
        {
            return AttachmentFileValidationResult.PayloadTooLarge(
                $"File không được vượt quá {FormatMiB(maxFileSizeBytes)} MiB.");
        }

        var validContent = await HasExpectedContentAsync(
            content,
            fileType,
            cancellationToken);
        content.Position = 0;

        return validContent
            ? AttachmentFileValidationResult.Success(
                sanitizedFileName,
                extension,
                fileType.ContentType,
                fileType.ExtractionStatus)
            : AttachmentFileValidationResult.Unsupported(
                "Nội dung file không khớp với định dạng được hỗ trợ.");
    }

    public static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return TryGetFileType(extension, out var fileType)
            ? fileType.ContentType
            : "application/octet-stream";
    }

    private static string SanitizeFileName(string value)
    {
        var normalized = value.Replace('\\', '/');
        var leafName = normalized[(normalized.LastIndexOf('/') + 1)..];
        var sanitized = new string(
            leafName
                .Where(character =>
                    !char.IsControl(character)
                    && character != '/'
                    && character != '\\'
                    && !InvalidFileNameCharacters.Contains(character))
                .ToArray());
        return sanitized.Trim();
    }

    private static async Task<bool> HasExpectedContentAsync(
        Stream content,
        SupportedFileType fileType,
        CancellationToken cancellationToken)
    {
        try
        {
            content.Position = 0;
            if (fileType.PackageEntry is not null)
            {
                using var archive = new ZipArchive(
                    content,
                    ZipArchiveMode.Read,
                    leaveOpen: true);
                return archive.GetEntry("[Content_Types].xml") is not null
                    && archive.GetEntry(fileType.PackageEntry) is not null;
            }

            var prefix = new byte[fileType.Signature!.Length];
            var totalRead = 0;
            while (totalRead < prefix.Length)
            {
                var read = await content.ReadAsync(
                    prefix.AsMemory(totalRead, prefix.Length - totalRead),
                    cancellationToken);
                if (read == 0)
                {
                    return false;
                }

                totalRead += read;
            }

            return prefix.AsSpan().SequenceEqual(fileType.Signature);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            if (content.CanSeek)
            {
                content.Position = 0;
            }
        }
    }

    private static bool TryGetFileType(
        string extension,
        out SupportedFileType fileType)
    {
        fileType = extension switch
        {
            ".pdf" => new(
                "application/pdf",
                ExtractionStatus.Pending,
                "%PDF-"u8.ToArray(),
                null),
            ".docx" => new(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ExtractionStatus.Pending,
                null,
                "word/document.xml"),
            ".xlsx" => new(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExtractionStatus.Pending,
                null,
                "xl/workbook.xml"),
            ".jpg" or ".jpeg" => new(
                "image/jpeg",
                ExtractionStatus.Unsupported,
                [0xFF, 0xD8, 0xFF],
                null),
            ".png" => new(
                "image/png",
                ExtractionStatus.Unsupported,
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
                null),
            _ => default
        };
        return fileType.ContentType is not null;
    }

    private static long FormatMiB(long bytes) =>
        Math.Max(1, bytes / (1024 * 1024));

    private readonly record struct SupportedFileType(
        string ContentType,
        ExtractionStatus ExtractionStatus,
        byte[]? Signature,
        string? PackageEntry);
}

internal sealed record AttachmentFileValidationResult(
    bool Succeeded,
    AttachmentFailure Failure,
    string? Detail,
    IReadOnlyDictionary<string, string[]> Errors,
    string? FileName,
    string? Extension,
    string? ContentType,
    ExtractionStatus ExtractionStatus)
{
    public static AttachmentFileValidationResult Success(
        string fileName,
        string extension,
        string contentType,
        ExtractionStatus extractionStatus) =>
        new(
            true,
            AttachmentFailure.None,
            null,
            new Dictionary<string, string[]>(),
            fileName,
            extension,
            contentType,
            extractionStatus);

    public static AttachmentFileValidationResult Validation(
        string field,
        string detail) =>
        new(
            false,
            AttachmentFailure.Validation,
            detail,
            new Dictionary<string, string[]> { [field] = [detail] },
            null,
            null,
            null,
            default);

    public static AttachmentFileValidationResult PayloadTooLarge(string detail) =>
        new(
            false,
            AttachmentFailure.PayloadTooLarge,
            detail,
            new Dictionary<string, string[]>(),
            null,
            null,
            null,
            default);

    public static AttachmentFileValidationResult Unsupported(string detail) =>
        new(
            false,
            AttachmentFailure.UnsupportedFileType,
            detail,
            new Dictionary<string, string[]>(),
            null,
            null,
            null,
            default);
}
