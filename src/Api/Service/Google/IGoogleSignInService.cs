using System.Threading.Tasks;
using Api.Data;

namespace Api.Service.Google;

public record GoogleSignInResolveResult(ApplicationUser? User, string? DenyCode);

public interface IGoogleSignInService
{
    Task<GoogleSignInResolveResult> ResolveAsync(
        GoogleIdentity identity,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null);
}
