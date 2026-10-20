using System.Threading.Tasks;
using Api.Data;
using Dto.Security;

namespace Api.Service
{
    public interface IAuthenticationService
    {
        Task<Token> CreateToken(ApplicationUser user, bool populateExp);
    }
}
