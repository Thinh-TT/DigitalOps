using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Features.StaffManagement;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class MemberImportServiceTests
{
    [Fact]
    public void Template_contains_guidance_headers_formats_and_validations()
    {
        using var factory = new StaffManagementApiFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemberImportService>();

        using var stream = new MemoryStream(service.CreateTemplate());
        using var workbook = new XLWorkbook(stream);

        Assert.True(workbook.TryGetWorksheet("Hội viên", out var data));
        Assert.True(workbook.TryGetWorksheet("Hướng dẫn", out _));
        Assert.Equal(
            XLWorksheetVisibility.VeryHidden,
            workbook.Worksheet("Danh mục").Visibility);
        Assert.Equal(ExpectedHeaders, ReadHeaders(data));
        Assert.True(data.SheetView.SplitRow > 0);
        Assert.True(data.AutoFilter.IsEnabled);
        Assert.Equal("yyyy-mm-dd", data.Cell("B2").Style.NumberFormat.Format);
        Assert.Equal("yyyy-mm-dd", data.Cell("H2").Style.NumberFormat.Format);
        Assert.Equal("@", data.Cell("E2").Style.NumberFormat.Format);
        Assert.Equal(
            XLAllowedValues.List,
            data.Cell("C2").GetDataValidation().AllowedValues);
        Assert.Equal(
            XLAllowedValues.List,
            data.Cell("I2").GetDataValidation().AllowedValues);
    }

    [Fact]
    public async Task Valid_workbook_normalizes_values_and_imports_all_rows()
    {
        using var factory = new StaffManagementApiFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemberImportService>();
        var bytes = CreateWorkbook(sheet =>
        {
            WriteMemberRow(
                sheet,
                2,
                "  Nguyễn   Văn An  ",
                new DateTime(1990, 5, 12),
                "Male",
                "0901  000  001",
                "AN@EXAMPLE.COM",
                "2020-01-02",
                null);
            WriteMemberRow(
                sheet,
                4,
                "Trần Thị Bình",
                "1992-08-20",
                "Female",
                "0902000002",
                null,
                new DateTime(2021, 3, 4),
                "inactive");
        });

        await using var stream = new MemoryStream(bytes);
        var result = await service.ImportAsync(stream, "members.xlsx", bytes.Length);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Result!.ImportedCount);
        Assert.Equal(2, result.Result.TotalRows);
        var members = await scope.ServiceProvider
            .GetRequiredService<DigitalOpsDbContext>()
            .Members
            .AsNoTracking()
            .OrderBy(member => member.FullName)
            .ToArrayAsync();
        Assert.Equal(2, members.Length);
        Assert.Equal("Nguyễn Văn An", members[0].FullName);
        Assert.Equal("an@example.com", members[0].Email);
        Assert.Equal("0901 000 001", members[0].Phone);
        Assert.Equal(new DateOnly(1990, 5, 12), members[0].DateOfBirth);
        Assert.Equal(MemberStatus.Active, members[0].Status);
        Assert.Equal(MemberStatus.Inactive, members[1].Status);
        Assert.All(members, member =>
        {
            Assert.NotEqual(default, member.CreatedAt);
            Assert.Equal(member.CreatedAt, member.UpdatedAt);
        });
    }

    [Fact]
    public async Task Row_errors_are_all_reported_and_no_valid_row_is_saved()
    {
        using var factory = new StaffManagementApiFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemberImportService>();
        var bytes = CreateWorkbook(sheet =>
        {
            WriteMemberRow(
                sheet,
                2,
                "Hội viên hợp lệ",
                null,
                null,
                "0901000000",
                null,
                null,
                null);
            sheet.Cell(3, 2).Value = "31/12/2020";
            sheet.Cell(3, 3).Value = "Unknown";
            sheet.Cell(3, 5).Value = 901234567;
            sheet.Cell(3, 6).Value = "không-phải-email";
            sheet.Cell(3, 9).Value = "Unknown";
            sheet.Cell(3, 10).FormulaA1 = "1+1";
        });

        await using var stream = new MemoryStream(bytes);
        var result = await service.ImportAsync(stream, "members.xlsx", bytes.Length);

        Assert.Equal(MemberImportFailure.Validation, result.Failure);
        Assert.Equal(2, result.Result!.TotalRows);
        Assert.Contains(result.Result.Errors, error =>
            error.RowNumber == 3 && error.Field == "fullName");
        Assert.Contains(result.Result.Errors, error => error.Field == "phone");
        Assert.Contains(result.Result.Errors, error => error.Field == "dateOfBirth");
        Assert.Contains(result.Result.Errors, error => error.Field == "gender");
        Assert.Contains(result.Result.Errors, error => error.Field == "email");
        Assert.Contains(result.Result.Errors, error => error.Field == "status");
        Assert.Contains(result.Result.Errors, error => error.Field == "notes");
        Assert.Equal(
            0,
            await scope.ServiceProvider
                .GetRequiredService<DigitalOpsDbContext>()
                .Members.CountAsync());
    }

    [Fact]
    public async Task Duplicate_key_is_case_insensitive_and_treats_null_as_equal()
    {
        using var factory = new StaffManagementApiFactory();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        dbContext.Members.Add(new Member
        {
            Id = Guid.NewGuid(),
            FullName = "Existing Member",
            Status = MemberStatus.Inactive
        });
        await dbContext.SaveChangesAsync();

        var bytes = CreateWorkbook(sheet =>
        {
            WriteMemberRow(sheet, 2, " existing   member ", null, null, null, null, null, null);
            WriteMemberRow(sheet, 3, "New Member", null, null, null, null, null, null);
            WriteMemberRow(sheet, 4, "new member", null, null, null, null, null, null);
        });
        var service = scope.ServiceProvider.GetRequiredService<IMemberImportService>();

        await using var stream = new MemoryStream(bytes);
        var result = await service.ImportAsync(stream, "members.xlsx", bytes.Length);

        Assert.Equal(MemberImportFailure.Validation, result.Failure);
        Assert.Contains(result.Result!.Errors, error =>
            error.RowNumber == 2
            && error.Field == "duplicateKey"
            && error.Message.Contains("hệ thống", StringComparison.Ordinal));
        Assert.Contains(result.Result.Errors, error =>
            error.RowNumber == 4
            && error.Field == "duplicateKey"
            && error.Message.Contains("dòng 3", StringComparison.Ordinal));
        Assert.Equal(1, await dbContext.Members.CountAsync());
    }

    [Fact]
    public async Task Configured_file_and_row_limits_are_enforced()
    {
        using var factory = new StaffManagementApiFactory();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var bytes = CreateWorkbook(sheet =>
        {
            WriteMemberRow(sheet, 2, "First", null, null, null, null, null, null);
            WriteMemberRow(sheet, 3, "Second", null, null, null, null, null, null);
        });

        var fileLimitedService = new MemberImportService(
            dbContext,
            Options.Create(new MemberImportOptions
            {
                MaxFileSizeBytes = 32,
                MaxRows = 10,
                MaxExpandedWorkbookBytes = 1024
            }));
        await using var firstStream = new MemoryStream(bytes);
        var fileLimited = await fileLimitedService.ImportAsync(
            firstStream,
            "members.xlsx",
            bytes.Length);
        Assert.Equal(MemberImportFailure.PayloadTooLarge, fileLimited.Failure);

        var rowLimitedService = new MemberImportService(
            dbContext,
            Options.Create(new MemberImportOptions
            {
                MaxFileSizeBytes = 1024 * 1024,
                MaxRows = 1,
                MaxExpandedWorkbookBytes = 10 * 1024 * 1024
            }));
        await using var secondStream = new MemoryStream(bytes);
        var rowLimited = await rowLimitedService.ImportAsync(
            secondStream,
            "members.xlsx",
            bytes.Length);
        Assert.Equal(MemberImportFailure.Validation, rowLimited.Failure);
        Assert.Contains(rowLimited.Result!.Errors, error =>
            error.RowNumber == 0 && error.Field == "file");
    }

    [Fact]
    public async Task Save_failure_rolls_back_the_import_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
            .AddInterceptors(new ThrowAfterSaveInterceptor())
            .Options;
        await using var dbContext = new DigitalOpsDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new MemberImportService(
            dbContext,
            Options.Create(new MemberImportOptions()));
        var bytes = CreateWorkbook(sheet =>
            WriteMemberRow(
                sheet,
                2,
                "Rollback Member",
                null,
                null,
                null,
                null,
                null,
                null));

        await using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportAsync(stream, "members.xlsx", bytes.Length));

        dbContext.ChangeTracker.Clear();
        Assert.Equal(0, await dbContext.Members.AsNoTracking().CountAsync());
    }

    internal static byte[] CreateWorkbook(Action<IXLWorksheet> populate)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Hội viên");
        for (var index = 0; index < ExpectedHeaders.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = ExpectedHeaders[index];
        }

        populate(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteMemberRow(
        IXLWorksheet sheet,
        int row,
        string fullName,
        object? dateOfBirth,
        string? gender,
        string? phone,
        string? email,
        object? joinDate,
        string? status)
    {
        sheet.Cell(row, 1).Value = fullName;
        SetValue(sheet.Cell(row, 2), dateOfBirth);
        SetValue(sheet.Cell(row, 3), gender);
        SetValue(sheet.Cell(row, 5), phone);
        SetValue(sheet.Cell(row, 6), email);
        SetValue(sheet.Cell(row, 8), joinDate);
        SetValue(sheet.Cell(row, 9), status);
    }

    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case string text:
                cell.Value = text;
                break;
            case DateTime dateTime:
                cell.Value = dateTime;
                break;
            default:
                throw new ArgumentException("Unsupported workbook test value.", nameof(value));
        }
    }

    private static string[] ReadHeaders(IXLWorksheet worksheet) =>
        Enumerable.Range(1, ExpectedHeaders.Length)
            .Select(column => worksheet.Cell(1, column).GetString())
            .ToArray();

    internal static readonly string[] ExpectedHeaders =
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

    private sealed class ThrowAfterSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new InvalidOperationException("Simulated save failure."));
    }
}

