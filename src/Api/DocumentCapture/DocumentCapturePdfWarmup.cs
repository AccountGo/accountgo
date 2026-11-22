// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures;

internal sealed class DocumentCapturePdfWarmup : BackgroundService
{
    // Explicit LF keeps the cross-reference offsets identical on every platform.
    private static ReadOnlySpan<byte> SamplePdf => "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>\nendobj\nxref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n203\n%%EOF\n"u8;

    private readonly IHostApplicationLifetime _lifetime;
    private readonly IOptions<DocumentCaptureOptions> _options;
    private readonly ILogger<DocumentCapturePdfWarmup> _logger;
    private readonly Func<int, CancellationToken, Task<DocumentCapturePdfValidationResult>> _warmParser;

    public DocumentCapturePdfWarmup(IHostApplicationLifetime lifetime, IOptions<DocumentCaptureOptions> options,
        ILogger<DocumentCapturePdfWarmup> logger)
        : this(lifetime, options, logger, WarmParserAsync)
    {
    }

    internal DocumentCapturePdfWarmup(IHostApplicationLifetime lifetime, IOptions<DocumentCaptureOptions> options,
        ILogger<DocumentCapturePdfWarmup> logger,
        Func<int, CancellationToken, Task<DocumentCapturePdfValidationResult>> warmParser)
    {
        _lifetime = lifetime;
        _options = options;
        _logger = logger;
        _warmParser = warmParser;
    }

    // Only the embedded fixture gets more time to absorb cold assembly loading and JIT.
    internal static Task<DocumentCapturePdfValidationResult> WarmParserAsync(int parseSlots, CancellationToken cancellationToken) =>
        DocumentCapturePdfValidator.ValidateWithDeadlineAsync(SamplePdf.ToArray(), parseSlots, TimeProvider.System,
            TimeSpan.FromSeconds(15), cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
            return;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
            var result = await _warmParser(_options.Value.PdfParseSlots, stoppingToken)
                .ConfigureAwait(false);
            if (!result.IsValid)
                _logger.LogWarning("Document capture PDF parser warm-up did not complete successfully.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _logger.LogWarning("Document capture PDF parser warm-up did not complete successfully.");
        }
    }
}
