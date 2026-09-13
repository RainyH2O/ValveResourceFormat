using System.Text;
using SkiaSharp;

namespace ValveResourceFormat.Graphs;

/// <summary>Measures and draws graph text with system font fallback for missing glyphs.</summary>
internal sealed class GraphFont
{
    private static readonly string[] PreferredLanguages = [];

    private readonly SKFont primaryFont;
    private readonly Dictionary<int, SKFont> fontsByCodePoint = [];
    private readonly Dictionary<string, SKFont> fallbackFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Lock fallbackLock = new();

    public SKFontMetrics Metrics => primaryFont.Metrics;

    public GraphFont(SKFont primaryFont)
    {
        this.primaryFont = primaryFont;
    }

    public float MeasureText(string text)
    {
        if (!NeedsFallback(text))
        {
            return primaryFont.MeasureText(text);
        }

        var width = 0f;

        foreach (var run in BuildRuns(text, ResolveFont))
        {
            width += run.Value.MeasureText(text.AsSpan(run.Start, run.Length));
        }

        return width;
    }

    public void DrawText(SKCanvas canvas, string text, float x, float y, SKTextAlign align, SKPaint paint)
    {
        if (!NeedsFallback(text))
        {
            canvas.DrawText(text, x, y, align, primaryFont, paint);
            return;
        }

        if (align != SKTextAlign.Left)
        {
            var width = MeasureText(text);
            x -= align == SKTextAlign.Center ? width * 0.5f : width;
        }

        foreach (var run in BuildRuns(text, ResolveFont))
        {
            var runText = text.Substring(run.Start, run.Length);
            canvas.DrawText(runText, x, y, SKTextAlign.Left, run.Value, paint);
            x += run.Value.MeasureText(runText);
        }
    }

    internal bool NeedsFallback(string text) => !primaryFont.ContainsGlyphs(text);

    private SKFont ResolveFont(Rune rune)
    {
        lock (fallbackLock)
        {
            if (fontsByCodePoint.TryGetValue(rune.Value, out var cached))
            {
                return cached;
            }

            if (primaryFont.ContainsGlyphs(rune.ToString()))
            {
                fontsByCodePoint[rune.Value] = primaryFont;
                return primaryFont;
            }

            using var typeface = SKFontManager.Default.MatchCharacter(
                primaryFont.Typeface.FamilyName,
                primaryFont.Typeface.FontStyle,
                PreferredLanguages,
                rune.Value);

            if (typeface == null)
            {
                fontsByCodePoint[rune.Value] = primaryFont;
                return primaryFont;
            }

            if (!fallbackFonts.TryGetValue(typeface.FamilyName, out var fallback))
            {
                fallback = Prepare(typeface.ToFont(primaryFont.Size, primaryFont.ScaleX, primaryFont.SkewX));
                fallbackFonts[typeface.FamilyName] = fallback;
            }

            fontsByCodePoint[rune.Value] = fallback;
            return fallback;
        }
    }

    private SKFont Prepare(SKFont font)
    {
        font.BaselineSnap = primaryFont.BaselineSnap;
        font.Edging = primaryFont.Edging;
        font.Embolden = primaryFont.Embolden;
        font.ForceAutoHinting = primaryFont.ForceAutoHinting;
        font.Hinting = primaryFont.Hinting;
        font.LinearMetrics = primaryFont.LinearMetrics;
        font.Subpixel = primaryFont.Subpixel;
        return font;
    }

    internal readonly record struct TextRun<T>(int Start, int Length, T Value);

    internal static List<TextRun<T>> BuildRuns<T>(string text, Func<Rune, T> resolve) where T : notnull
    {
        var runs = new List<TextRun<T>>();
        var index = 0;

        while (index < text.Length)
        {
            var hasRune = Rune.TryGetRuneAt(text, index, out var rune);
            var length = hasRune ? rune.Utf16SequenceLength : 1;
            var value = resolve(hasRune ? rune : Rune.ReplacementChar);

            if (runs.Count > 0 && EqualityComparer<T>.Default.Equals(runs[^1].Value, value))
            {
                var previous = runs[^1];
                runs[^1] = previous with { Length = previous.Length + length };
            }
            else
            {
                runs.Add(new TextRun<T>(index, length, value));
            }

            index += length;
        }

        return runs;
    }
}
