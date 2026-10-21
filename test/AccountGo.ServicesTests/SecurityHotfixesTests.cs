using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Data;
using Api.Service;
using Dto.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Services.Security;
using Xunit;

namespace AccountGo.ServicesTests;

public class SecurityHotfixesTests
{
    private class TestController : BaseController
    {
        public string? TestGetCurrentUserName() => GetCurrentUserName();
    }

    [Fact]
    public void BaseController_IgnoresClientSuppliedUserNameHeader_WhenUnauthenticated()
    {
        // Arrange
        var controller = new TestController();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["UserName"] = "attacker@evil.com";
        // User identity is not authenticated
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = controller.TestGetCurrentUserName();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void BaseController_ReturnsAuthenticatedUserName()
    {
        // Arrange
        var controller = new TestController();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["UserName"] = "spoofed@evil.com";
        var claims = new[] { new Claim(ClaimTypes.Name, "legit@accountgo.ph") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = controller.TestGetCurrentUserName();

        // Assert
        Assert.Equal("legit@accountgo.ph", result);
    }

    [Fact]
    public async Task InternalSyncController_Returns503_WhenSecretKeyMissing()
    {
        var prevKey = Environment.GetEnvironmentVariable("SECRET_KEY");
        var prevKeyAlt = Environment.GetEnvironmentVariable("SECRET__key");
        try
        {
            Environment.SetEnvironmentVariable("SECRET_KEY", null);
            Environment.SetEnvironmentVariable("SECRET__key", null);

            // Arrange
            var options = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite("DataSource=:memory:")
                .Options;
            using var context = new ApiDbContext(options);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var logger = new Mock<ILogger<InternalSyncController>>();

            var controller = new InternalSyncController(context, config, logger.Object);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["x-internal-key"] = "some-key";
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            // Act
            var result = await controller.RestoreFromD1(new InternalSyncController.RestoreRequest());

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(503, statusResult.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SECRET_KEY", prevKey);
            Environment.SetEnvironmentVariable("SECRET__key", prevKeyAlt);
        }
    }

    [Fact]
    public async Task InternalSyncController_Returns401_WhenKeyInvalid()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new ApiDbContext(options);

        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = secret
        }).Build();
        var logger = new Mock<ILogger<InternalSyncController>>();

        var controller = new InternalSyncController(context, config, logger.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-internal-key"] = "wrong-key";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await controller.RestoreFromD1(new InternalSyncController.RestoreRequest());

        // Assert
        var statusResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, statusResult.StatusCode);
    }

    [Fact]
    public async Task EmailOtpService_DoesNotReturnDevCode_InProduction()
    {
        // Arrange
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var user = new ApplicationUser { Email = "user@accountgo.ph", UserName = "user@accountgo.ph" };
        userManager.Setup(m => m.FindByEmailAsync("user@accountgo.ph")).ReturnsAsync(user);
        userManager.Setup(m => m.GetAuthenticationTokenAsync(user, "EmailOtp", "Default")).ReturnsAsync((string?)null);
        userManager.Setup(m => m.SetAuthenticationTokenAsync(user, "EmailOtp", "Default", It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

        var idOptions = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var idContext = new ApplicationIdentityDbContext(idOptions);

        var emailSender = new DevConsoleEmailSender(new Mock<ILogger<DevConsoleEmailSender>>().Object);
        var authService = new Mock<IAuthenticationService>();
        var secService = new Mock<ISecurityService>();

        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = secret,
            ["Email:Provider"] = "Console"
        }).Build();

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var logger = new Mock<ILogger<EmailOtpService>>();

        var service = new EmailOtpService(
            userManager.Object,
            idContext,
            emailSender,
            authService.Object,
            secService.Object,
            config,
            env.Object,
            logger.Object);

        // Act
        var result = await service.SendOtpAsync("user@accountgo.ph", "127.0.0.1");

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.DevCode);
    }

    [Fact]
    public async Task EmailOtpService_SendWelcomeOtpAsync_DoesNotReturnDevCode_InProduction()
    {
        // Arrange
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var user = new ApplicationUser { Email = "newuser@accountgo.ph", UserName = "newuser@accountgo.ph" };
        userManager.Setup(m => m.GetAuthenticationTokenAsync(user, "EmailOtp", "Default")).ReturnsAsync((string?)null);
        userManager.Setup(m => m.SetAuthenticationTokenAsync(user, "EmailOtp", "Default", It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

        var idOptions = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var idContext = new ApplicationIdentityDbContext(idOptions);

        var emailSender = new DevConsoleEmailSender(new Mock<ILogger<DevConsoleEmailSender>>().Object);
        var authService = new Mock<IAuthenticationService>();
        var secService = new Mock<ISecurityService>();

        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = secret,
            ["Email:Provider"] = "Console"
        }).Build();

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var logger = new Mock<ILogger<EmailOtpService>>();

        var service = new EmailOtpService(
            userManager.Object,
            idContext,
            emailSender,
            authService.Object,
            secService.Object,
            config,
            env.Object,
            logger.Object);

        // Act
        var result = await service.SendWelcomeOtpAsync(user, "New", "Acme Corp", "127.0.0.1");

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.DevCode);
    }

    [Fact]
    public async Task EmailOtpService_ReturnsDevCode_InDevelopment_WithDevConsoleEmailSender()
    {
        // Arrange
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var user = new ApplicationUser { Email = "dev@accountgo.ph", UserName = "dev@accountgo.ph" };
        userManager.Setup(m => m.FindByEmailAsync("dev@accountgo.ph")).ReturnsAsync(user);
        userManager.Setup(m => m.GetAuthenticationTokenAsync(user, "EmailOtp", "Default")).ReturnsAsync((string?)null);
        userManager.Setup(m => m.SetAuthenticationTokenAsync(user, "EmailOtp", "Default", It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

        var idOptions = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var idContext = new ApplicationIdentityDbContext(idOptions);

        var emailSender = new DevConsoleEmailSender(new Mock<ILogger<DevConsoleEmailSender>>().Object);
        var authService = new Mock<IAuthenticationService>();
        var secService = new Mock<ISecurityService>();

        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = secret,
            ["Email:Provider"] = "Console"
        }).Build();

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var logger = new Mock<ILogger<EmailOtpService>>();

        var service = new EmailOtpService(
            userManager.Object,
            idContext,
            emailSender,
            authService.Object,
            secService.Object,
            config,
            env.Object,
            logger.Object);

        // Act: SendOtpAsync
        var otpResult = await service.SendOtpAsync("dev@accountgo.ph", "127.0.0.1");

        // Assert: SendOtpAsync in Dev provides 6-digit devCode
        Assert.True(otpResult.Success);
        Assert.NotNull(otpResult.DevCode);
        Assert.Equal(6, otpResult.DevCode!.Length);

        // Act: SendWelcomeOtpAsync
        var welcomeResult = await service.SendWelcomeOtpAsync(user, "Dev", "Dev Workspace", "127.0.0.1");

        // Assert: SendWelcomeOtpAsync in Dev provides 6-digit devCode
        Assert.True(welcomeResult.Success);
        Assert.NotNull(welcomeResult.DevCode);
        Assert.Equal(6, welcomeResult.DevCode!.Length);
    }
}
