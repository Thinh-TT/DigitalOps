using System.IO.Compression;
using System.Text;

namespace DigitalOps.API.Features.Attachments;

internal static class TextExtractionGuard
{
    public static string? ValidateZipPackage(Stream stream, TextExtractionWorkerOptions options)
    {
        if (!stream.CanSeek)
        {
            return "Không thể đọc gói tệp theo cách an toàn.";
        }

        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length < 0 || entry.Length > options.MaxPackageExpandedBytes - expandedBytes)
                {
                    return "Tệp nén vượt giới hạn kích thước an toàn.";
                }

                expandedBytes += entry.Length;
            }

            return null;
        }
        catch (InvalidDataException)
        {
            return "Gói tệp không hợp lệ.";
        }
        finally
        {
            stream.Position = 0;
        }
    }

    public static bool TryAppend(StringBuilder builder, string value, int maxChars)
    {
        if (value.Length > maxChars - builder.Length)
        {
            return false;
        }

        builder.Append(value);
        return true;
    }

    public static bool TryAppendLine(StringBuilder builder, string value, int maxChars)
    {
        if (value.Length + Environment.NewLine.Length > maxChars - builder.Length)
        {
            return false;
        }

        builder.AppendLine(value);
        return true;
    }
}
