namespace DigitalOps.API.Shared.Identity;

public static class AuthorizationPolicies
{
    public const string CurrentStaff = nameof(CurrentStaff);

    public const string BusinessAccess = nameof(BusinessAccess);

    public const string PasswordChangeRequired = nameof(PasswordChangeRequired);

    public const string Administrator = SystemRoles.Administrator;

    public const string Clerk = SystemRoles.Clerk;

    public const string Drafter = SystemRoles.Drafter;

    public const string Leader = SystemRoles.Leader;
}
