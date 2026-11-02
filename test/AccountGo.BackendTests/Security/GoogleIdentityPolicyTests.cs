using System;
using System.Reflection;
using Xunit;

namespace AccountGo.BackendTests.Security;

public class GoogleIdentityPolicyTests
{
    [Theory]
    [InlineData("person@gmail.com", null, true)]
    [InlineData("person@example.com", null, false)]
    [InlineData("person@example.com", "example.com", true)]
    [InlineData("person@other.com", "example.com", false)]
    public void OnlyGoogleAuthoritativeEmailsCanBeLinkedAutomatically(string email, string? hostedDomain, bool expected)
    {
        var type = typeof(Api.Controllers.AuthController).Assembly.GetType("Api.Service.GoogleIdentityPolicy");
        Assert.NotNull(type);
        var method = type!.GetMethod("IsAuthoritativeEmail", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, new object?[] { email, hostedDomain }));
    }
}
