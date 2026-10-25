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
using Newtonsoft.Json.Linq;

namespace Api.Controllers
{
    [Core.Tenancy.TenantFree]
    [ApiController]
    [Route("api/internal")]
    [AllowAnonymous]
    public class InternalSyncController : ControllerBase
    {
        private static readonly Regex SafeIdentifier = new(
            @"^[a-zA-Z_][a-zA-Z0-9_]*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const int MaxRowsPerRequest = 1000;
        private const int MaxColumnsPerRow = 128;

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

        /// <summary>
        /// Structured D1 → SQLite restore. Prefer <see cref="Rows"/> (parameterized inserts).
        /// Raw SQL strings are rejected — cell data often contains ';' / SQL keywords and broke allowlists.
        /// </summary>
        public class RestoreRequest
        {
            public List<string>? TablesToClear { get; set; }
            public List<RestoreRow>? Rows { get; set; }

            /// <summary>Deprecated. Ignored; returns 400 if non-empty.</summary>
            public List<string>? Statements { get; set; }

            /// <summary>Deprecated. Ignored; returns 400 if non-empty.</summary>
            public string? RawSql { get; set; }
        }

        public class RestoreRow
        {
            public string? Table { get; set; }
            /// <summary>Column → cell value (Newtonsoft binds JSON primitives / objects here).</summary>
            public Dictionary<string, object?>? Data { get; set; }
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

            if ((request.Statements != null && request.Statements.Count > 0) ||
                !string.IsNullOrWhiteSpace(request.RawSql))
            {
                return BadRequest(new
                {
                    error = "Raw SQL restore is disabled. Send structured rows: { tablesToClear, rows: [{ table, data }] }."
                });
            }

            var tablesToClear = request.TablesToClear ?? new List<string>();
            var rows = request.Rows ?? new List<RestoreRow>();

            if (tablesToClear.Count == 0 && rows.Count == 0)
            {
                return Ok(new { restored = 0, total = 0, message = "No operations to execute." });
            }

            if (rows.Count > MaxRowsPerRequest)
            {
                return BadRequest(new { error = $"At most {MaxRowsPerRequest} rows per request." });
            }

            foreach (var table in tablesToClear)
            {
                if (string.IsNullOrWhiteSpace(table) || !SafeIdentifier.IsMatch(table))
                {
                    return BadRequest(new { error = $"Invalid table name in tablesToClear: '{table}'." });
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (string.IsNullOrWhiteSpace(row.Table) || !SafeIdentifier.IsMatch(row.Table))
                {
                    return BadRequest(new { error = "Invalid row table name.", index = i });
                }

                if (row.Data == null || row.Data.Count == 0)
                {
                    return BadRequest(new { error = "Row data is required.", index = i });
                }

                if (row.Data.Count > MaxColumnsPerRow)
                {
                    return BadRequest(new { error = $"Row exceeds {MaxColumnsPerRow} columns.", index = i });
                }

                foreach (var col in row.Data.Keys)
                {
                    if (string.IsNullOrWhiteSpace(col) || !SafeIdentifier.IsMatch(col))
                    {
                        return BadRequest(new { error = $"Invalid column name '{col}'.", index = i });
                    }
                }
            }

            var connection = _context.Database.GetDbConnection();
            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                await connection.OpenAsync();
            }

            int restoredCount = 0;

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
                        foreach (var table in tablesToClear)
                        {
                            using var delCmd = connection.CreateCommand();
                            delCmd.Transaction = dbTx;
                            delCmd.CommandText = $"DELETE FROM \"{table}\";";
                            await delCmd.ExecuteNonQueryAsync();
                        }

                        foreach (var row in rows)
                        {
                            await InsertOrReplaceRowAsync(connection, dbTx, row.Table!, row.Data!);
                            restoredCount++;
                        }


                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "Restored {Count} parameterized rows from Cloudflare D1 into local SQLite (cleared {ClearCount} tables).",
                            restoredCount,
                            tablesToClear.Count);

                        return Ok(new { restored = restoredCount, total = rows.Count, cleared = tablesToClear.Count });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "Failed executing structured restore. Transaction rolled back.");
                        return StatusCode(500, new
                        {
                            error = "Restore failed: row execution threw an exception.",
                            details = ex.Message,
                            executedBeforeFailure = restoredCount,
                            total = rows.Count
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

        private static async Task InsertOrReplaceRowAsync(
            System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction dbTx,
            string table,
            Dictionary<string, object?> data)
        {
            var columns = data.Keys.ToList();
            var colSql = string.Join(", ", columns.Select(c => $"\"{c}\""));
            var paramSql = string.Join(", ", columns.Select((_, i) => $"$p{i}"));

            using var cmd = connection.CreateCommand();
            cmd.Transaction = dbTx;
            cmd.CommandText = $"INSERT OR REPLACE INTO \"{table}\" ({colSql}) VALUES ({paramSql});";

            for (int i = 0; i < columns.Count; i++)
            {
                var param = cmd.CreateParameter();
                param.ParameterName = $"$p{i}";
                param.Value = CellToDbValue(data[columns[i]]);
                cmd.Parameters.Add(param);
            }

            await cmd.ExecuteNonQueryAsync();
        }

        private static object CellToDbValue(object? value)
        {
            if (value == null || value is DBNull)
                return DBNull.Value;

            if (value is JValue jv)
                return CellToDbValue(jv.Value);

            if (value is JObject jo)
            {
                if (jo.TryGetValue("$b64", out var b64Token) && b64Token.Type == JTokenType.String)
                {
                    var s = b64Token.Value<string>();
                    return string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Convert.FromBase64String(s);
                }

                return jo.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (value is JArray ja)
                return ja.ToString(Newtonsoft.Json.Formatting.None);

            if (value is bool b)
                return b ? 1 : 0;

            if (value is byte[] or string or long or int or short or byte or double or float or decimal)
                return value;

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value;
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
