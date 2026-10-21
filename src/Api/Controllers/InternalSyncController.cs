using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
            public List<string>? TablesToClear { get; set; }
        }

        [HttpGet("build")]
        public IActionResult GetBuildInfo()
        {
            if (!InternalAuthHelper.ValidateInternalKey(_configuration, Request.Headers, out var status, out var message))
            {
                if (status == 503)
                {
                    _logger.LogError("Internal build endpoint called but SECRET_KEY is not configured.");
                    return StatusCode(503, new { error = message });
                }
                return Unauthorized(new { error = message });
            }

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

        [HttpPost("d1-restore")]
        public async Task<IActionResult> RestoreFromD1([FromBody] RestoreRequest request)
        {
            if (!InternalAuthHelper.ValidateInternalKey(_configuration, Request.Headers, out var status, out var message))
            {
                if (status == 503)
                {
                    _logger.LogError("Internal sync endpoint called but SECRET_KEY is not configured.");
                    return StatusCode(503, new { error = message });
                }
                return Unauthorized(new { error = message });
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

            bool hasTablesToClear = request.TablesToClear != null && request.TablesToClear.Count > 0;
            if (statements.Count == 0 && !hasTablesToClear)
            {
                return Ok(new { restored = 0, total = 0, message = "No operations to execute." });
            }

            var connection = _context.Database.GetDbConnection();
            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                await connection.OpenAsync();
            }

            try
            {
                using (var pragmaOffCmd = connection.CreateCommand())
                {
                    pragmaOffCmd.CommandText = "PRAGMA foreign_keys=OFF;";
                    await pragmaOffCmd.ExecuteNonQueryAsync();
                }

                try
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    var dbTx = transaction.GetDbTransaction();
                    try
                    {
                        if (hasTablesToClear)
                        {
                            foreach (var table in request.TablesToClear!)
                            {
                                if (!string.IsNullOrWhiteSpace(table) && System.Text.RegularExpressions.Regex.IsMatch(table, @"^[a-zA-Z0-9_]+$"))
                                {
                                    using var delCmd = connection.CreateCommand();
                                    delCmd.Transaction = dbTx;
                                    delCmd.CommandText = $"DELETE FROM \"{table}\";";
                                    await delCmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        foreach (var stmt in statements)
                        {
                            using var cmd = connection.CreateCommand();
                            cmd.Transaction = dbTx;
                            cmd.CommandText = stmt;
                            await cmd.ExecuteNonQueryAsync();
                            executedCount++;
                        }

                        await transaction.CommitAsync();

                        _logger.LogInformation("Restored {Count} statements from Cloudflare D1 into local SQLite.", executedCount);
                        return Ok(new { restored = executedCount, total = statements.Count });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "Failed executing restore statement. Transaction rolled back.");
                        return StatusCode(500, new
                        {
                            error = "Restore failed: statement execution threw an exception.",
                            details = ex.Message,
                            executedBeforeFailure = executedCount,
                            total = statements.Count
                        });
                    }
                }
                finally
                {
                    try
                    {
                        using var pragmaOnCmd = connection.CreateCommand();
                        pragmaOnCmd.CommandText = "PRAGMA foreign_keys=ON;";
                        await pragmaOnCmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to re-enable foreign keys after restore.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed restoring data from Cloudflare D1.");
                return StatusCode(500, new { error = ex.Message });
            }
            finally
            {
                if (wasClosed)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
