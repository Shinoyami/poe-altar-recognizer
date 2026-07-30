using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Tesseract;

namespace PoeAltarGuard;

public sealed class OcrWatcher : IDisposable
{
    private readonly TesseractEngine _engine;

    public sealed record Match(System.Windows.Rect Bounds, bool IsGood, string Rule, string OcrText);

    public OcrWatcher()
    {
        var dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        _engine = new TesseractEngine(dataPath, "eng");
        _engine.SetVariable("user_defined_dpi", "144");
        _engine.SetVariable("tessedit_char_whitelist",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789%,'- ");
    }

    public Task<IReadOnlyList<Match>> ScanAsync(
        System.Windows.Rect area, IReadOnlyList<string> goodRules, IReadOnlyList<string> badRules)
    {
        return Task.Run<IReadOnlyList<Match>>(() =>
        {
            using var bitmap = new Bitmap((int)area.Width, (int)area.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen((int)area.Left, (int)area.Top, 0, 0, bitmap.Size,
                    CopyPixelOperation.SourceCopy);
            return AnalyzeBitmapTiled(bitmap, goodRules, badRules, area.Left, area.Top);
        });
    }

    public IReadOnlyList<Match> AnalyzeBitmapTiled(Bitmap bitmap,
        IReadOnlyList<string> goodRules, IReadOnlyList<string> badRules,
        double screenLeft = 0, double screenTop = 0)
    {
        var textBlocks = FindModifierTextBlocks(bitmap);
        if (textBlocks.Count > 0)
        {
            var blockMatches = new List<Match>();
            foreach (var block in textBlocks)
            {
                using var crop = bitmap.Clone(block, bitmap.PixelFormat);
                blockMatches.AddRange(AnalyzeBitmap(
                    crop, goodRules, badRules, screenLeft + block.X, screenTop + block.Y));
            }
            return Deduplicate(blockMatches);
        }
        // No modifier-colored text means there is nothing useful to OCR.
        return Array.Empty<Match>();
    }

    private static IReadOnlyList<Match> Deduplicate(IEnumerable<Match> matches)
    {
        var unique = new List<Match>();
        foreach (var match in matches.OrderByDescending(m => m.Bounds.Width))
        {
            var duplicate = unique.Any(other =>
                Math.Abs(CenterX(other.Bounds) - CenterX(match.Bounds)) < 40 &&
                Math.Abs(CenterY(other.Bounds) - CenterY(match.Bounds)) < 25);
            if (!duplicate) unique.Add(match);
        }
        return unique;
    }

    private static List<Rectangle> FindModifierTextBlocks(Bitmap bitmap)
    {
        var source = bitmap;
        Bitmap? converted = null;
        if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
        {
            converted = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                PixelFormat.Format32bppArgb);
            source = converted;
        }
        var lockRect = new Rectangle(0, 0, source.Width, source.Height);
        var data = source.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[Math.Abs(data.Stride) * source.Height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        source.UnlockBits(data);

        bool IsFrameColor(int x, int y)
        {
            var offset = y * data.Stride + x * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            return ColorDistanceSquared(r, g, b, 0xE3, 0x6B, 0x01) <= 60 * 60;
        }

        bool IsModifierColor(int x, int y)
        {
            var offset = y * data.Stride + x * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            return Math.Min(
                ColorDistanceSquared(r, g, b, 0x9F, 0xB9, 0xCA),
                ColorDistanceSquared(r, g, b, 0x97, 0x97, 0xEB)) <= 70 * 70;
        }

        var segments = new List<(int Y, int Left, int Right)>();
        for (var y = 0; y < source.Height; y++)
        {
            var start = -1;
            var lastFramePixel = -1;
            var framePixelCount = 0;
            for (var x = 0; x < source.Width; x++)
            {
                if (IsFrameColor(x, y))
                {
                    if (start < 0) start = x;
                    lastFramePixel = x;
                    framePixelCount++;
                }
                if (start >= 0 && (x - lastFramePixel > 8 || x == source.Width - 1))
                {
                    var width = lastFramePixel - start + 1;
                    if (width >= 300 && framePixelCount >= width * 0.72)
                        segments.Add((y, start, lastFramePixel));
                    start = -1;
                    lastFramePixel = -1;
                    framePixelCount = 0;
                }
            }
        }

        var borderGroups = new List<List<(int Y, int Left, int Right)>>();
        foreach (var segment in segments)
        {
            var group = borderGroups.FirstOrDefault(g =>
                segment.Y - g.Max(s => s.Y) <= 2 &&
                Math.Abs(segment.Left - g.Average(s => s.Left)) < 25 &&
                Math.Abs(segment.Right - g.Average(s => s.Right)) < 25);
            if (group is null)
            {
                group = new List<(int Y, int Left, int Right)>();
                borderGroups.Add(group);
            }
            group.Add(segment);
        }

        var borders = borderGroups.Select(group => (
            Y: (int)Math.Round(group.Average(s => s.Y)),
            Left: (int)Math.Round(group.Average(s => s.Left)),
            Right: (int)Math.Round(group.Average(s => s.Right)))).ToList();

        var boxes = new List<(Rectangle Rect, int TextPixels)>();
        for (var i = 0; i < borders.Count; i++)
        for (var j = i + 1; j < borders.Count; j++)
        {
            var top = borders[i];
            var bottom = borders[j];
            var height = bottom.Y - top.Y;
            if (height is < 40 or > 250) continue;
            if (Math.Abs(top.Left - bottom.Left) > 40 ||
                Math.Abs(top.Right - bottom.Right) > 40) continue;
            var left = Math.Min(top.Left, bottom.Left);
            var right = Math.Max(top.Right, bottom.Right);
            var box = new Rectangle(left, top.Y, right - left + 1, height + 1);

            var verticalFrameHits = 0;
            var verticalSamples = 0;
            for (var y = box.Top; y < box.Bottom; y += 3)
            {
                verticalSamples += 2;
                if (Enumerable.Range(Math.Max(0, box.Left - 4),
                        Math.Min(12, source.Width - Math.Max(0, box.Left - 4)))
                    .Any(x => IsFrameColor(x, y))) verticalFrameHits++;
                var rightStart = Math.Max(0, box.Right - 8);
                if (Enumerable.Range(rightStart, Math.Min(12, source.Width - rightStart))
                    .Any(x => IsFrameColor(x, y))) verticalFrameHits++;
            }
            if (verticalSamples == 0 || verticalFrameHits < verticalSamples * 0.25) continue;

            var textPixels = 0;
            for (var y = box.Top + 3; y < box.Bottom - 3; y += 2)
            for (var x = box.Left + 3; x < box.Right - 3; x += 2)
                if (IsModifierColor(x, y)) textPixels++;
            if (textPixels < 20) continue;
            if (!boxes.Any(existing =>
                    Math.Abs(existing.Rect.Left - box.Left) < 15 &&
                    Math.Abs(existing.Rect.Top - box.Top) < 15))
                boxes.Add((box, textPixels));
        }

        converted?.Dispose();
        return boxes.OrderByDescending(box => box.TextPixels)
            .Take(2)
            .Select(box => box.Rect)
            .ToList();
    }

    private static Bitmap CreateTextMask(Bitmap source, int scale)
    {
        using var input = source.Clone(new Rectangle(0, 0, source.Width, source.Height),
            PixelFormat.Format32bppArgb);
        var inputData = input.LockBits(new Rectangle(0, 0, input.Width, input.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var inputBytes = new byte[Math.Abs(inputData.Stride) * input.Height];
        Marshal.Copy(inputData.Scan0, inputBytes, 0, inputBytes.Length);
        input.UnlockBits(inputData);

        var output = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format32bppArgb);
        var outputData = output.LockBits(new Rectangle(0, 0, output.Width, output.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var outputBytes = new byte[Math.Abs(outputData.Stride) * output.Height];
        Array.Fill(outputBytes, (byte)255);

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var sourceOffset = y * inputData.Stride + x * 4;
            var b = inputBytes[sourceOffset];
            var g = inputBytes[sourceOffset + 1];
            var r = inputBytes[sourceOffset + 2];
            // PoE altar modifiers use #A3C0D6 and #9999EE. A tolerance keeps
            // anti-aliased edge pixels while rejecting the scene behind the panel.
            var normalTextDistance = ColorDistanceSquared(r, g, b, 0x9F, 0xB9, 0xCA);
            var specialTextDistance = ColorDistanceSquared(r, g, b, 0x97, 0x97, 0xEB);
            var isText = Math.Min(normalTextDistance, specialTextDistance) <= 70 * 70;
            if (!isText) continue;
            for (var dy = 0; dy < scale; dy++)
            for (var dx = 0; dx < scale; dx++)
            {
                var outputOffset = (y * scale + dy) * outputData.Stride + (x * scale + dx) * 4;
                outputBytes[outputOffset] = 0;
                outputBytes[outputOffset + 1] = 0;
                outputBytes[outputOffset + 2] = 0;
                outputBytes[outputOffset + 3] = 255;
            }
        }
        Marshal.Copy(outputBytes, 0, outputData.Scan0, outputBytes.Length);
        output.UnlockBits(outputData);
        return output;
    }

    private static int ColorDistanceSquared(int r, int g, int b, int targetR, int targetG, int targetB)
    {
        var dr = r - targetR;
        var dg = g - targetG;
        var db = b - targetB;
        return dr * dr + dg * dg + db * db;
    }

    public IReadOnlyList<Match> AnalyzeBitmap(Bitmap bitmap,
        IReadOnlyList<string> goodRules, IReadOnlyList<string> badRules,
        double screenLeft = 0, double screenTop = 0)
    {
        const int ocrScale = 1;
        using var cleaned = CreateTextMask(bitmap, ocrScale);
        using var pix = PixConverter.ToPix(cleaned);
        using var page = _engine.Process(pix, PageSegMode.SingleBlock);
        var lines = new List<(string Text, Rect Box)>();
        using var iterator = page.GetIterator();
        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim() ?? "";
            if (text.Length > 0 && iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var box))
                lines.Add((text, box));
        } while (iterator.Next(PageIteratorLevel.TextLine));

        var matches = new List<Match>();
        foreach (var line in lines)
        {
            // Bad takes priority if a line happens to match both lists.
            var bad = badRules.FirstOrDefault(rule => RuleMatches(rule, line.Text));
            var good = bad is null ? goodRules.FirstOrDefault(rule => RuleMatches(rule, line.Text)) : null;
            var rule = bad ?? good;
            if (rule is null) continue;
            const int pad = 8;
            matches.Add(new Match(
                new System.Windows.Rect(screenLeft + line.Box.X1 / ocrScale - pad,
                    screenTop + line.Box.Y1 / ocrScale - pad,
                    line.Box.Width / ocrScale + pad * 2, line.Box.Height / ocrScale + pad * 2),
                bad is null, rule, line.Text));
        }
        return matches;
    }

    private static bool RuleMatches(string rule, string ocrLine)
    {
        var wanted = Tokens(rule);
        var seen = Tokens(ocrLine);
        if (wanted.Count == 0 || seen.Count == 0) return false;
        var matched = wanted.Count(w => seen.Any(s => TokensMatch(w, s)));
        // Short rules often differ by one critical word (for example Quantity
        // versus Rarity), so every token must be present. Longer rules retain
        // some tolerance for OCR mistakes.
        var required = wanted.Count <= 7 ? wanted.Count : wanted.Count - 1;
        return matched >= required;
    }

    private static bool TokensMatch(string wanted, string seen)
    {
        if (wanted == seen) return true;
        if (wanted.Length >= 5 && seen.Length >= 5 &&
            wanted[..5] == seen[..5]) return true;
        var allowance = Math.Max(1, Math.Max(wanted.Length, seen.Length) / 5);
        return Math.Abs(wanted.Length - seen.Length) <= allowance &&
               LevenshteinDistance(wanted, seen) <= allowance;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static List<string> Tokens(string value) =>
        new(Regex.Replace(value,
                @"\(\s*[+-]?\d+(?:\.\d+)?\s*[-–—]\s*[+-]?\d+(?:\.\d+)?\s*\)\s*%?",
                " ", RegexOptions.CultureInvariant)
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '%', '(', ')', '-', '/' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length > 2));

    private static double CenterX(System.Windows.Rect rect) => rect.Left + rect.Width / 2;
    private static double CenterY(System.Windows.Rect rect) => rect.Top + rect.Height / 2;

    public void Dispose() => _engine.Dispose();
}
