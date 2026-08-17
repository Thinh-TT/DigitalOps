using System.Text;
using ClosedXML.Excel;

namespace DigitalOps.API.Features.Attachments;

public sealed class XlsxTextExtractor : ITextExtractor
{
    private readonly TextExtractionWorkerOptions _options;

    public XlsxTextExtractor(TextExtractionWorkerOptions? options = null)
    {
        _options = options ?? new TextExtractionWorkerOptions();
    }

    public bool CanExtract(string extension) =>
        string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase);

    public Task<TextExtractionResult> ExtractAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            stream.Position = 0;
            var packageError = TextExtractionGuard.ValidateZipPackage(stream, _options);
            if (packageError is not null)
            {
                return Task.FromResult(TextExtractionResult.Failed(packageError));
            }

            using var workbook = new XLWorkbook(stream);
            var builder = new StringBuilder();
            var cellCount = 0;

            foreach (var worksheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (worksheet.Visibility != XLWorksheetVisibility.Visible)
                {
                    continue;
                }

                if (!TextExtractionGuard.TryAppendLine(builder, $"[Trang: {worksheet.Name}]", _options.MaxExtractedTextChars))
                {
                    return Task.FromResult(
                        TextExtractionResult.Failed("XLSX vượt giới hạn dung lượng văn bản được phép trích xuất."));
                }
                var usedRange = worksheet.RangeUsed();
                if (usedRange is null)
                {
                    continue;
                }

                foreach (var row in usedRange.Rows())
                {
                    var rowValues = new List<string>();
                    foreach (var cell in row.Cells())
                    {
                        cellCount++;
                        if (cellCount > _options.MaxXlsxCells)
                        {
                            return Task.FromResult(
                                TextExtractionResult.Failed("XLSX vượt giới hạn số ô được phép trích xuất."));
                        }

                        var cellValue = cell.GetFormattedString().Trim();
                        if (!string.IsNullOrEmpty(cellValue))
                        {
                            rowValues.Add(cellValue);
                        }
                    }

                    if (rowValues.Count > 0)
                    {
                        if (!TextExtractionGuard.TryAppendLine(builder, string.Join("\t", rowValues), _options.MaxExtractedTextChars))
                        {
                            return Task.FromResult(
                                TextExtractionResult.Failed("XLSX vượt giới hạn dung lượng văn bản được phép trích xuất."));
                        }
                    }
                }

                if (!TextExtractionGuard.TryAppendLine(builder, string.Empty, _options.MaxExtractedTextChars))
                {
                    return Task.FromResult(
                        TextExtractionResult.Failed("XLSX vượt giới hạn dung lượng văn bản được phép trích xuất."));
                }
            }

            var resultText = builder.ToString().Trim();
            return Task.FromResult(string.IsNullOrWhiteSpace(resultText)
                ? TextExtractionResult.Unsupported()
                : TextExtractionResult.Succeeded(resultText));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult(
                TextExtractionResult.Failed("Lỗi trích xuất XLSX."));
        }
    }
}
