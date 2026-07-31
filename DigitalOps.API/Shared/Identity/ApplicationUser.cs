using Microsoft.AspNetCore.Identity;

namespace DigitalOps.API.Shared.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; }

    public Staff? Staff { get; set; }
}