public sealed class MemberImportOptionsValidatorTests
{
    [Fact]
    public void Rejects_non_positive_inverted_and_out_of_range_limits()
    {
        var result = new MemberImportOptionsValidator().Validate(
            Options.DefaultName,
            new MemberImportOptions
            {
                MaxFileSizeBytes = 0,
                MaxRows = 1_048_576,
                MaxExpandedWorkbookBytes = -1
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(MemberImportOptions.MaxFileSizeBytes), StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(MemberImportOptions.MaxRows), StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Contains(
                nameof(MemberImportOptions.MaxExpandedWorkbookBytes),
                StringComparison.Ordinal));
    }
}

public sealed class MemberImportApiTests
{
    private const string Password = "Valid1!Password";
    private const string TemporaryPassword = "Temporary2!Password";

    [Fact]
    public async Task Template_and_import_enforce_access_and_return_expected_contracts()
    {
        using var factory = new StaffManagementApiFactory();
        using var anonymous = factory.CreateApiClient();
        await ProblemDetailsAssert.HasContractAsync(
            await anonymous.GetAsync("/api/v1/members/import-template"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/members/import-template");

        using var forced = factory.CreateApiClient();
        await AuthenticateAsync(forced, "forcedadmin");
        await ProblemDetailsAssert.HasContractAsync(
            await forced.GetAsync("/api/v1/members/import-template"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/api/v1/members/import-template");

        using var admin = factory.CreateApiClient();
        await AuthenticateAsync(admin, "admin");

        var createDrafter = await admin.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "import.drafter",
                "import.drafter@digitalops.local",
                TemporaryPassword,
                "Import Drafter",
                null,
                null,
                null,
                [SystemRoles.Drafter]));
        Assert.Equal(HttpStatusCode.Created, createDrafter.StatusCode);
        using var drafter = factory.CreateApiClient();
        await AuthenticateAsync(drafter, "import.drafter", TemporaryPassword);
        await CompletePasswordChangeAsync(
            drafter,
            TemporaryPassword,
            Password);
        await ProblemDetailsAssert.HasContractAsync(
            await drafter.GetAsync("/api/v1/members/import-template"),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/members/import-template");

        var template = await admin.GetAsync("/api/v1/members/import-template");
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal(
            MemberImportService.SpreadsheetContentType,
            template.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            MemberImportService.TemplateFileName,
            template.Content.Headers.ContentDisposition?.FileNameStar
                ?? template.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using (var workbook = new XLWorkbook(
            new MemoryStream(await template.Content.ReadAsByteArrayAsync())))
        {
            Assert.Equal(
                MemberImportServiceTests.ExpectedHeaders,
                Enumerable.Range(1, MemberImportServiceTests.ExpectedHeaders.Length)
                    .Select(column => workbook.Worksheet("Hội viên").Cell(1, column).GetString())
                    .ToArray());
        }

        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");
        var bytes = MemberImportServiceTests.CreateWorkbook(sheet =>
            sheet.Cell(2, 1).Value = "Imported through API");
        var import = await clerk.PostAsync(
            "/api/v1/members/import",
            CreateMultipart(bytes, "members.xlsx"));
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var report = await import.Content.ReadFromJsonAsync<MemberImportResult>();
        Assert.NotNull(report);
        Assert.Equal(1, report.ImportedCount);
        Assert.Equal(1, report.TotalRows);
    }

    [Fact]
    public async Task Import_returns_validation_unsupported_type_and_row_problem_details()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin");

        var missingFile = await client.PostAsync(
            "/api/v1/members/import",
            new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, missingFile.StatusCode);

        await ProblemDetailsAssert.HasContractAsync(
            await client.PostAsync(
                "/api/v1/members/import",
                CreateMultipart([1, 2, 3], "members.txt")),
            HttpStatusCode.UnsupportedMediaType,
            "unsupported-file-type",
            "/api/v1/members/import");

        await ProblemDetailsAssert.HasContractAsync(
            await client.PostAsync(
                "/api/v1/members/import",
                CreateMultipart([1, 2, 3], "members.xlsx")),
            HttpStatusCode.UnsupportedMediaType,
            "unsupported-file-type",
            "/api/v1/members/import");

        await ProblemDetailsAssert.HasContractAsync(
            await client.PostAsync(
                "/api/v1/members/import",
                CreateMultipart(
                    new byte[MemberImportOptions.DefaultMaxFileSizeBytes + 1],
                    "members.xlsx")),
            HttpStatusCode.RequestEntityTooLarge,
            "file-too-large",
            "/api/v1/members/import");

        var invalidBytes = MemberImportServiceTests.CreateWorkbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "Sai header";
            sheet.Cell(2, 1).Value = "Valid row";
        });
        var invalid = await client.PostAsync(
            "/api/v1/members/import",
            CreateMultipart(invalidBytes, "members.xlsx"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(
            "application/problem+json",
            invalid.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("importedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("totalRows").GetInt32());
        Assert.Contains(root.GetProperty("errors").EnumerateArray(), error =>
            error.GetProperty("rowNumber").GetInt32() == 1);
        Assert.Equal(
            "https://digitalops/errors/business-validation-failed",
            root.GetProperty("type").GetString());
    }

    private static MultipartFormDataContent CreateMultipart(
        byte[] bytes,
        string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            MemberImportService.SpreadsheetContentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private static async Task AuthenticateAsync(
        HttpClient client,
        string userName,
        string password = Password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static async Task CompletePasswordChangeAsync(
        HttpClient client,
        string currentPassword,
        string newPassword)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest(currentPassword, newPassword));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
}
