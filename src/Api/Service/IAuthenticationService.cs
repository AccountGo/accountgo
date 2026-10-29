using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Api.Data;
using Dto.Security;

namespace Api.Service
{
    public interface IAuthenticationService
    {
        Task<Token> CreateToken(ApplicationUser user, bool populateExp);

        Task<Token> CreateToken(
            ApplicationUser user,
            string? purpose,
            TimeSpan? lifetime = null,
            IEnumerable<string>? amr = null,
            bool? mfa = null,
            DateTimeOffset? mfaTimestamp = null,
            IEnumerable<Claim>? extraClaims = null);
    }
}
