using System.Collections.Generic;

namespace Dto.Response
{
    public class ServiceResponses
    {
        public class CreatedResponse : GeneralResponse
        {
            public CreatedResponse()
            {
                
            }
            public CreatedResponse(bool flag, string id, string message):base(flag, message)
            {
                Id = id;   
            }
            /// <summary>
            /// Newly created Id
            /// </summary>
            public string Id {  get; set;}  
        }
  

        public  class LoginResponse : GeneralResponse
        {
            public LoginResponse() { }
            public LoginResponse(bool flag, string token, string message):base(flag, message)
            {
                Token = token;
            }
            public string Token { get; set;}
        }

        public class GeneralResponse : ErrorResponse
        {
            public GeneralResponse()
            {
                
            }
            public GeneralResponse(bool flag, string message)
            {
                Flag = flag;
                Message = message;
            }
            public bool Flag { get; set; }
            public string Message { get; set; }
        }
        public abstract class ErrorResponse
        {
            public List<string> Errors { get; set; } = new();
        }
    }
}
