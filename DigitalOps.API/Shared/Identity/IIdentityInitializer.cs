namespace DigitalOps.API.Shared.Identity;

public interface IIdentityInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
