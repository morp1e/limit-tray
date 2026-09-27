using LimitTray.Native.Ui;
using Windows.Win32.Graphics.DirectWrite;

namespace LimitTray.Native.Graphics;

/// <summary>
/// DirectWrite text formats and layouts. Separate from <see cref="Canvas"/> because
/// layout has to be measured before the surface can be sized.
/// </summary>
internal sealed unsafe class TextEngine : ITextMeasure, IDisposable
{
    private readonly GraphicsFactory _factory;
    private readonly Dictionary<TextStyle, nint> _formats = new();
    private readonly Dictionary<TextStyle, float> _lineHeights = new();
    private IDWriteTypography* _tabular;
    private IDWriteTypography* _smallCaps;

    public TextEngine(GraphicsFactory factory) => _factory = factory;

    public SizeF Measure(string text, TextStyle style, float maxWidth = float.MaxValue)
    {
        if (text.Length == 0) return new SizeF(0, LineHeight(style));
        var layout = CreateLayout(text, style, maxWidth, float.MaxValue);
        DWRITE_TEXT_METRICS metrics;
        layout->GetMetrics(&metrics);
        layout->Release();
        return new SizeF(metrics.width, metrics.height);
    }

    /// <summary>Height of one line of the style, for rows that must not move when the text changes.</summary>
    public float LineHeight(TextStyle style)
    {
        if (_lineHeights.TryGetValue(style, out var cached)) return cached;
        var height = Measure("Ag", style).H;
        _lineHeights[style] = height;
        return height;
    }

    /// <summary>The caller releases the returned layout.</summary>
    public IDWriteTextLayout* CreateLayout(string text, TextStyle style, float maxWidth, float maxHeight)
    {
        var format = Format(style);
        IDWriteTextLayout* layout;
        fixed (char* chars = text)
            _factory.DWrite->CreateTextLayout(chars, (uint)text.Length, format, maxWidth, maxHeight, &layout);

        var all = new DWRITE_TEXT_RANGE { startPosition = 0, length = (uint)text.Length };
        if (style.Tabular) layout->SetTypography(Typography(ref _tabular, TabularFeatures), all);
        if (style.SmallCaps) layout->SetTypography(Typography(ref _smallCaps, SmallCapsFeatures), all);
        return layout;
    }

    private static readonly DWRITE_FONT_FEATURE_TAG[] TabularFeatures =
        { DWRITE_FONT_FEATURE_TAG.DWRITE_FONT_FEATURE_TAG_TABULAR_FIGURES };

    private static readonly DWRITE_FONT_FEATURE_TAG[] SmallCapsFeatures =
    {
        DWRITE_FONT_FEATURE_TAG.DWRITE_FONT_FEATURE_TAG_SMALL_CAPITALS,
        DWRITE_FONT_FEATURE_TAG.DWRITE_FONT_FEATURE_TAG_SMALL_CAPITALS_FROM_CAPITALS,
    };

    private IDWriteTypography* Typography(ref IDWriteTypography* cache, DWRITE_FONT_FEATURE_TAG[] tags)
    {
        if (cache != null) return cache;
        IDWriteTypography* typography;
        _factory.DWrite->CreateTypography(&typography);
        foreach (var tag in tags)
            typography->AddFontFeature(new DWRITE_FONT_FEATURE { nameTag = tag, parameter = 1 });
        cache = typography;
        return typography;
    }

    private IDWriteTextFormat* Format(TextStyle style)
    {
        if (_formats.TryGetValue(style, out var cached)) return (IDWriteTextFormat*)cached;

        IDWriteTextFormat* format;
        fixed (char* family = style.Family ?? _factory.FontFamily)
        fixed (char* locale = "")
        {
            _factory.DWrite->CreateTextFormat(family, null, (DWRITE_FONT_WEIGHT)(int)style.Weight,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                style.SizeDip, locale, &format);
        }
        format->SetWordWrapping(style.Wrap
            ? DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_WRAP
            : DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        _formats[style] = (nint)format;
        return format;
    }

    public void Dispose()
    {
        foreach (var format in _formats.Values) ((IDWriteTextFormat*)format)->Release();
        _formats.Clear();
        if (_tabular != null) { _tabular->Release(); _tabular = null; }
        if (_smallCaps != null) { _smallCaps->Release(); _smallCaps = null; }
    }
}
