using System;
using System.Collections.Generic;
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
            var expectedKey = _configuration["SECRET__key"] ?? Environment.GetEnvironmentVariable("SECRET__key") ?? "1618184c2fea71c86669fa7d6611a55db32e5ac7b238f235b1bbbbdb14c2a9be";
            var providedKey = Request.Headers["x-internal-key"].ToString();

            if (string.IsNullOrEmpty(providedKey) || providedKey != expectedKey)
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
