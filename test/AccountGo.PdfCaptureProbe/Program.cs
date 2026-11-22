using System.Diagnostics;
using System.Text.Json;
using Api.DocumentCaptures;

namespace AccountGo.PdfCaptureProbe;

internal static class Probe
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--warm"))
            return 2;

        var bytes = await File.ReadAllBytesAsync(args[0]);
        if (args.Length == 2)
        {
            var startupTimer = Stopwatch.StartNew();
            var warmup = await DocumentCapturePdfWarmup.WarmParserAsync(parseSlots: 1, CancellationToken.None);
            Console.Error.WriteLine($"Warm-up verdict: {warmup.ErrorCode ?? "valid"} in {startupTimer.Elapsed.TotalMilliseconds:F1} ms");
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var peakWorkingSetBefore = process.PeakWorkingSet64;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        Console.WriteLine($"READY|{workingSetBefore}");
        Console.Out.Flush();
        var stopwatch = Stopwatch.StartNew();
        var validation = await DocumentCapturePdfValidator.ValidateAsync(bytes, parseSlots: 1);
        stopwatch.Stop();
        process.Refresh();

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            validation.IsValid,
            validation.PageCount,
            validation.ErrorCode,
            InputBytes = bytes.Length,
            AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
            WorkingSetGrowthBytes = Math.Max(0, process.WorkingSet64 - workingSetBefore),
            PeakWorkingSetGrowthBytes = Math.Max(0, process.PeakWorkingSet64 - peakWorkingSetBefore),
            ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
        }));
        return 0;
    }
}
