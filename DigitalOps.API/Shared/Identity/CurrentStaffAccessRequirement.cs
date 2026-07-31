using Microsoft.AspNetCore.Authorization;

namespace DigitalOps.API.Shared.Identity;

public sealed record CurrentStaffAccessRequirement(bool? MustChangePassword)
    : IAuthorizationRequirement;
