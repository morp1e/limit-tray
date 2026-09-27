using LimitTray.Native.Graphics;
using LimitTray.Native.Ui;

namespace LimitTray.Native.Popup;

/// <summary>What the renderer draws: the panel, the settings page, or both mid-slide.</summary>
internal interface IPopupScene
{
    Palette Palette { get; }

    /// <summary>Lays the scene out and returns the panel's height in DIPs.</summary>
    float Layout(ITextMeasure measure, float scale, long nowMs);

    void DrawBase(Canvas canvas, long nowMs);
    void DrawGlowSources(Canvas canvas, long nowMs);
    void DrawTop(Canvas canvas, long nowMs);
}

/// <summary>
/// Composes one popup frame into a premultiplied surface for UpdateLayeredWindow. The
/// frame is the v0.3.4 window: a 360 DIP panel inside a 16 DIP margin that holds its
/// shadow (DropShadowEffect BlurRadius 18, ShadowDepth 4 downwards, Opacity 0.45), the
/// two radial brand glows clipped to the rounded panel, and coloured glows under bars
/// and rings (BlurRadius 10). Everything here lives only while the popup is open.
/// </summary>
internal sealed class PopupRenderer : IDisposable
{
    public const float Margin = 16;

    private readonly GraphicsFactory _factory;
    private Surface? _main;
    private Surface? _glow;
    private Canvas? _mainCanvas;
    private Canvas? _glowCanvas;

    public PopupRenderer(float scale)
    {
        _factory = GraphicsFactory.Create();
        Text = new TextEngine(_factory);
        Scale = scale;
    }

    public TextEngine Text { get; }
    public float Scale { get; private set; }

    /// <summary>The panel's height in DIPs from the last frame.</summary>
    public float PanelHeight { get; private set; }

    public void SetScale(float scale)
    {
        if (Math.Abs(scale - Scale) < 0.001f) return;
        Scale = scale;
        DropSurfaces();
    }

    /// <summary>Converts a point in window pixels to panel DIPs.</summary>
    public PointF ToPanel(int x, int y) => new(x / Scale - Margin, y / Scale - Margin);

    public Surface Render(IPopupScene scene, long nowMs)
    {
        var height = scene.Layout(Text, Scale, nowMs);
        PanelHeight = height;
        var width = PanelPage.PanelWidth;
        var pixelsW = (int)MathF.Ceiling((width + 2 * Margin) * Scale);
        var pixelsH = (int)MathF.Ceiling((height + 2 * Margin) * Scale);
        EnsureSurfaces(pixelsW, pixelsH);

        var main = _mainCanvas!;
        var glow = _glowCanvas!;
        var palette = scene.Palette;
        var inner = new RectF(1, 1, width - 2, height - 2);

        // 1. Shadow: the panel's opaque body, offset down 4, blurred on its own.
        main.Begin();
        main.Clear();
        main.Translate(Margin, Margin);
        main.FillRoundedRect(inner.Offset(0, 4), 11, Colour.Black.WithAlpha((byte)(0.45 * 255)));
        main.End();
        var spread = (int)MathF.Ceiling(24 * Scale);
        Blur.Gaussian(_main!, 0, 0, pixelsW, pixelsH, 18 * Scale);

        // 2. Panel body, border, brand glows, and everything under the coloured glows.
        main.Begin();
        main.Translate(Margin, Margin);
        main.FillRoundedRect(inner, 11, palette.Surface);
        main.StrokeRoundedRect(new RectF(0, 0, width, height), 12, palette.CardBorder, 1);
        main.PushLayer(inner, 11, 1);
        main.FillRadialEllipse(new RectF(1 - 190, 1 - 160, 420, 300),
            Palette.From(Core.Presentation.Palette.ClaudeBrand, palette.GlowStrength),
            Palette.From(Core.Presentation.Palette.ClaudeBrand, 0));
        main.FillRadialEllipse(new RectF(width - 1 + 170 - 380, height - 1 + 150 - 280, 380, 280),
            Palette.From(Core.Presentation.Palette.CodexBrand, palette.GlowStrength),
            Palette.From(Core.Presentation.Palette.CodexBrand, 0));
        scene.DrawBase(main, nowMs);
        main.PopLayer();
        main.End();

        // 3. Coloured glows: drawn alone, blurred, laid over the base.
        glow.Begin();
        glow.Clear();
        glow.Translate(Margin, Margin);
        glow.PushLayer(inner, 11, 1);
        scene.DrawGlowSources(glow, nowMs);
        glow.PopLayer();
        glow.End();
        Blur.Gaussian(_glow!, spread / 2, spread / 2, pixelsW - spread / 2, pixelsH - spread / 2, 10 * Scale);
        Blur.CompositeOver(_main!, _glow!, 0, 0, pixelsW, pixelsH);

        // 4. Bars, rings, text.
        main.Begin();
        main.Translate(Margin, Margin);
        main.PushLayer(inner, 11, 1);
        scene.DrawTop(main, nowMs);
        main.PopLayer();
        main.End();

        return _main!;
    }

    private void EnsureSurfaces(int width, int height)
    {
        if (_main is not null && _main.Width == width && _main.Height == height) return;
        DropSurfaces();
        _main = new Surface(width, height);
        _glow = new Surface(width, height);
        _mainCanvas = new Canvas(_factory, Text, _main, Scale);
        _glowCanvas = new Canvas(_factory, Text, _glow, Scale);
    }

    private void DropSurfaces()
    {
        _mainCanvas?.Dispose();
        _glowCanvas?.Dispose();
        _main?.Dispose();
        _glow?.Dispose();
        _mainCanvas = _glowCanvas = null;
        _main = _glow = null;
    }

    public void Dispose()
    {
        DropSurfaces();
        Text.Dispose();
        _factory.Dispose();
    }
}
