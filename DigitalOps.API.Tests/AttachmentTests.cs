using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalOps.API.Tests;

public sealed class AttachmentServiceTests
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.7\nDigitalOps attachment");
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    [Fact]
    public async Task Upload_uses_safe_keys_sets_status_and_populates_incoming_response()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();
        var data = await database.CreateIncomingAsync();

        var pdf = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream(PdfBytes),
            "../Báo cáo THÁNG.PDF",
            PdfBytes.Length);
        var png = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream(PngBytes),
            "ảnh.png",
            PngBytes.Length);

        Assert.True(pdf.Succeeded);
        Assert.True(png.Succeeded);
        Assert.Equal("Báo cáo THÁNG.PDF", pdf.Value!.FileName);
        Assert.Equal(ExtractionStatus.Pending, pdf.Value.ExtractionStatus);
        Assert.Equal(ExtractionStatus.Unsupported, png.Value!.ExtractionStatus);
        Assert.Null(pdf.Value.ExtractedAt);

        var entities = await database.Context.Attachments
            .OrderBy(item => item.UploadedAt)
            .ToArrayAsync();
        Assert.Equal(2, entities.Length);
        Assert.All(entities, entity =>
        {
            Assert.StartsWith($"incoming/{data.Document.Id:N}/", entity.StorageKey);
            Assert.DoesNotContain("Báo cáo", entity.StorageKey, StringComparison.Ordinal);
            Assert.Null(entity.ExtractedText);
            Assert.Null(entity.ExtractionError);
            Assert.Null(entity.ExtractedAt);
        });

        var incomingService = new IncomingDocumentService(
            database.Context,
            TimeProvider.System,
            new AssignmentSuggestionTestDouble(),
            NullLogger<IncomingDocumentService>.Instance);
        var response = await incomingService.GetByIdAsync(data.Document.Id);
        Assert.Equal(2, response!.Attachments.Count);
        Assert.Equal("B Test Clerk", response.Attachments[0].UploadedBy.FullName);
    }

    [Fact]
    public async Task Upload_rejects_bad_state_size_type_and_content_without_writing()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();
        var data = await database.CreateIncomingAsync();

        var fakePdf = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream("not-a-pdf"u8.ToArray()),
            "fake.pdf",
            9);
        Assert.Equal(AttachmentFailure.UnsupportedFileType, fakePdf.Failure);

        var oversized = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream(PdfBytes),
            "large.pdf",
            AttachmentStorageOptions.DefaultMaxFileSizeBytes + 1);
        Assert.Equal(AttachmentFailure.PayloadTooLarge, oversized.Failure);

        var unsupported = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream(PdfBytes),
            "file.txt",
            PdfBytes.Length);
        Assert.Equal(AttachmentFailure.UnsupportedFileType, unsupported.Failure);

        data.Document.Status = IncomingDocumentStatus.Completed;
        data.Document.CompletedAt = DateTime.UtcNow;
        await database.Context.SaveChangesAsync();
        var completed = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream(PdfBytes),
            "locked.pdf",
            PdfBytes.Length);
        Assert.Equal(AttachmentFailure.Conflict, completed.Failure);

        var missing = await database.Service.UploadIncomingAsync(
            Guid.NewGuid(),
            data.Staff.Id,
            new MemoryStream(PdfBytes),
            "missing.pdf",
            PdfBytes.Length);
        Assert.Equal(AttachmentFailure.NotFound, missing.Failure);
        Assert.Empty(await database.Context.Attachments.ToArrayAsync());
        Assert.False(Directory.Exists(database.RootPath));
    }

    [Fact]
    public async Task Download_returns_original_bytes_and_delete_removes_file_and_metadata()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();
        var data = await database.CreateIncomingAsync();
        var uploaded = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            new MemoryStream(PdfBytes),
            "report.pdf",
            PdfBytes.Length);

        var download = await database.Service.DownloadAsync(uploaded.Value!.Id);
        Assert.True(download.Succeeded);
        Assert.Equal("application/pdf", download.Value!.ContentType);
        await using (download.Value.Content)
        {
            using var buffer = new MemoryStream();
            await download.Value.Content.CopyToAsync(buffer);
            Assert.Equal(PdfBytes, buffer.ToArray());
        }

        var deleted = await database.Service.DeleteIncomingAsync(uploaded.Value.Id);
        Assert.True(deleted.Succeeded);
        Assert.Empty(await database.Context.Attachments.ToArrayAsync());
        Assert.Equal(
            AttachmentFailure.NotFound,
            (await database.Service.DownloadAsync(uploaded.Value.Id)).Failure);
        Assert.Empty(Directory.EnumerateFiles(database.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Local_storage_rejects_keys_that_escape_the_configured_root()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();

        await Assert.ThrowsAsync<AttachmentStorageException>(() =>
            database.Storage.WriteAsync(
                "../escape.pdf",
                new MemoryStream(PdfBytes)));
    }

    private sealed class AttachmentTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private AttachmentTestDatabase(
            SqliteConnection connection,
            DigitalOpsDbContext context,
            string rootPath,
            LocalAttachmentStorage storage,
            AttachmentService service)
        {
            _connection = connection;
            Context = context;
            RootPath = rootPath;
            Storage = storage;
            Service = service;
        }

        public DigitalOpsDbContext Context { get; }

        public string RootPath { get; }

        public LocalAttachmentStorage Storage { get; }

        public AttachmentService Service { get; }

        public static async Task<AttachmentTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new DigitalOpsDbContext(
                new DbContextOptionsBuilder<DigitalOpsDbContext>()
                    .UseSqlite(connection)
                    .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                    .Options);
            await context.Database.EnsureCreatedAsync();

            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "digitalops-attachment-tests",
                Guid.NewGuid().ToString("N"));
            var storageOptions = Options.Create(new AttachmentStorageOptions
            {
                RootPath = rootPath,
                MaxFileSizeBytes = AttachmentStorageOptions.DefaultMaxFileSizeBytes
            });
            var environment = new TestWebHostEnvironment
            {
                ContentRootPath = Path.GetTempPath(),
                WebRootPath = Path.Combine(Path.GetTempPath(), "wwwroot")
            };
            var storage = new LocalAttachmentStorage(storageOptions, environment);
            var service = new AttachmentService(
                context,
                storage,
                storageOptions,
                TimeProvider.System,
                NullLogger<AttachmentService>.Instance);
            return new AttachmentTestDatabase(
                connection,
                context,
                rootPath,
                storage,
                service);
        }

        public async Task<(IncomingDocument Document, Staff Staff)> CreateIncomingAsync()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"user-{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@test.local"
            };
            var staff = new Staff
            {
                Id = Guid.NewGuid(),
                IdentityUserId = user.Id,
                IdentityUser = user,
                FullName = "B Test Clerk",
                Email = user.Email!,
                IsActive = true
            };
            var type = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = $"TYPE-{Guid.NewGuid():N}",
                Name = "Test type",
                IsActive = true
            };
            var document = new IncomingDocument
            {
                Id = Guid.NewGuid(),
                ReferenceNumber = "01/TEST",
                SenderOrg = "Test sender",
                Summary = "Test attachment",
                ReceivedDate = new DateOnly(2026, 7, 31),
                Deadline = new DateOnly(2026, 8, 1),
                DocumentTypeId = type.Id,
                DocumentType = type,
                Status = IncomingDocumentStatus.New
            };
            Context.AddRange(staff, type, document);
            await Context.SaveChangesAsync();
            return (document, staff);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DigitalOps.API.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Testing";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class AttachmentApiTests
{
    private const string Password = "Valid1!Password";
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.7\nAPI attachment");

    [Fact]
    public async Task Clerk_uploads_business_user_downloads_and_only_clerk_deletes()
    {
        using var factory = new StaffManagementApiFactory();
        var incomingId = await CreateIncomingAsync(factory);
        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");

        using var uploadContent = CreateMultipart(PdfBytes, "Báo cáo.pdf", "application/pdf");
        var upload = await clerk.PostAsync(
            $"/api/v1/incoming-documents/{incomingId}/attachments",
            uploadContent);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.NotNull(upload.Headers.Location);
        var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentResponse>())!;
        Assert.Equal(ExtractionStatus.Pending, attachment.ExtractionStatus);
        Assert.Equal("B Clerk", attachment.UploadedBy.FullName);
        Assert.DoesNotContain("fileUrl", await upload.Content.ReadAsStringAsync());

        using var admin = factory.CreateApiClient();
        await AuthenticateAsync(admin, "admin");
        var forbiddenUpload = await admin.PostAsync(
            $"/api/v1/incoming-documents/{incomingId}/attachments",
            CreateMultipart(PdfBytes, "admin.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpload.StatusCode);

        var download = await admin.GetAsync($"/api/v1/attachments/{attachment.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfBytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(
            "Báo cáo.pdf",
            download.Content.Headers.ContentDisposition?.FileNameStar);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await admin.DeleteAsync($"/api/v1/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await clerk.DeleteAsync($"/api/v1/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await clerk.GetAsync($"/api/v1/attachments/{attachment.Id}/download")).StatusCode);
    }

    [Fact]
    public async Task Upload_enforces_auth_validation_type_size_and_completed_lock()
    {
        using var factory = new StaffManagementApiFactory();
        var incomingId = await CreateIncomingAsync(factory);
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync(
                $"/api/v1/incoming-documents/{incomingId}/attachments",
                CreateMultipart(PdfBytes, "anonymous.pdf", "application/pdf"))).StatusCode);

        using var forced = factory.CreateApiClient();
        await AuthenticateAsync(forced, "forcedadmin");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await forced.PostAsync(
                $"/api/v1/incoming-documents/{incomingId}/attachments",
                CreateMultipart(PdfBytes, "forced.pdf", "application/pdf"))).StatusCode);

        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");
        var emptyForm = await clerk.PostAsync(
            $"/api/v1/incoming-documents/{incomingId}/attachments",
            new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, emptyForm.StatusCode);

        var fake = await clerk.PostAsync(
            $"/api/v1/incoming-documents/{incomingId}/attachments",
            CreateMultipart("not a pdf"u8.ToArray(), "fake.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, fake.StatusCode);

        var oversizedBytes = new byte[AttachmentStorageOptions.DefaultMaxFileSizeBytes + 1];
        PdfBytes.CopyTo(oversizedBytes, 0);
        var oversized = await clerk.PostAsync(
            $"/api/v1/incoming-documents/{incomingId}/attachments",
            CreateMultipart(oversizedBytes, "large.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);

        await SetCompletedAsync(factory, incomingId);
        var completed = await clerk.PostAsync(
            $"/api/v1/incoming-documents/{incomingId}/attachments",
            CreateMultipart(PdfBytes, "locked.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.Conflict, completed.StatusCode);
    }

    private static MultipartFormDataContent CreateMultipart(
        byte[] bytes,
        string fileName,
        string contentType)
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(file, "file", fileName);
        return multipart;
    }

    private static async Task<Guid> CreateIncomingAsync(StaffManagementApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var type = new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = $"TYPE-{Guid.NewGuid():N}",
            Name = "Test type",
            IsActive = true
        };
        var document = new IncomingDocument
        {
            Id = Guid.NewGuid(),
            ReferenceNumber = "01/ATTACHMENT",
            SenderOrg = "Test sender",
            Summary = "Attachment API",
            ReceivedDate = new DateOnly(2026, 7, 31),
            Deadline = new DateOnly(2026, 8, 1),
            DocumentTypeId = type.Id,
            DocumentType = type,
            Status = IncomingDocumentStatus.New
        };
        dbContext.AddRange(type, document);
        await dbContext.SaveChangesAsync();
        return document.Id;
    }

    private static async Task SetCompletedAsync(
        StaffManagementApiFactory factory,
        Guid incomingId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var document = await dbContext.IncomingDocuments.SingleAsync(
            item => item.Id == incomingId);
        document.Status = IncomingDocumentStatus.Completed;
        document.CompletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private static async Task AuthenticateAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
}
