using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Controllers
{
    /// <summary>Endpoints only the Worker calls (with x-internal-key): the build check before a container serves.</summary>
    [Core.Tenancy.TenantFree]
    [ApiController]
    [Route("api/internal")]
    [AllowAnonymous]
    public class InternalSyncController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InternalSyncController> _logger;

        public InternalSyncController(IConfiguration configuration, ILogger<InternalSyncController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("build")]
        public async Task<IActionResult> GetBuildInfo()
        {
            if (!EnsureInternalAccess(out var failure))
                return failure!;

            // The Worker sends traffic only after this check: hold it until start-up warm-up is done (bounded, so a
            // stuck warm-up never keeps the container out of service).
            if (Api.Service.StartupWarmup.IsEnabled(_configuration))
                await Task.WhenAny(Api.Service.StartupWarmup.Completion, Task.Delay(TimeSpan.FromSeconds(60)));

            string sha = "dev";
            var possiblePaths = new[]
            {
                "/app/BUILD_SHA",
                Path.Combine(AppContext.BaseDirectory, "BUILD_SHA"),
                Path.Combine(Directory.GetCurrentDirectory(), "BUILD_SHA"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Api", "BUILD_SHA")
            };

            foreach (var path in possiblePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    var content = System.IO.File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        sha = content;
                        break;
                    }
                }
            }

            return Ok(new { sha });
        }

        private bool EnsureInternalAccess(out IActionResult? failure)
        {
            // Cloudflare Containers: Worker containerFetch is not reliably loopback from .NET's view.
            // Container is not publicly addressed; Worker strips x-internal-key before forwarding browsers.
            if (!InternalAuthHelper.ValidateInternalKey(_configuration, Request.Headers, out var status, out var message, _logger))
            {
                if (status == 503)
                {
                    _logger.LogError("Internal endpoint called but INTERNAL_SYNC_KEY is not configured.");
                    failure = StatusCode(503, new { error = message });
                    return false;
                }

                failure = Unauthorized(new { error = message });
                return false;
            }

            failure = null;
            return true;
        }
    }
}
