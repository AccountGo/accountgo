using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AccountGoWeb.Models.Account
{
    public class LoginResponseModel
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }
}