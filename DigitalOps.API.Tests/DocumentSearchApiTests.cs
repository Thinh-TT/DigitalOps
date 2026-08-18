using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Search;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class DocumentSearchApiTests
{
    private const string Password = "Valid1!Password";

    [Fact]
    public async Task Search_requires_valid_query_and_business_access()
    {
        using var factory = new StaffManagementApiFactory();
        using var anonymous = factory.CreateApiClient();

        // 1. Unauthenticated -> 401
        var unauth = await anonymous.GetAsync("/api/v1/documents/search?q=test");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        // 2. Password change required -> 403
        using var forced = factory.CreateApiClient();
        await AuthenticateAsync(forced, "forcedadmin");
        var forbidden = await forced.GetAsync("/api/v1/documents/search?q=test");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // 3. Authenticated staff with empty / short query -> 400
        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");

        var emptyQuery = await clerk.GetAsync("/api/v1/documents/search?q=");
        Assert.Equal(HttpStatusCode.BadRequest, emptyQuery.StatusCode);

        var shortQuery = await clerk.GetAsync("/api/v1/documents/search?q=a");
        Assert.Equal(HttpStatusCode.BadRequest, shortQuery.StatusCode);

        var invalidDates = await clerk.GetAsync("/api/v1/documents/search?q=abc&dateFrom=2026-08-30&dateTo=2026-08-01");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDates.StatusCode);
    }

    [Fact]
    public async Task Search_returns_matching_incoming_outgoing_and_attachments()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "clerk");

        // Seed test data
        Guid docTypeId1;
        Guid docTypeId2;
        Guid incomingDocId;
        Guid outgoingDocId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
            var staff = db.Staff.First();

            var type1 = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = "CV-TEST",
                Name = "Công văn thử nghiệm",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var type2 = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = "TB-TEST",
                Name = "Thông báo nội bộ",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.DocumentTypes.AddRange(type1, type2);

            var template = new DocumentTemplate
            {
                Id = Guid.NewGuid(),
                DocumentTypeId = type1.Id,
                Name = "Mẫu công văn",
                TemplateContent = "Kính gửi...",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.DocumentTemplates.Add(template);

            // 1. Incoming doc with "quy hoạch đô thị" in summary
            var incoming = new IncomingDocument
            {
                Id = Guid.NewGuid(),
                ReferenceNumber = "100/UBND-QH",
                SenderOrg = "Sở Quy hoạch",
                Summary = "Báo cáo phương án quy hoạch đô thị khu vực phía Tây thành phố",
                ReceivedDate = new DateOnly(2026, 8, 10),
                Deadline = new DateOnly(2026, 8, 20),
                DocumentTypeId = type1.Id,
                Status = IncomingDocumentStatus.New,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.IncomingDocuments.Add(incoming);

            // 2. Outgoing doc with "quy hoạch đô thị" in title and content
            var outgoing = new OutgoingDocument
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                ReferenceNumber = "200/CV-UBND",
                Title = "Tờ trình phê duyệt quy hoạch đô thị",
                Content = "Kính trình Ủy ban xem xét kế hoạch phát triển hạ tầng và quy hoạch đô thị",
                AiDraftContent = "Bản thảo AI về định hướng quy hoạch",
                DraftedByStaffId = staff.Id,
                Status = OutgoingDocumentStatus.Approved,
                ApprovedByStaffId = staff.Id,
                ApprovedAt = DateTime.UtcNow,
                IssuedDate = new DateOnly(2026, 8, 12),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.OutgoingDocuments.Add(outgoing);

            // 3. Attachments: 1 succeeded with keyword, 1 failed with keyword (should be ignored)
            var succeededAtt = new Attachment
            {
                Id = Guid.NewGuid(),
                IncomingDocumentId = incoming.Id,
                FileName = "so_do_quy_hoach.docx",
                StorageKey = "att-001",
                UploadedByStaffId = staff.Id,
                ExtractionStatus = ExtractionStatus.Succeeded,
                ExtractedText = "Tài liệu thuyết minh chi tiết bản đồ quy hoạch giao thông kết nối liên vùng",
                ExtractedAt = DateTime.UtcNow,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var failedAtt = new Attachment
            {
                Id = Guid.NewGuid(),
                IncomingDocumentId = incoming.Id,
                FileName = "file_loi.pdf",
                StorageKey = "att-002",
                UploadedByStaffId = staff.Id,
                ExtractionStatus = ExtractionStatus.Failed,
                ExtractedText = "Từ khóa quy hoạch trong file lỗi không được index",
                ExtractionError = "Parse error",
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Attachments.AddRange(succeededAtt, failedAtt);
            await db.SaveChangesAsync();

            docTypeId1 = type1.Id;
            docTypeId2 = type2.Id;
            incomingDocId = incoming.Id;
            outgoingDocId = outgoing.Id;
        }

        // Test 1: Search keyword "quy hoạch" -> returns incoming & outgoing
        var search1 = await client.GetFromJsonAsync<PagedResponse<DocumentSearchResult>>(
            "/api/v1/documents/search?q=quy%20ho%E1%BA%A1ch");
        Assert.NotNull(search1);
        Assert.True(search1.TotalCount >= 2);
        Assert.Contains(search1.Items, i => i.DocumentId == incomingDocId);
        Assert.Contains(search1.Items, i => i.DocumentId == outgoingDocId);

        var firstResult = search1.Items.First();
        Assert.True(firstResult.Score > 0);
        Assert.Contains("<b>", firstResult.Snippet);

        // Test 2: Filter by documentKind = Incoming
        var searchIncomingOnly = await client.GetFromJsonAsync<PagedResponse<DocumentSearchResult>>(
            "/api/v1/documents/search?q=quy%20ho%E1%BA%A1ch&documentKind=Incoming");
        Assert.NotNull(searchIncomingOnly);
        Assert.All(searchIncomingOnly.Items, i => Assert.Equal(DocumentKind.Incoming, i.DocumentKind));

        // Test 3: Filter by documentKind = Outgoing
        var searchOutgoingOnly = await client.GetFromJsonAsync<PagedResponse<DocumentSearchResult>>(
            "/api/v1/documents/search?q=quy%20ho%E1%BA%A1ch&documentKind=Outgoing");
        Assert.NotNull(searchOutgoingOnly);
        Assert.All(searchOutgoingOnly.Items, i => Assert.Equal(DocumentKind.Outgoing, i.DocumentKind));

        // Test 4: Search attachment-only text ("giao thông kết nối")
        var searchAttachment = await client.GetFromJsonAsync<PagedResponse<DocumentSearchResult>>(
            "/api/v1/documents/search?q=giao%20th%C3%B4ng%20k%E1%BA%BFt%20n%E1%BB%91i");
        Assert.NotNull(searchAttachment);
        var attHit = Assert.Single(searchAttachment.Items);
        Assert.Equal(incomingDocId, attHit.DocumentId);
        Assert.Equal(DocumentSearchMatchSource.Attachment, attHit.MatchSource);
        Assert.Contains("[Tệp: so_do_quy_hoach.docx]", attHit.Snippet);

        // Test 5: Search keyword in Failed attachment ("file lỗi không được index") -> 0 items
        var searchFailed = await client.GetFromJsonAsync<PagedResponse<DocumentSearchResult>>(
            "/api/v1/documents/search?q=file%20l%E1%BB%97i%20kh%C3%B4ng%20%C4%91%C6%B0%E1%BB%A3c%20index");
        Assert.NotNull(searchFailed);
        Assert.Empty(searchFailed.Items);

        // Test 6: Filter by documentTypeId
        var searchByType = await client.GetFromJsonAsync<PagedResponse<DocumentSearchResult>>(
            $"/api/v1/documents/search?q=quy%20ho%E1%BA%A1ch&documentTypeId={docTypeId2}");
        Assert.NotNull(searchByType);
        Assert.Empty(searchByType.Items);
    }

    private static async Task AuthenticateAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, Password));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
}
