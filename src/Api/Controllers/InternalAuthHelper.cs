using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Api.Controllers
{
    public static class InternalAuthHelper
    {
        public static bool ValidateInternalKey(
            IConfiguration configuration,
            IHeaderDictionary headers,
            out int errorStatus,
            out string? errorMessage)
        {
            var expectedKey = configuration["SECRET_KEY"]
                ?? configuration["SECRET:key"]
                ?? configuration["SECRET__key"]
                ?? Environment.GetEnvironmentVariable("SECRET_KEY")
                ?? Environment.GetEnvironmentVariable("SECRET__key");

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                errorStatus = 503;
                errorMessage = "Internal sync is unavailable: SECRET_KEY is not configured.";
                return false;
            }

            var providedKey = headers["x-internal-key"].ToString();
            if (string.IsNullOrEmpty(providedKey))
            {
                errorStatus = 401;
                errorMessage = "Invalid internal sync key";
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);

            if (expectedBytes.Length != providedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                errorStatus = 401;
                errorMessage = "Invalid internal sync key";
                return false;
            }

            errorStatus = 200;
            errorMessage = null;
            return true;
        }
    }
}
