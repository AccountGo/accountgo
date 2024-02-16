using Dto.Authentication;
using System.Threading.Tasks;
using static Dto.Response.ServiceResponses;

namespace Dto.Contracts
{
    public interface IUserAccount
    {
        Task<GeneralResponse> CreateAccount(UserDTO userDTO);
        Task<LoginResponse> LoginAccount(LoginDTO loginDTO);
    }
}
