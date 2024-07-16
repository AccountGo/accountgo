namespace Api.Service
{
    public interface IAuthenticationService
    {
        Task<bool> ValidateUser(dynamic loginViewModel);
        Task<string> CreateToken();
    }
}
