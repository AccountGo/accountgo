using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
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
    [Core.Tenancy.TenantFree]
    [ApiController]
    [Route("api/internal")]
    [AllowAnonymous]
    public class InternalSyncController : ControllerBase
    {
        // D1 restore emits INSERT OR REPLACE / INSERT / DELETE against quoted identifiers only.
        private static readonly Regex AllowedRestoreStatement = new(
            @"^\s*(?:INSERT\s+OR\s+REPLACE\s+INTO|INSERT\s+INTO|DELETE\s+FROM)\s+""([a-zA-Z0-9_]+)""\b[\s\S]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex DangerousSql = new(
            @"\b(ATTACH|DETACH|DROP|ALTER|CREATE|PRAGMA|VACUUM|REINDEX|LOAD_EXTENSION|UNION\s+SELECT)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SafeTableName = new(
            @"^[a-zA-Z0-9_]+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
            if (!EnsureInternalAccess(out var failure))
                return failure!;

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
            if (!EnsureInternalAccess(out var failure))
                return failure!;

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
                        statements.Add(trimmed.EndsWith(';') ? trimmed : trimmed + ";");
                    }
                }
            }

            bool hasTablesToClear = request.TablesToClear != null && request.TablesToClear.Count > 0;
            if (statements.Count == 0 && !hasTablesToClear)
            {
                return Ok(new { restored = 0, total = 0, message = "No operations to execute." });
            }

            if (hasTablesToClear)
            {
                foreach (var table in request.TablesToClear!)
                {
                    if (string.IsNullOrWhiteSpace(table) || !SafeTableName.IsMatch(table))
                    {
                        return BadRequest(new { error = $"Invalid table name in tablesToClear: '{table}'." });
                    }
                }
            }

            for (int i = 0; i < statements.Count; i++)
            {
                if (!IsAllowedRestoreStatement(statements[i], out var reason))
                {
                    return BadRequest(new
                    {
                        error = "Restore statement rejected by allowlist.",
                        index = i,
                        reason
                    });
                }
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
                                using var delCmd = connection.CreateCommand();
                                delCmd.Transaction = dbTx;
                                delCmd.CommandText = $"DELETE FROM \"{table}\";";
                                await delCmd.ExecuteNonQueryAsync();
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

        private bool EnsureInternalAccess(out IActionResult? failure)
        {
            // Do not require IPAddress.IsLoopback here. On Cloudflare Containers the Worker calls
            // http://127.0.0.1:8001 via containerFetch, but the .NET process often sees a non-loopback
            // remote address — that blocked /api/internal/build + d1-restore and left the API in 503
            // "starting up" forever. The container is not publicly addressed; the Worker strips
            // x-internal-key before forwarding browser traffic. Auth is the dedicated sync key
            // (plus SQL allowlisting on restore).
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

        private static bool IsAllowedRestoreStatement(string statement, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(statement))
            {
                reason = "Empty statement.";
                return false;
            }

            var normalized = statement.Trim().TrimEnd(';').Trim();

            if (DangerousSql.IsMatch(normalized))
            {
                reason = "Statement contains disallowed SQL constructs.";
                return false;
            }

            var match = AllowedRestoreStatement.Match(normalized);
            if (!match.Success)
            {
                reason = "Only INSERT, INSERT OR REPLACE, and DELETE FROM quoted tables are allowed.";
                return false;
            }

            var tableName = match.Groups[1].Value;
            if (!SafeTableName.IsMatch(tableName))
            {
                reason = "Table name failed allowlist validation.";
                return false;
            }

            return true;
        }
    }
}
