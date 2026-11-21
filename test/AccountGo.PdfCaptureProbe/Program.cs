using System.Diagnostics;
using System.Text.Json;
using Api.DocumentCaptures;

namespace AccountGo.PdfCaptureProbe;

internal static class Probe
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
            return 2;

        var bytes = await File.ReadAllBytesAsync(args[0]);
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
