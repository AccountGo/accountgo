using Dto.Security;

namespace Api.Service
{
    public interface IAuthenticationService
    {
        Task<bool> ValidateUser(dynamic loginViewModel);
        Task<Token> CreateToken(bool populateExp);
        Task<Token> RefreshToken(Token tokenDto);
    }
}
