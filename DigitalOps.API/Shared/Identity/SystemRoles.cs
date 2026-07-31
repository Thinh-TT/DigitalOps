namespace DigitalOps.API.Shared.Identity;

public static class SystemRoles
{
    public const string Administrator = nameof(Administrator);

    public const string Clerk = nameof(Clerk);

    public const string Drafter = nameof(Drafter);

    public const string Leader = nameof(Leader);

    public static readonly IReadOnlyList<string> Ordered =
        [Administrator, Clerk, Drafter, Leader];

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        Ordered,
        StringComparer.Ordinal);
}
