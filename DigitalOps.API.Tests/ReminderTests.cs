using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Reminders;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class ReminderServiceTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 8, 1, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Process_creates_reminders_transitions_overdue_and_is_idempotent()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var documentType = await database.CreateDocumentTypeAsync();
        var assigned = await database.CreateStaffAsync("Người được giao");
        var today = new DateOnly(2026, 8, 1);

        var beforeDeadline = await database.CreateIncomingDocumentAsync(
            documentType.Id,
            assigned.Id,
            deadline: today.AddDays(3));
        var dueToday = await database.CreateIncomingDocumentAsync(
            documentType.Id,
            assigned.Id,
            deadline: today);
        var overdue = await database.CreateIncomingDocumentAsync(
            documentType.Id,
            assigned.Id,
            deadline: today.AddDays(-1));
        var unassignedOverdue = await database.CreateIncomingDocumentAsync(
            documentType.Id,
            assignedStaffId: null,
            deadline: today.AddDays(-1));
        await database.CreateIncomingDocumentAsync(
            documentType.Id,
            assigned.Id,
            deadline: today.AddDays(-1),
            status: IncomingDocumentStatus.Completed);

        var service = database.CreateService();
        var first = await service.ProcessAsync(today);

        Assert.Equal(2, first.OverdueDocuments);
        Assert.Equal(3, first.CreatedReminders);
        Assert.Equal(0, first.ExistingReminders);
        Assert.Equal(
            IncomingDocumentStatus.Overdue,
            (await database.Context.IncomingDocuments.SingleAsync(item => item.Id == overdue.Id)).Status);
        Assert.Equal(
            IncomingDocumentStatus.Overdue,
            (await database.Context.IncomingDocuments.SingleAsync(item => item.Id == unassignedOverdue.Id)).Status);

        var reminders = await database.Context.ReminderHistory
            .AsNoTracking()
            .OrderBy(item => item.ReminderKind)
            .ToArrayAsync();
        Assert.Equal(3, reminders.Length);
        Assert.Contains(reminders, item =>
            item.IncomingDocumentId == beforeDeadline.Id
            && item.ReminderKind == ReminderKind.BeforeDeadline
            && item.ReminderDate == today);
        Assert.Contains(reminders, item =>
            item.IncomingDocumentId == dueToday.Id
            && item.ReminderKind == ReminderKind.DueDate
            && item.ReminderDate == today);
        Assert.Contains(reminders, item =>
            item.IncomingDocumentId == overdue.Id
            && item.ReminderKind == ReminderKind.Overdue
            && item.ReminderDate == today);
        Assert.DoesNotContain(reminders, item => item.IncomingDocumentId == unassignedOverdue.Id);

        var repeated = await service.ProcessAsync(today);
        Assert.Equal(0, repeated.OverdueDocuments);
        Assert.Equal(0, repeated.CreatedReminders);
        Assert.Equal(3, repeated.ExistingReminders);
        Assert.Equal(3, await database.Context.ReminderHistory.CountAsync());

        var nextDay = await service.ProcessAsync(today.AddDays(1));
        Assert.Equal(1, nextDay.OverdueDocuments);
        Assert.Equal(2, nextDay.CreatedReminders);
        Assert.Equal(5, await database.Context.ReminderHistory.CountAsync());
    }

    [Fact]
    public async Task List_and_mark_read_enforce_recipient_access_and_preserve_read_timestamp()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var documentType = await database.CreateDocumentTypeAsync();
        var recipient = await database.CreateStaffAsync("Người nhận");
        var other = await database.CreateStaffAsync("Người khác");
        var document = await database.CreateIncomingDocumentAsync(
            documentType.Id,
            recipient.Id,
            deadline: new DateOnly(2026, 8, 4));
        var reminder = new ReminderHistory
        {
            Id = Guid.NewGuid(),
            IncomingDocumentId = document.Id,
            RecipientStaffId = recipient.Id,
            ReminderKind = ReminderKind.BeforeDeadline,
            ReminderDate = new DateOnly(2026, 8, 1),
            CreatedAt = UtcNow.UtcDateTime
        };
        database.Context.ReminderHistory.Add(reminder);
        await database.Context.SaveChangesAsync();

        var service = database.CreateService();
        var forbiddenList = await service.GetListAsync(
            new ReminderListQuery { RecipientStaffId = recipient.Id },
            other.Id,
            currentStaffIsAdministrator: false);
        Assert.Equal(ReminderServiceFailure.Forbidden, forbiddenList.Failure);

        var own = await service.GetListAsync(
            new ReminderListQuery { DeliveryStatus = ReminderDeliveryStatus.Unread },
            recipient.Id,
            currentStaffIsAdministrator: false);
        Assert.True(own.Succeeded);
        Assert.Single(own.Value!.Items);
        Assert.Equal(document.ReferenceNumber, own.Value.Items[0].ReferenceNumber);

        var forbiddenRead = await service.MarkReadAsync(
            reminder.Id,
            other.Id,
            currentStaffIsAdministrator: false);
        Assert.Equal(ReminderServiceFailure.Forbidden, forbiddenRead.Failure);

        var read = await service.MarkReadAsync(
            reminder.Id,
            recipient.Id,
            currentStaffIsAdministrator: false);
        Assert.True(read.Succeeded);
        Assert.Equal(ReminderDeliveryStatus.Read, read.Value!.DeliveryStatus);
        Assert.Equal(UtcNow.UtcDateTime, read.Value.ReadAt);

        var repeated = await service.MarkReadAsync(
            reminder.Id,
            recipient.Id,
            currentStaffIsAdministrator: false);
        Assert.True(repeated.Succeeded);
        Assert.Equal(read.Value.ReadAt, repeated.Value!.ReadAt);

        var administrator = await service.GetListAsync(
            new ReminderListQuery { RecipientStaffId = recipient.Id },
            other.Id,
            currentStaffIsAdministrator: true);
        Assert.Single(administrator.Value!.Items);
    }

    [Fact]
    public void Reminder_timezone_options_accept_the_cross_platform_default()
    {
        var options = new ReminderWorkerOptions();
        var validation = new ReminderWorkerOptionsValidator().Validate(
            Options.DefaultName,
            options);

        Assert.True(validation.Succeeded);
        Assert.True(ReminderTimeZoneResolver.TryResolve(options.TimeZoneId, out var timeZone));
        Assert.NotNull(timeZone);
    }

    private sealed class ReminderDatabase : IAsyncDisposable
    {
        private ReminderDatabase(SqliteConnection connection, DigitalOpsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public DigitalOpsDbContext Context { get; }

        public static async Task<ReminderDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new ReminderDatabase(connection, context);
        }

        public ReminderService CreateService() =>
            new(
                Context,
                new FixedTimeProvider(UtcNow),
                Options.Create(new ReminderWorkerOptions()));

        public async Task<DocumentType> CreateDocumentTypeAsync()
        {
            var documentType = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = $"TYPE-{Guid.NewGuid():N}"[..20],
                Name = "Thông báo",
                IsActive = true
            };
            Context.DocumentTypes.Add(documentType);
            await Context.SaveChangesAsync();
            return documentType;
        }

        public async Task<Staff> CreateStaffAsync(string fullName)
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
                FullName = fullName,
                Email = user.Email!,
                IsActive = true
            };
            Context.Staff.Add(staff);
            await Context.SaveChangesAsync();
            return staff;
        }

        public async Task<IncomingDocument> CreateIncomingDocumentAsync(
            Guid documentTypeId,
            Guid? assignedStaffId,
            DateOnly deadline,
            IncomingDocumentStatus status = IncomingDocumentStatus.New)
        {
            var document = new IncomingDocument
            {
                Id = Guid.NewGuid(),
                ReferenceNumber = $"VB-{Guid.NewGuid():N}",
                SenderOrg = "UBND phường",
                Summary = "Nội dung nhắc hạn",
                ReceivedDate = deadline.AddDays(-1),
                Deadline = deadline,
                DocumentTypeId = documentTypeId,
                AssignedToStaffId = assignedStaffId,
                AssignmentConfirmedByStaffId = assignedStaffId,
                AssignmentConfirmedAt = assignedStaffId is null ? null : UtcNow.UtcDateTime,
                Status = status,
                CompletedAt = status == IncomingDocumentStatus.Completed
                    ? UtcNow.UtcDateTime
                    : null
            };
            Context.IncomingDocuments.Add(document);
            await Context.SaveChangesAsync();
            return document;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

public sealed class ReminderApiTests
{
    private const string Password = "Valid1!Password";

    [Fact]
    public async Task Endpoints_scope_reminders_to_recipient_and_allow_administrator_support()
    {
        using var factory = new StaffManagementApiFactory();
        using var anonymous = factory.CreateApiClient();
        await ProblemDetailsAssert.HasContractAsync(
            await anonymous.GetAsync("/api/v1/reminders"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/reminders");

        var reminder = await CreateReminderAsync(factory, "target");
        var target = await factory.FindStaffAsync("target");

        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");
        Assert.Empty((await clerk.GetFromJsonAsync<PagedResponse<ReminderResponse>>(
            "/api/v1/reminders"))!.Items);
        await ProblemDetailsAssert.HasContractAsync(
            await clerk.GetAsync($"/api/v1/reminders?recipientStaffId={target.Id}"),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/reminders");
        await ProblemDetailsAssert.HasContractAsync(
            await clerk.PostAsync($"/api/v1/reminders/{reminder.Id}/read", null),
            HttpStatusCode.Forbidden,
            "forbidden",
            $"/api/v1/reminders/{reminder.Id}/read");

        using var administrator = factory.CreateApiClient();
        await AuthenticateAsync(administrator, "admin");
        var adminList = await administrator.GetFromJsonAsync<PagedResponse<ReminderResponse>>(
            $"/api/v1/reminders?recipientStaffId={target.Id}&deliveryStatus=Unread&page=1&pageSize=20");
        Assert.Single(adminList!.Items);
        Assert.Equal(reminder.Id, adminList.Items[0].Id);

        using var recipient = factory.CreateApiClient();
        await AuthenticateAsync(recipient, "target");
        var read = await recipient.PostAsync($"/api/v1/reminders/{reminder.Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readReminder = (await read.Content.ReadFromJsonAsync<ReminderResponse>())!;
        Assert.Equal(ReminderDeliveryStatus.Read, readReminder.DeliveryStatus);
        Assert.NotNull(readReminder.ReadAt);

        var repeated = await recipient.PostAsync($"/api/v1/reminders/{reminder.Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(
            readReminder.ReadAt,
            (await repeated.Content.ReadFromJsonAsync<ReminderResponse>())!.ReadAt);

        var missingId = Guid.NewGuid();
        await ProblemDetailsAssert.HasContractAsync(
            await recipient.PostAsync($"/api/v1/reminders/{missingId}/read", null),
            HttpStatusCode.NotFound,
            "not-found",
            $"/api/v1/reminders/{missingId}/read");
    }

    private static async Task<ReminderHistory> CreateReminderAsync(
        StaffManagementApiFactory factory,
        string recipientUserName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var recipient = await dbContext.Staff
            .Include(staff => staff.IdentityUser)
            .SingleAsync(staff => staff.IdentityUser.UserName == recipientUserName);
        var documentType = new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = $"REM-{Guid.NewGuid():N}"[..20],
            Name = "Nhắc hạn",
            IsActive = true
        };
        var document = new IncomingDocument
        {
            Id = Guid.NewGuid(),
            ReferenceNumber = "01/NH",
            SenderOrg = "UBND phường",
            Summary = "Thông báo cần xử lý",
            ReceivedDate = new DateOnly(2026, 7, 30),
            Deadline = new DateOnly(2026, 8, 4),
            DocumentType = documentType,
            DocumentTypeId = documentType.Id,
            AssignedToStaffId = recipient.Id,
            AssignmentConfirmedByStaffId = recipient.Id,
            AssignmentConfirmedAt = DateTime.UtcNow,
            Status = IncomingDocumentStatus.InProgress
        };
        var reminder = new ReminderHistory
        {
            Id = Guid.NewGuid(),
            IncomingDocument = document,
            IncomingDocumentId = document.Id,
            RecipientStaffId = recipient.Id,
            ReminderKind = ReminderKind.BeforeDeadline,
            ReminderDate = new DateOnly(2026, 8, 1),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.ReminderHistory.Add(reminder);
        await dbContext.SaveChangesAsync();
        return reminder;
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
