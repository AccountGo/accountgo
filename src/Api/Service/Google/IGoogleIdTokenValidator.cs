using System.Threading;
using System.Threading.Tasks;

namespace Api.Service.Google;

public interface IGoogleIdTokenValidator
{
    Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct = default);
}
