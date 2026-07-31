using System.Security.Claims;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DigitalOps.API.Tests;

public sealed class CurrentStaffAccessHandlerTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Requirement_matches_the_must_change_password_claim(
        bool claimValue,
        bool requiredValue,
        bool expectedSuccess)
    {
        var checker = new RecordingStaffAccessChecker(isActive: true);
        var requirement = new CurrentStaffAccessRequirement(requiredValue);
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(claimValue),
            resource: null);
        var handler = new CurrentStaffAccessHandler(checker);

        await handler.HandleAsync(context);

        Assert.Equal(expectedSuccess, context.HasSucceeded);
        Assert.True(checker.WasCalled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_staff_requirement_accepts_both_password_states(
        bool mustChangePassword)
    {
        var checker = new RecordingStaffAccessChecker(isActive: true);
        var requirement = new CurrentStaffAccessRequirement(MustChangePassword: null);
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(mustChangePassword),
            resource: null);
        var handler = new CurrentStaffAccessHandler(checker);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.True(checker.WasCalled);
    }

    [Fact]
    public async Task Forced_password_failure_records_the_specific_reason()
    {
        var checker = new RecordingStaffAccessChecker(isActive: true);
        var requirement = new CurrentStaffAccessRequirement(MustChangePassword: false);
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(mustChangePassword: true),
            resource: null);
        var handler = new CurrentStaffAccessHandler(checker);

        await handler.HandleAsync(context);

        var reason = Assert.Single(context.FailureReasons);
        Assert.Equal(
            CurrentStaffAccessHandler.PasswordChangeRequiredFailureReason,
            reason.Message);
    }

    [Fact]
    public async Task Requirement_fails_when_the_staff_link_is_inactive_or_mismatched()
    {
        var checker = new RecordingStaffAccessChecker(isActive: false);
        var requirement = new CurrentStaffAccessRequirement(MustChangePassword: false);
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(mustChangePassword: false),
            resource: null);
        var handler = new CurrentStaffAccessHandler(checker);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(checker.WasCalled);
    }

    [Fact]
    public async Task Requirement_fails_without_valid_subject_and_staff_claims()
    {
        var checker = new RecordingStaffAccessChecker(isActive: true);
        var requirement = new CurrentStaffAccessRequirement(MustChangePassword: false);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(JwtClaimNames.Subject, "not-a-guid"),
                new Claim(JwtClaimNames.StaffId, Guid.NewGuid().ToString()),
                new Claim(JwtClaimNames.MustChangePassword, "false")
            ],
            authenticationType: "Test",
            nameType: JwtClaimNames.Subject,
            roleType: JwtClaimNames.Role));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new CurrentStaffAccessHandler(checker);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.False(checker.WasCalled);
    }

    private static ClaimsPrincipal CreatePrincipal(bool mustChangePassword)
    {
        var identityUserId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
        [
            new Claim(JwtClaimNames.Subject, identityUserId.ToString()),
            new Claim(JwtClaimNames.StaffId, staffId.ToString()),
            new Claim(
                JwtClaimNames.MustChangePassword,
                mustChangePassword ? "true" : "false",
                ClaimValueTypes.Boolean)
        ],
        authenticationType: "Test",
        nameType: JwtClaimNames.Subject,
        roleType: JwtClaimNames.Role);

        return new ClaimsPrincipal(identity);
    }

    private sealed class RecordingStaffAccessChecker(bool isActive) : IStaffAccessChecker
    {
        public bool WasCalled { get; private set; }

        public Task<bool> IsActiveAsync(
            Guid identityUserId,
            Guid staffId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(isActive);
        }
    }
}
