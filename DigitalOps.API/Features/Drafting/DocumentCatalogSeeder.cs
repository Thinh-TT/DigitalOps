using System.Data;
using System.Text.Json;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentCatalogSeeder(
    DigitalOpsDbContext dbContext,
    IDocumentCatalogService catalogService,
    ILogger<DocumentCatalogSeeder> logger) : IDocumentCatalogSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var createdTypeCount = 0;
            var createdTemplateCount = 0;
            var preservedTypeCount = 0;
            var preservedTemplateCount = 0;
            var inactiveParentSkipCount = 0;

            foreach (var definition in Definitions)
            {
                var documentType = await FindOrCreateDocumentTypeAsync(
                    definition,
                    cancellationToken);

                if (documentType.Created)
                {
                    createdTypeCount++;
                }
                else
                {
                    preservedTypeCount++;
                    logger.LogInformation(
                        "Document catalog seed preserved existing document type {DocumentTypeCode}.",
                        definition.Code);
                }

                var templateExists = await dbContext.DocumentTemplates
                    .AsNoTracking()
                    .AnyAsync(
                        template => template.DocumentTypeId == documentType.Value.Id
                            && template.Name == definition.TemplateName,
                        cancellationToken);
                if (templateExists)
                {
                    preservedTemplateCount++;
                    logger.LogInformation(
                        "Document catalog seed preserved existing template {TemplateName} for type {DocumentTypeCode}.",
                        definition.TemplateName,
                        definition.Code);
                    continue;
                }

                if (!documentType.Value.IsActive)
                {
                    inactiveParentSkipCount++;
                    logger.LogWarning(
                        "Document catalog seed skipped missing template {TemplateName} because document type {DocumentTypeCode} is inactive.",
                        definition.TemplateName,
                        definition.Code);
                    continue;
                }

                await CreateDocumentTemplateAsync(
                    documentType.Value.Id,
                    definition,
                    cancellationToken);
                createdTemplateCount++;
            }

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Document catalog seed completed. Created {CreatedTypeCount} types and {CreatedTemplateCount} templates; preserved {PreservedTypeCount} types and {PreservedTemplateCount} templates; skipped {InactiveParentSkipCount} templates with inactive parents.",
                createdTypeCount,
                createdTemplateCount,
                preservedTypeCount,
                preservedTemplateCount,
                inactiveParentSkipCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Document catalog seed failed. The transaction will be rolled back.");
            throw new InvalidOperationException(
                "Document catalog seed failed. No seed data was committed.",
                exception);
        }
    }

    private async Task<(DocumentTypeResponse Value, bool Created)> FindOrCreateDocumentTypeAsync(
        DocumentCatalogSeedDefinition definition,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.DocumentTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                documentType => documentType.Code == definition.Code,
                cancellationToken);
        if (existing is not null)
        {
            return (
                new DocumentTypeResponse(
                    existing.Id,
                    existing.Code,
                    existing.Name,
                    existing.Description,
                    existing.IsActive,
                    existing.CreatedAt,
                    existing.UpdatedAt),
                false);
        }

        var result = await catalogService.CreateDocumentTypeAsync(
            new DocumentTypeRequest
            {
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                IsActive = true
            },
            cancellationToken);
        if (!result.Succeeded)
        {
            throw CreateSeedFailure(
                $"document type '{definition.Code}'",
                result.Failure);
        }

        return (result.Value!, true);
    }

    private async Task CreateDocumentTemplateAsync(
        Guid documentTypeId,
        DocumentCatalogSeedDefinition definition,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CreateDocumentTemplateAsync(
            new DocumentTemplateRequest
            {
                DocumentTypeId = documentTypeId,
                Name = definition.TemplateName,
                TemplateContent = CreateTemplateContent(definition.TemplateBody),
                FormatRules = FormatRules.Clone(),
                IsActive = true
            },
            cancellationToken);
        if (!result.Succeeded)
        {
            throw CreateSeedFailure(
                $"template '{definition.TemplateName}' for type '{definition.Code}'",
                result.Failure);
        }
    }

    private static InvalidOperationException CreateSeedFailure(
        string target,
        DocumentCatalogFailure failure) =>
        new($"Document catalog seed could not create {target}. Failure: {failure}.");

    private static string CreateTemplateContent(string body) =>
        string.Join(
            Environment.NewLine + Environment.NewLine,
            CommonHeader,
            body,
            CommonFooter);

    private static readonly JsonElement FormatRules = JsonDocument.Parse(
        """
        {
          "version": 1,
          "rules": [
            { "code": "national_header", "required": true },
            { "code": "reference_number", "required": true },
            { "code": "signature_block", "required": true }
          ]
        }
        """).RootElement.Clone();

    private const string CommonHeader = """
        TÊN CƠ QUAN
        Số: .../...

        CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM
        Độc lập - Tự do - Hạnh phúc

        ..., ngày ... tháng ... năm ...
        """;

    private const string CommonFooter = """
        Nơi nhận:
        - ...;
        - Lưu: VT.

        ĐẠI DIỆN CƠ QUAN
        (Ký, ghi rõ họ tên)
        """;

    private static readonly IReadOnlyList<DocumentCatalogSeedDefinition> Definitions =
    [
        new(
            "PLAN",
            "Kế hoạch",
            "Kế hoạch triển khai nhiệm vụ và hoạt động.",
            "Mẫu kế hoạch cơ bản",
            """
            KẾ HOẠCH
            ...

            I. MỤC ĐÍCH, YÊU CẦU
            ...

            II. NỘI DUNG
            ...

            III. TIẾN ĐỘ THỰC HIỆN
            ...

            IV. TỔ CHỨC THỰC HIỆN
            ...
            """),
        new(
            "PROGRAM",
            "Chương trình",
            "Chương trình tổ chức hội nghị, sự kiện và hoạt động.",
            "Mẫu chương trình cơ bản",
            """
            CHƯƠNG TRÌNH
            ...

            I. MỤC ĐÍCH, YÊU CẦU
            ...

            II. THỜI GIAN, ĐỊA ĐIỂM
            ...

            III. THÀNH PHẦN
            ...

            IV. NỘI DUNG CHƯƠNG TRÌNH
            ...

            V. TỔ CHỨC THỰC HIỆN
            ...
            """),
        new(
            "REPORT",
            "Báo cáo",
            "Báo cáo tình hình và kết quả thực hiện nhiệm vụ.",
            "Mẫu báo cáo cơ bản",
            """
            BÁO CÁO
            ...

            I. TÌNH HÌNH CHUNG
            ...

            II. KẾT QUẢ THỰC HIỆN
            ...

            III. HẠN CHẾ, NGUYÊN NHÂN
            ...

            IV. NHIỆM VỤ, GIẢI PHÁP THỜI GIAN TỚI
            ...
            """),
        new(
            "RESOLUTION",
            "Nghị quyết",
            "Nghị quyết của tập thể hoặc hội nghị.",
            "Mẫu nghị quyết cơ bản",
            """
            NGHỊ QUYẾT
            ...

            Căn cứ ...;
            Sau khi thảo luận và thống nhất,

            QUYẾT NGHỊ:

            Điều 1. ...

            Điều 2. ...

            Điều 3. ...
            """),
        new(
            "CONCLUSION_NOTICE",
            "Thông báo kết luận",
            "Thông báo kết luận và phân công sau cuộc họp.",
            "Mẫu thông báo kết luận cơ bản",
            """
            THÔNG BÁO KẾT LUẬN
            ...

            Ngày ..., ... đã chủ trì cuộc họp về ...
            Sau khi nghe báo cáo và các ý kiến, kết luận như sau:

            1. Kết luận
            ...

            2. Phân công thực hiện
            ...

            3. Thời hạn hoàn thành
            ...
            """),
        new(
            "DECISION",
            "Quyết định",
            "Quyết định quản lý và tổ chức thực hiện.",
            "Mẫu quyết định cơ bản",
            """
            QUYẾT ĐỊNH
            ...

            Căn cứ ...;
            Theo đề nghị của ...,

            QUYẾT ĐỊNH:

            Điều 1. ...

            Điều 2. ...

            Điều 3. ...
            """),
        new(
            "INVITATION",
            "Giấy mời",
            "Giấy mời tham dự cuộc họp hoặc sự kiện.",
            "Mẫu giấy mời cơ bản",
            """
            GIẤY MỜI
            ...

            Kính mời: ...

            Thời gian: ...

            Địa điểm: ...

            Nội dung: ...

            Đề nghị đại biểu tham dự đầy đủ, đúng giờ.
            """)
    ];

    private sealed record DocumentCatalogSeedDefinition(
        string Code,
        string Name,
        string Description,
        string TemplateName,
        string TemplateBody);
}
