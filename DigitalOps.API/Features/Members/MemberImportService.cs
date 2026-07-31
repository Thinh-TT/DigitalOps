using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Members;

public sealed class MemberImportService(
    DigitalOpsDbContext dbContext,
    IOptions<MemberImportOptions> optionsAccessor) : IMemberImportService
{
    public const string TemplateFileName =
        "DigitalOps-Member-Import-Template.xlsx";
    public const string SpreadsheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string DataSheetName = "Hội viên";
    private const string InstructionsSheetName = "Hướng dẫn";
    private const string CatalogSheetName = "Danh mục";

    private static readonly string[] Headers =
    [
        "Họ và tên",
        "Ngày sinh",
        "Giới tính",
        "Địa chỉ",
        "Số điện thoại",
        "Email",
        "Chức vụ",
        "Ngày gia nhập",
        "Trạng thái",
        "Ghi chú"
    ];

    private static readonly IReadOnlyDictionary<string, int> FieldOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["file"] = 0,
            ["fullName"] = 1,
            ["dateOfBirth"] = 2,
            ["gender"] = 3,
            ["address"] = 4,
            ["phone"] = 5,
            ["email"] = 6,
            ["position"] = 7,
            ["joinDate"] = 8,
            ["status"] = 9,
            ["notes"] = 10,
            ["duplicateKey"] = 11
        };

    private readonly MemberImportOptions _options = optionsAccessor.Value;

    public byte[] CreateTemplate()
    {
        MemberWorkbookGraphics.Configure();
        using var workbook = new XLWorkbook();
        var data = workbook.Worksheets.Add(DataSheetName);
        var instructions = workbook.Worksheets.Add(InstructionsSheetName);
        var catalog = workbook.Worksheets.Add(CatalogSheetName);

        ConfigureDataSheet(data);
        ConfigureInstructionsSheet(instructions);
        ConfigureCatalogSheet(catalog);

        data.SetTabActive();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<MemberImportServiceResult> ImportAsync(
        Stream stream,
        string fileName,
        long fileLength,
        CancellationToken cancellationToken = default)
    {
        MemberWorkbookGraphics.Configure();
        if (fileLength > _options.MaxFileSizeBytes)
        {
            return MemberImportServiceResult.PayloadTooLarge(
                $"File vượt giới hạn {_options.MaxFileSizeBytes} byte.");
        }

        if (!string.Equals(
                Path.GetExtension(fileName),
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return MemberImportServiceResult.UnsupportedFileType(
                "Chỉ chấp nhận file có phần mở rộng .xlsx.");
        }

        var bufferedFile = await BufferFileAsync(stream, cancellationToken);
        if (bufferedFile is null)
        {
            return MemberImportServiceResult.PayloadTooLarge(
                $"File vượt giới hạn {_options.MaxFileSizeBytes} byte.");
        }

        using (bufferedFile)
        {
            var archiveValidation = ValidateArchive(bufferedFile);
            if (archiveValidation == ArchiveValidation.TooLarge)
            {
                return MemberImportServiceResult.PayloadTooLarge(
                    "Nội dung giải nén của workbook vượt giới hạn cho phép.");
            }

            if (archiveValidation == ArchiveValidation.Invalid)
            {
                return MemberImportServiceResult.UnsupportedFileType(
                    "File không phải workbook XLSX hợp lệ.");
            }

            ParsedWorkbook parsedWorkbook;
            try
            {
                bufferedFile.Position = 0;
                using var workbook = new XLWorkbook(bufferedFile);
                parsedWorkbook = ParseWorkbook(workbook);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                and not OutOfMemoryException)
            {
                return MemberImportServiceResult.UnsupportedFileType(
                    "Không thể đọc workbook XLSX.");
            }

            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(cancellationToken);
            try
            {
                await AddDatabaseDuplicateErrorsAsync(
                    parsedWorkbook,
                    cancellationToken);

                if (parsedWorkbook.Errors.Count > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return MemberImportServiceResult.Validation(
                        parsedWorkbook.TotalRows,
                        SortErrors(parsedWorkbook.Errors));
                }

                var members = parsedWorkbook.Rows
                    .Select(row => row.Member)
                    .ToArray();
                dbContext.Members.AddRange(members);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return MemberImportServiceResult.Success(
                    new MemberImportResult(
                        members.Length,
                        parsedWorkbook.TotalRows,
                        []));
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private void ConfigureDataSheet(IXLWorksheet worksheet)
    {
        for (var index = 0; index < Headers.Length; index++)
        {
            worksheet.Cell(1, index + 1).Value = Headers[index];
        }

        var header = worksheet.Range(1, 1, 1, Headers.Length);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        header.Style.Border.BottomBorderColor = XLColor.FromHtml("#17365D");
        worksheet.Row(1).Height = 26;
        worksheet.SheetView.FreezeRows(1);
        worksheet.ShowGridLines = false;

        var filterEndRow = Math.Max(2, _options.MaxRows + 1);
        worksheet.Range(1, 1, filterEndRow, Headers.Length).SetAutoFilter();

        worksheet.Column(1).Width = 28;
        worksheet.Column(2).Width = 14;
        worksheet.Column(3).Width = 14;
        worksheet.Column(4).Width = 32;
        worksheet.Column(5).Width = 18;
        worksheet.Column(6).Width = 28;
        worksheet.Column(7).Width = 24;
        worksheet.Column(8).Width = 16;
        worksheet.Column(9).Width = 16;
        worksheet.Column(10).Width = 36;

        var lastDataRow = _options.MaxRows + 1;
        worksheet.Range(2, 2, lastDataRow, 2)
            .Style.NumberFormat.Format = "yyyy-mm-dd";
        worksheet.Range(2, 8, lastDataRow, 8)
            .Style.NumberFormat.Format = "yyyy-mm-dd";
        worksheet.Range(2, 5, lastDataRow, 5)
            .Style.NumberFormat.Format = "@";

        var genderValidation = worksheet
            .Range(2, 3, lastDataRow, 3)
            .CreateDataValidation();
        genderValidation.List($"'{CatalogSheetName}'!$A$2:$A$4", true);
        genderValidation.IgnoreBlanks = true;
        genderValidation.ShowErrorMessage = true;
        genderValidation.ErrorTitle = "Giới tính không hợp lệ";
        genderValidation.ErrorMessage = "Chọn Male, Female hoặc Other.";

        var statusValidation = worksheet
            .Range(2, 9, lastDataRow, 9)
            .CreateDataValidation();
        statusValidation.List($"'{CatalogSheetName}'!$B$2:$B$3", true);
        statusValidation.IgnoreBlanks = true;
        statusValidation.ShowErrorMessage = true;
        statusValidation.ErrorTitle = "Trạng thái không hợp lệ";
        statusValidation.ErrorMessage = "Chọn Active hoặc Inactive; để trống sẽ dùng Active.";
    }

    private static void ConfigureInstructionsSheet(IXLWorksheet worksheet)
    {
        worksheet.Range("A1:F1").Merge();
        worksheet.Cell("A1").Value = "HƯỚNG DẪN IMPORT HỘI VIÊN";
        worksheet.Cell("A1").Style.Fill.BackgroundColor =
            XLColor.FromHtml("#D9EAF7");
        worksheet.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#17365D");
        worksheet.Cell("A1").Style.Font.Bold = true;
        worksheet.Cell("A1").Style.Font.FontSize = 16;
        worksheet.Cell("A1").Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;
        worksheet.Row(1).Height = 30;

        var instructions = new[]
        {
            "Nhập dữ liệu tại sheet 'Hội viên'; không đổi tên, thứ tự hoặc xóa cột.",
            "Họ và tên là bắt buộc. Các ô chuỗi trắng được xem là không có dữ liệu.",
            "Ngày sinh và Ngày gia nhập dùng ngày Excel hoặc chuỗi yyyy-mm-dd.",
            "Số điện thoại phải nhập dạng Text để giữ số 0 ở đầu.",
            "Giới tính: Male, Female, Other. Trạng thái: Active, Inactive; để trống là Active.",
            "Toàn bộ file được kiểm tra trước khi lưu; một lỗi khiến không có dòng nào được import.",
            "Khóa trùng: Họ và tên + Ngày sinh + Số điện thoại sau chuẩn hóa."
        };

        worksheet.Cell("A3").Value = "Quy tắc";
        worksheet.Cell("A3").Style.Font.Bold = true;
        worksheet.Cell("A3").Style.Font.FontColor = XLColor.FromHtml("#1F4E78");
        for (var index = 0; index < instructions.Length; index++)
        {
            var row = index + 4;
            worksheet.Range(row, 1, row, 6).Merge();
            worksheet.Cell(row, 1).Value = $"{index + 1}. {instructions[index]}";
        }

        var ruleRange = worksheet.Range(4, 1, instructions.Length + 3, 6);
        ruleRange.Style.Alignment.WrapText = true;
        ruleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.Column(1).Width = 88;
        for (var row = 4; row <= instructions.Length + 3; row++)
        {
            worksheet.Row(row).Height = 28;
        }

        worksheet.ShowGridLines = false;
    }

    private static void ConfigureCatalogSheet(IXLWorksheet worksheet)
    {
        worksheet.Cell("A1").Value = "Gender";
        worksheet.Cell("A2").Value = "Male";
        worksheet.Cell("A3").Value = "Female";
        worksheet.Cell("A4").Value = "Other";
        worksheet.Cell("B1").Value = "Status";
        worksheet.Cell("B2").Value = "Active";
        worksheet.Cell("B3").Value = "Inactive";
        worksheet.Visibility = XLWorksheetVisibility.VeryHidden;
    }

    private async Task<MemoryStream?> BufferFileAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                destination.Position = 0;
                return destination;
            }

            if (destination.Length + read > _options.MaxFileSizeBytes)
            {
                await destination.DisposeAsync();
                return null;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private ArchiveValidation ValidateArchive(Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            long totalExpandedSize = 0;
            var hasContentTypes = false;
            var hasWorkbook = false;

            foreach (var entry in archive.Entries)
            {
                totalExpandedSize = checked(totalExpandedSize + entry.Length);
                if (totalExpandedSize > _options.MaxExpandedWorkbookBytes)
                {
                    return ArchiveValidation.TooLarge;
                }

                hasContentTypes |= string.Equals(
                    entry.FullName,
                    "[Content_Types].xml",
                    StringComparison.OrdinalIgnoreCase);
                hasWorkbook |= string.Equals(
                    entry.FullName,
                    "xl/workbook.xml",
                    StringComparison.OrdinalIgnoreCase);
            }

            return hasContentTypes && hasWorkbook
                ? ArchiveValidation.Valid
                : ArchiveValidation.Invalid;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or OverflowException)
        {
            return ArchiveValidation.Invalid;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
        }
    }

    private ParsedWorkbook ParseWorkbook(XLWorkbook workbook)
    {
        var parsed = new ParsedWorkbook();
        if (!workbook.TryGetWorksheet(DataSheetName, out var worksheet))
        {
            parsed.Errors.Add(new MemberImportRowError(
                0,
                "file",
                $"Không tìm thấy sheet '{DataSheetName}'."));
            return parsed;
        }

        for (var column = 1; column <= Headers.Length; column++)
        {
            var actual = worksheet.Cell(1, column).GetString().Trim();
            if (!string.Equals(actual, Headers[column - 1], StringComparison.Ordinal))
            {
                parsed.Errors.Add(new MemberImportRowError(
                    1,
                    FieldOrder.Keys.FirstOrDefault(
                        field => FieldOrder[field] == column) ?? "file",
                    $"Cột {column} phải có tiêu đề '{Headers[column - 1]}'."));
            }
        }

        if ((worksheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0)
            > Headers.Length)
        {
            parsed.Errors.Add(new MemberImportRowError(
                1,
                "file",
                $"Sheet '{DataSheetName}' chỉ được có {Headers.Length} cột theo template."));
        }

        var lastRow = worksheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (Enumerable.Range(1, Headers.Length)
                .All(column => row.Cell(column).IsEmpty()))
            {
                continue;
            }

            parsed.TotalRows++;
            if (parsed.TotalRows > _options.MaxRows)
            {
                parsed.Errors.Add(new MemberImportRowError(
                    0,
                    "file",
                    $"Workbook vượt giới hạn {_options.MaxRows} dòng dữ liệu."));
                break;
            }

            ParseRow(row, parsed);
        }

        if (parsed.TotalRows == 0)
        {
            parsed.Errors.Add(new MemberImportRowError(
                0,
                "file",
                "Workbook không có dòng dữ liệu."));
        }

        AddInFileDuplicateErrors(parsed);
        return parsed;
    }

    private static void ParseRow(IXLRow row, ParsedWorkbook parsed)
    {
        var rowNumber = row.RowNumber();
        var rowErrors = new List<MemberImportRowError>();
        var invalidFields = new HashSet<string>(StringComparer.Ordinal);

        var fullName = ReadText(row.Cell(1), rowNumber, "fullName", rowErrors, invalidFields);
        var dateOfBirth = ReadDate(row.Cell(2), rowNumber, "dateOfBirth", rowErrors, invalidFields);
        var gender = ReadText(row.Cell(3), rowNumber, "gender", rowErrors, invalidFields);
        var address = ReadText(row.Cell(4), rowNumber, "address", rowErrors, invalidFields);
        var phone = ReadText(row.Cell(5), rowNumber, "phone", rowErrors, invalidFields);
        var email = ReadText(row.Cell(6), rowNumber, "email", rowErrors, invalidFields);
        var position = ReadText(row.Cell(7), rowNumber, "position", rowErrors, invalidFields);
        var joinDate = ReadDate(row.Cell(8), rowNumber, "joinDate", rowErrors, invalidFields);
        var statusText = ReadText(row.Cell(9), rowNumber, "status", rowErrors, invalidFields);
        var notes = ReadText(row.Cell(10), rowNumber, "notes", rowErrors, invalidFields);

        var validationErrors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!invalidFields.Contains("fullName"))
        {
            MemberProfileRules.ValidateFullName(fullName, validationErrors);
        }

        if (!invalidFields.Contains("gender"))
        {
            MemberProfileRules.ValidateGender(gender, validationErrors);
        }

        if (!invalidFields.Contains("phone"))
        {
            MemberProfileRules.ValidatePhone(phone, validationErrors);
        }

        if (!invalidFields.Contains("email"))
        {
            MemberProfileRules.ValidateEmail(email, validationErrors);
        }

        if (!invalidFields.Contains("position"))
        {
            MemberProfileRules.ValidatePosition(position, validationErrors);
        }

        var status = MemberStatus.Active;
        var normalizedStatus = MemberProfileRules.NormalizeOptional(statusText);
        if (!invalidFields.Contains("status") && normalizedStatus is not null)
        {
            if (string.Equals(
                    normalizedStatus,
                    nameof(MemberStatus.Active),
                    StringComparison.OrdinalIgnoreCase))
            {
                status = MemberStatus.Active;
            }
            else if (string.Equals(
                         normalizedStatus,
                         nameof(MemberStatus.Inactive),
                         StringComparison.OrdinalIgnoreCase))
            {
                status = MemberStatus.Inactive;
            }
            else
            {
                MemberProfileRules.AddError(
                    validationErrors,
                    "status",
                    "Trạng thái phải là Active hoặc Inactive.");
            }
        }

        foreach (var (field, messages) in validationErrors)
        {
            rowErrors.AddRange(messages.Select(message =>
                new MemberImportRowError(rowNumber, field, message)));
        }

        var normalizedFullName = string.IsNullOrWhiteSpace(fullName)
            ? string.Empty
            : MemberProfileRules.NormalizeFullName(fullName);
        var member = new Member
        {
            Id = Guid.NewGuid(),
            FullName = normalizedFullName,
            DateOfBirth = dateOfBirth,
            Gender = MemberProfileRules.NormalizeOptional(gender),
            Address = MemberProfileRules.NormalizeOptional(address),
            Phone = MemberProfileRules.NormalizePhone(phone),
            Email = MemberProfileRules.NormalizeEmail(email),
            Position = MemberProfileRules.NormalizeOptional(position),
            JoinDate = joinDate,
            Status = status,
            Notes = MemberProfileRules.NormalizeOptional(notes)
        };

        parsed.Errors.AddRange(rowErrors);
        var duplicateKeyFieldsValid =
            !invalidFields.Overlaps(["fullName", "dateOfBirth", "phone"])
            && !validationErrors.ContainsKey("fullName")
            && !validationErrors.ContainsKey("phone")
            && !string.IsNullOrWhiteSpace(normalizedFullName);
        parsed.Rows.Add(new ParsedMemberRow(
            rowNumber,
            member,
            duplicateKeyFieldsValid));
    }

    private static string? ReadText(
        IXLCell cell,
        int rowNumber,
        string field,
        ICollection<MemberImportRowError> errors,
        ISet<string> invalidFields)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.HasFormula)
        {
            AddCellError("Không chấp nhận công thức.");
            return null;
        }

        if (cell.DataType != XLDataType.Text)
        {
            AddCellError("Giá trị phải được nhập dạng văn bản.");
            return null;
        }

        return cell.GetString();

        void AddCellError(string message)
        {
            invalidFields.Add(field);
            errors.Add(new MemberImportRowError(rowNumber, field, message));
        }
    }

    private static DateOnly? ReadDate(
        IXLCell cell,
        int rowNumber,
        string field,
        ICollection<MemberImportRowError> errors,
        ISet<string> invalidFields)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.HasFormula)
        {
            AddCellError("Không chấp nhận công thức.");
            return null;
        }

        if (cell.DataType == XLDataType.DateTime
            && cell.TryGetValue<DateTime>(out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        if (cell.DataType == XLDataType.Text
            && DateOnly.TryParseExact(
                cell.GetString().Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        AddCellError("Ngày phải là ngày Excel hoặc chuỗi YYYY-MM-DD.");
        return null;

        void AddCellError(string message)
        {
            invalidFields.Add(field);
            errors.Add(new MemberImportRowError(rowNumber, field, message));
        }
    }

    private static void AddInFileDuplicateErrors(ParsedWorkbook parsed)
    {
        var firstRows = new Dictionary<MemberDuplicateKey, int>(
            MemberDuplicateKeyComparer.Instance);
        foreach (var row in parsed.Rows.Where(row => row.HasValidDuplicateKey))
        {
            var key = MemberDuplicateKey.FromMember(row.Member);
            if (firstRows.TryGetValue(key, out var firstRow))
            {
                parsed.Errors.Add(new MemberImportRowError(
                    row.RowNumber,
                    "duplicateKey",
                    $"Trùng Họ và tên + Ngày sinh + Số điện thoại với dòng {firstRow}."));
            }
            else
            {
                firstRows[key] = row.RowNumber;
            }
        }
    }

    private async Task AddDatabaseDuplicateErrorsAsync(
        ParsedWorkbook parsed,
        CancellationToken cancellationToken)
    {
        var existingValues = await dbContext.Members
            .AsNoTracking()
            .Select(member => new
            {
                member.FullName,
                member.DateOfBirth,
                member.Phone
            })
            .ToArrayAsync(cancellationToken);
        var existingKeys = existingValues
            .Select(member => new MemberDuplicateKey(
                MemberProfileRules.NormalizeFullName(member.FullName),
                member.DateOfBirth,
                MemberProfileRules.NormalizePhone(member.Phone)))
            .ToHashSet(MemberDuplicateKeyComparer.Instance);

        foreach (var row in parsed.Rows.Where(row => row.HasValidDuplicateKey))
        {
            if (existingKeys.Contains(MemberDuplicateKey.FromMember(row.Member)))
            {
                parsed.Errors.Add(new MemberImportRowError(
                    row.RowNumber,
                    "duplicateKey",
                    "Trùng Họ và tên + Ngày sinh + Số điện thoại với hội viên trong hệ thống."));
            }
        }
    }

    private static IReadOnlyList<MemberImportRowError> SortErrors(
        IEnumerable<MemberImportRowError> errors) =>
        errors
            .OrderBy(error => error.RowNumber)
            .ThenBy(error => FieldOrder.GetValueOrDefault(error.Field, int.MaxValue))
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToArray();

    private enum ArchiveValidation
    {
        Valid,
        Invalid,
        TooLarge
    }

    private sealed class ParsedWorkbook
    {
        public int TotalRows { get; set; }

        public List<ParsedMemberRow> Rows { get; } = [];

        public List<MemberImportRowError> Errors { get; } = [];
    }

    private sealed record ParsedMemberRow(
        int RowNumber,
        Member Member,
        bool HasValidDuplicateKey);

    private sealed record MemberDuplicateKey(
        string FullName,
        DateOnly? DateOfBirth,
        string? Phone)
    {
        public static MemberDuplicateKey FromMember(Member member) =>
            new(member.FullName, member.DateOfBirth, member.Phone);
    }

    private sealed class MemberDuplicateKeyComparer
        : IEqualityComparer<MemberDuplicateKey>
    {
        public static MemberDuplicateKeyComparer Instance { get; } = new();

        public bool Equals(MemberDuplicateKey? x, MemberDuplicateKey? y) =>
            ReferenceEquals(x, y)
            || x is not null
            && y is not null
            && StringComparer.OrdinalIgnoreCase.Equals(x.FullName, y.FullName)
            && x.DateOfBirth == y.DateOfBirth
            && StringComparer.Ordinal.Equals(x.Phone, y.Phone);

        public int GetHashCode(MemberDuplicateKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.FullName, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.DateOfBirth);
            hash.Add(obj.Phone, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
