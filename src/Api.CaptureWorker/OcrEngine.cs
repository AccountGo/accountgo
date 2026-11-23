// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Parsing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace AccountGo.CaptureWorker;

internal sealed class OcrEngine(string modelDirectory, int threads, bool memoryArena) : IDisposable
{
    private const int DetectionLongEdge = 960;
    private InferenceSession? _detection;
    private InferenceSession? _recognition;
    private readonly string[] _dictionary = LoadDictionary(modelDirectory);

    private InferenceSession Detection => _detection ??= CreateSession(CaptureWorkerModels.Detection);
    private InferenceSession Recognition => _recognition ??= CreateSession(CaptureWorkerModels.Recognition);

    private InferenceSession CreateSession(string file)
    {
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new SessionOptions
        {
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            EnableCpuMemArena = memoryArena,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        return new InferenceSession(Path.Combine(modelDirectory, file), options);
    }

    private static string[] LoadDictionary(string directory)
    {
        var alphabet = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Path.Combine(directory, CaptureWorkerModels.Dictionary)));
        if (alphabet is null || alphabet.Length != 436)
            throw new InvalidOperationException();
        return ["", .. alphabet, " "];
    }

    internal IReadOnlyList<TextLine> Read(SKBitmap source, int page)
    {
        using var bitmap = source.Copy(SKColorType.Rgba8888) ?? throw new InvalidOperationException();
        var boxes = Detect(bitmap);
        var texts = Recognize(bitmap, boxes);
        var entries = new List<TextLine>(boxes.Count);
        for (var index = 0; index < boxes.Count; index++)
        {
            var text = texts[index];
            if (string.IsNullOrEmpty(text))
                continue;
            var box = boxes[index];
            var halfWidth = (Math.Abs(box.Cos) * box.W + Math.Abs(box.Sin) * box.H) / 2;
            entries.Add(new TextLine(page, text, Math.Max(0, box.Cx - halfWidth),
                Math.Max(0, box.Cy - box.H / 2), Math.Min(bitmap.Width, box.Cx + halfWidth),
                Math.Min(bitmap.Height, box.Cy + box.H / 2), TextLineSource.Ocr));
        }
        return GroupLines(entries);
    }

    private List<RBox> Detect(SKBitmap img)
    {
        int ow = img.Width, oh = img.Height;
        var ratio = Math.Max(ow, oh) > DetectionLongEdge ? (float)DetectionLongEdge / Math.Max(ow, oh) : 1f;
        int w = Math.Max(32, (int)Math.Round(ow * ratio / 32) * 32), h = Math.Max(32, (int)Math.Round(oh * ratio / 32) * 32);
        using var rs = img.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888), SKFilterQuality.Medium) ?? throw new InvalidOperationException();
        var buf = new float[3 * h * w];
        float[] mean = { 0.485f, 0.456f, 0.406f }, std = { 0.229f, 0.224f, 0.225f };
        var px = rs.GetPixelSpan();
        int plane = h * w;
        for (int i = 0; i < plane; i++)
        {
            int o = i * 4; // BGR channel order, matching the Paddle pipeline
            buf[i] = (px[o + 2] / 255f - mean[0]) / std[0];
            buf[plane + i] = (px[o + 1] / 255f - mean[1]) / std[1];
            buf[2 * plane + i] = (px[o] / 255f - mean[2]) / std[2];
        }
        var input = new DenseTensor<float>(buf, new[] { 1, 3, h, w });
        using var res = Detection.Run(new[] { NamedOnnxValue.CreateFromTensor("x", input) });
        var probT = res.First().AsTensor<float>();
        int ph = probT.Dimensions[2], pw = probT.Dimensions[3];
        var prob = probT.ToArray();
        float sx = (float)ow / pw, sy = (float)oh / ph;
        var seen = new bool[ph * pw];
        var boxes = new List<RBox>();
        var stack = new Stack<int>();
        var comp = new List<int>();
        for (int i = 0; i < ph * pw; i++)
        {
            if (seen[i] || prob[i] <= 0.3f) continue;
            comp.Clear();
            stack.Push(i); seen[i] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop(), cy = c / pw, cx = c % pw;
                comp.Add(c);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ny = cy + dy, nx = cx + dx;
                        if (ny < 0 || nx < 0 || ny >= ph || nx >= pw) continue;
                        int n = ny * pw + nx;
                        if (!seen[n] && prob[n] > 0.3f) { seen[n] = true; stack.Push(n); }
                    }
            }
            if (comp.Count < 9) continue;
            // principal axis of the component (original coordinates), folded to within +-45 deg of horizontal
            double mx = 0, my = 0, score = 0;
            foreach (var c in comp) { mx += (c % pw + 0.5) * sx; my += (c / pw + 0.5) * sy; score += prob[c]; }
            mx /= comp.Count; my /= comp.Count; score /= comp.Count;
            if (score < 0.6) continue;
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var c in comp)
            {
                double dx = (c % pw + 0.5) * sx - mx, dy = (c / pw + 0.5) * sy - my;
                sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
            }
            var ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            if (ang > Math.PI / 4) ang -= Math.PI / 2;
            if (ang < -Math.PI / 4) ang += Math.PI / 2;
            double cos = Math.Cos(ang), sin = Math.Sin(ang);
            double umin = double.MaxValue, umax = double.MinValue, vmin = double.MaxValue, vmax = double.MinValue;
            foreach (var c in comp)
            {
                double x = (c % pw + 0.5) * sx - mx, y = (c / pw + 0.5) * sy - my;
                double u = x * cos + y * sin, v = -x * sin + y * cos;
                umin = Math.Min(umin, u); umax = Math.Max(umax, u); vmin = Math.Min(vmin, v); vmax = Math.Max(vmax, v);
            }
            double L1 = umax - umin + sx, L2 = vmax - vmin + sy;
            if (Math.Min(L1 / sx, L2 / sy) < 3) continue;
            var d = L1 * L2 * 1.5 / (2 * (L1 + L2)); // DB unclip distance
            double ccu = (umin + umax) / 2, ccv = (vmin + vmax) / 2;
            float cxo = (float)(mx + ccu * cos - ccv * sin), cyo = (float)(my + ccu * sin + ccv * cos);
            if (boxes.Count == 2048)
                throw new InvalidDataException();
            boxes.Add(new RBox(cxo, cyo, (float)(L1 + 2 * d), (float)(L2 + 2 * d), (float)cos, (float)sin, (float)ang));
        }
        return boxes;
    }

    private string[] Recognize(SKBitmap img, List<RBox> boxes)
    {
        var results = new string[boxes.Count];
        var order = Enumerable.Range(0, boxes.Count).OrderBy(i => Ratio(boxes[i])).ToList();
        const int H = 48, batch = 8;
        for (int b = 0; b < order.Count; b += batch)
        {
            var idx = order.Skip(b).Take(batch).ToList();
            var maxRatio = Math.Max(320f / H, idx.Max(i => Ratio(boxes[i])));
            int W = (int)(H * maxRatio);
            if (W > 8192)
                throw new InvalidDataException();
            var buf = new float[idx.Count * 3 * H * W]; // zero padding
            for (int k = 0; k < idx.Count; k++)
            {
                using var c = Crop(img, boxes[idx[k]]);
                int rw = Math.Max(1, Math.Min(W, (int)Math.Ceiling(H * (float)c.Width / c.Height)));
                using var rs = c.Resize(new SKImageInfo(rw, H, SKColorType.Rgba8888), SKFilterQuality.Medium) ?? throw new InvalidOperationException();
                var px = rs.GetPixelSpan();
                int baseK = k * 3 * H * W;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < rw; x++)
                    {
                        int o = (y * rw + x) * 4, t = y * W + x;
                        buf[baseK + t] = px[o + 2] / 127.5f - 1f;
                        buf[baseK + H * W + t] = px[o + 1] / 127.5f - 1f;
                        buf[baseK + 2 * H * W + t] = px[o] / 127.5f - 1f;
                    }
            }
            var input = new DenseTensor<float>(buf, new[] { idx.Count, 3, H, W });
            using var res = Recognition.Run(new[] { NamedOnnxValue.CreateFromTensor("x", input) });
            var pt = res.First().AsTensor<float>(); // N x T x C
            int T = pt.Dimensions[1], C = pt.Dimensions[2];
            if (C != _dictionary.Length)
                throw new InvalidOperationException();
            var p = pt.ToArray();
            for (int k = 0; k < idx.Count; k++)
            {
                var sb = new StringBuilder();
                int prev = -1;
                for (int t = 0; t < T; t++)
                {
                    int off = (k * T + t) * C, best = 0;
                    float bv = float.MinValue;
                    for (int ci = 0; ci < C; ci++) if (p[off + ci] > bv) { bv = p[off + ci]; best = ci; }
                    if (best != 0 && best != prev && best < _dictionary.Length) sb.Append(_dictionary[best]);
                    prev = best;
                }
                results[idx[k]] = sb.ToString().Trim();
            }
        }
        return results;
    }


    private static float Ratio(RBox box) =>
        Math.Max(1, (int)MathF.Round(box.W)) / (float)Math.Max(1, (int)MathF.Round(box.H));

    private static SKBitmap Crop(SKBitmap image, RBox box)
    {
        var width = Math.Max(1, (int)MathF.Round(box.W));
        var height = Math.Max(1, (int)MathF.Round(box.H));
        var crop = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(crop);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };
            canvas.Clear(SKColors.White);
            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateRadians(-box.Angle);
            canvas.Translate(-box.Cx, -box.Cy);
            canvas.DrawBitmap(image, 0, 0, paint);
            return crop;
        }
        catch
        {
            crop.Dispose();
            throw;
        }
    }

    private static List<TextLine> GroupLines(List<TextLine> entries)
    {
        var lines = new List<TextLine>();
        var row = new List<TextLine>();
        var rowY = 0f;
        var rowHeight = 0f;
        foreach (var entry in entries.OrderBy(CenterY).ThenBy(entry => entry.X0))
        {
            var center = CenterY(entry);
            var height = Math.Max(1, entry.Y1 - entry.Y0);
            if (row.Count > 0 && Math.Abs(center - rowY) > 0.5f * Math.Max(height, rowHeight))
                Flush();
            if (row.Count == 0)
                (rowY, rowHeight) = (center, height);
            row.Add(entry);
        }
        Flush();
        return lines;

        void Flush()
        {
            if (row.Count == 0)
                return;
            lines.Add(new TextLine(row[0].Page, string.Join(" ", row.OrderBy(entry => entry.X0).Select(entry => entry.Text)),
                row.Min(entry => entry.X0), row.Min(entry => entry.Y0), row.Max(entry => entry.X1),
                row.Max(entry => entry.Y1), TextLineSource.Ocr));
            row.Clear();
        }
    }

    private static float CenterY(TextLine line) => (line.Y0 + line.Y1) / 2;

    public void Dispose()
    {
        _recognition?.Dispose();
        _detection?.Dispose();
        if (OrtEnv.IsCreated)
            OrtEnv.Instance().Dispose();
    }

    private readonly record struct RBox(float Cx, float Cy, float W, float H, float Cos, float Sin, float Angle);
}
