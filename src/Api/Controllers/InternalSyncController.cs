using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/internal")]
    [AllowAnonymous]
    public class InternalSyncController : ControllerBase
    {
        private readonly ApiDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InternalSyncController> _logger;

        public InternalSyncController(
            ApiDbContext context,
            IConfiguration configuration,
            ILogger<InternalSyncController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public class RestoreRequest
        {
            public List<string>? Statements { get; set; }
            public string? RawSql { get; set; }
        }

        [HttpPost("d1-restore")]
        public async Task<IActionResult> RestoreFromD1([FromBody] RestoreRequest request)
        {
            var expectedKey = _configuration["SECRET_KEY"]
                ?? _configuration["SECRET:key"]
                ?? _configuration["SECRET__key"]
                ?? Environment.GetEnvironmentVariable("SECRET_KEY")
                ?? Environment.GetEnvironmentVariable("SECRET__key");

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError("Internal sync endpoint called but SECRET_KEY is not configured.");
                return StatusCode(503, new { error = "Internal sync is unavailable: SECRET_KEY is not configured." });
            }

            var providedKey = Request.Headers["x-internal-key"].ToString();
            if (string.IsNullOrEmpty(providedKey))
            {
                return Unauthorized(new { error = "Invalid internal sync key" });
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);

            if (expectedBytes.Length != providedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                return Unauthorized(new { error = "Invalid internal sync key" });
            }

            int executedCount = 0;
            var statements = new List<string>();

            if (request.Statements != null && request.Statements.Count > 0)
            {
                statements.AddRange(request.Statements);
            }
            else if (!string.IsNullOrWhiteSpace(request.RawSql))
            {
                var parts = request.RawSql.Split(new[] { ";\n", ";\r\n", ";" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        statements.Add(trimmed + ";");
                    }
                }
            }

            if (statements.Count == 0)
            {
                return Ok(new { restored = 0, message = "No statements to execute." });
            }

            try
            {
                await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
                try
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    foreach (var stmt in statements)
                    {
                        try
                        {
                            await _context.Database.ExecuteSqlRawAsync(stmt);
                            executedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to execute restore statement: {Sql}", stmt);
                        }
                    }
                    await transaction.CommitAsync();
                }
                finally
                {
                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to re-enable foreign keys after restore.");
                    }
                }

                _logger.LogInformation("Restored {Count} statements from Cloudflare D1 into local SQLite.", executedCount);
                return Ok(new { restored = executedCount, total = statements.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed restoring data from Cloudflare D1.");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
