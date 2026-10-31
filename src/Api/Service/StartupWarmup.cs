using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    /// <summary>
    /// Runs the heavy request paths once right after start-up, before the container takes traffic. On the smallest
    /// container (1/16 vCPU) the first call of each path costs seconds of CPU (JIT, EF query compilation, crypto
    /// set-up), and while that runs every other request waits; a cold container in front of real traffic stalled for
    /// minutes. The Worker's build check (<c>GET /api/internal/build</c>) waits for <see cref="Completion"/>, so no
    /// request reaches a cold app. Read-only: nothing is written to the database. On when <c>WARMUP_ON_START</c> is true.
    /// </summary>
    public sealed class StartupWarmup : BackgroundService
    {
        public const string EnabledKey = "WARMUP_ON_START";

        private static readonly TaskCompletionSource CompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when warm-up has finished, failed or was not enabled.</summary>
        public static Task Completion => CompletionSource.Task;

        private readonly IServiceProvider _services;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StartupWarmup> _logger;

        public StartupWarmup(IServiceProvider services, IHostApplicationLifetime lifetime,
            IConfiguration configuration, ILogger<StartupWarmup> logger)
        {
            _services = services;
            _lifetime = lifetime;
            _configuration = configuration;
            _logger = logger;
        }

        public static bool IsEnabled(IConfiguration configuration) =>
            string.Equals(configuration[EnabledKey], "true", StringComparison.OrdinalIgnoreCase);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!IsEnabled(_configuration))
            {
                CompletionSource.TrySetResult();
                return;
            }

            try
            {
                // The loopback requests need Kestrel listening.
                await WaitForStartAsync(stoppingToken);
                var total = Stopwatch.StartNew();
                await StepAsync("services", () => WarmServicesAsync(stoppingToken));
                await StepAsync("http", () => WarmHttpAsync(stoppingToken));
                _logger.LogWarning("Warm-up finished in {Ms} ms", total.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                CompletionSource.TrySetResult();
            }
        }

        private Task WaitForStartAsync(CancellationToken stoppingToken)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            stoppingToken.Register(() => started.TrySetCanceled());
            return started.Task;
        }

        private async Task StepAsync(string name, Func<Task> step)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await step();
                _logger.LogWarning("Warm-up {Step}: {Ms} ms", name, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Warm-up only speeds things up; a failed step must never stop the app from serving.
                _logger.LogWarning(ex, "Warm-up {Step} failed after {Ms} ms", name, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>The session refresh, workspace bootstrap and entitlements paths, without writes.</summary>
        private async Task WarmServicesAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;

            // Session refresh: token-hash lookup, user lookup, JWT signing (in-memory user, nothing stored).
            var identity = sp.GetRequiredService<ApplicationIdentityDbContext>();
            var missing = "warmup-" + Guid.NewGuid().ToString("N");
            await identity.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == missing, ct);
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            await users.FindByIdAsync(missing);
            await users.FindByEmailAsync(missing + "@warmup.invalid");
            var auth = sp.GetRequiredService<IAuthenticationService>();
            await auth.CreateToken(new ApplicationUser { Id = missing, UserName = missing, Email = missing + "@warmup.invalid" },
                populateExp: false);

            var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("AccountGo.Warmup");
            protector.Unprotect(protector.Protect("warmup"));

            // Workspace bootstrap and entitlements for one workspace (organizations/current, entitlements, billing).
            var db = sp.GetRequiredService<ApiDbContext>();
            await db.CompanyMembers.AsNoTracking().Where(m => m.UserId == missing).ToListAsync(ct);
            var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.Slug }).FirstOrDefaultAsync(ct);
            if (company != null)
            {
                var tenant = sp.GetRequiredService<ITenantContext>();
                using (tenant.Impersonate(company.Id, company.Slug, Core.Domain.CompanyMemberRole.Owner))
                {
                    sp.GetRequiredService<IEntitlementService>().Get(company.Id);
                }
            }
        }

        /// <summary>Anonymous loopback requests: routing, auth handlers, MVC, filters and serialization.</summary>
        private async Task WarmHttpAsync(CancellationToken ct)
        {
            var baseUrl = LoopbackUrl(_configuration["ASPNETCORE_URLS"]);
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
            foreach (var (method, path) in new[]
            {
                (HttpMethod.Get, "/health"),
                (HttpMethod.Post, "/api/auth/refresh"),
                (HttpMethod.Get, "/api/plans"),
                (HttpMethod.Get, "/api/organizations"),
                (HttpMethod.Get, "/api/entitlements"),
            })
            {
                using var request = new HttpRequestMessage(method, path);
                using var response = await http.SendAsync(request, ct);
            }
        }

        /// <summary>First ASPNETCORE_URLS entry with a wildcard host replaced by 127.0.0.1.</summary>
        public static string LoopbackUrl(string? urls)
        {
            var first = (urls ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "http://127.0.0.1:8001";
            return first.Replace("://+", "://127.0.0.1").Replace("://*", "://127.0.0.1")
                .Replace("://0.0.0.0", "://127.0.0.1").Replace("://[::]", "://127.0.0.1");
        }
    }
}
