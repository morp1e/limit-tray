using System.Numerics;
using LimitTray.Native.Graphics;
using LimitTray.Native.Popup;

namespace LimitTray.Native.Ui;

/// <summary>
/// The panel and settings pages inside one popup, and the 150 ms horizontal slide
/// between them (v0.3 QuotaPopup.SlideTo: cubic ease-out, pages 360 DIPs apart). While
/// the slide runs both pages are laid out, so the panel is as tall as the taller one, as
/// the WPF Grid holding both was; afterwards it takes the visible page's height.
/// </summary>
internal sealed class PopupContent : IPopupScene
{
    private const float PageWidth = 360;
    private readonly Tween _slide = new(0);
    private bool _settingsShown;

    public PopupContent(PanelPage panel, SettingsPage settings)
    {
        Panel = panel;
        Settings = settings;
    }

    public PanelPage Panel { get; }
    public SettingsPage Settings { get; }
    public bool SettingsShown => _settingsShown;
    public Palette Palette => Panel.Palette;

    public void ShowSettings(long nowMs) => SlideTo(true, nowMs);

    public void ShowPanel(long nowMs) => SlideTo(false, nowMs);

    private void SlideTo(bool settings, long nowMs)
    {
        if (settings == _settingsShown && !_slide.IsAnimating(nowMs)) return;
        _settingsShown = settings;
        Settings.CloseList();
        _slide.MoveTo(settings ? 1 : 0, nowMs, Panel.AnimationsEnabled ? Durations.Slide : 0);
    }

    public bool IsSliding(long nowMs) => _slide.IsAnimating(nowMs);

    public bool IsAnimating(long nowMs) =>
        IsSliding(nowMs) || Panel.IsAnimating(nowMs) || Settings.IsAnimating(nowMs);

    public float Layout(ITextMeasure measure, float scale, long nowMs)
    {
        var panel = Panel.Layout(measure, scale, nowMs);
        var settings = Settings.Layout(measure, scale);
        if (IsSliding(nowMs)) return MathF.Max(panel, settings);
        return _settingsShown ? settings : panel;
    }

    public void DrawBase(Canvas canvas, long nowMs) =>
        Each(canvas, nowMs, panel: c => Panel.DrawBase(c, nowMs), settings: null);

    public void DrawGlowSources(Canvas canvas, long nowMs) =>
        Each(canvas, nowMs, panel: c => Panel.DrawGlowSources(c, nowMs), settings: null);

    public void DrawTop(Canvas canvas, long nowMs) =>
        Each(canvas, nowMs, panel: c => Panel.DrawTop(c, nowMs), settings: c => Settings.DrawTop(c, nowMs));

    /// <summary>Draws each visible page at its slide offset.</summary>
    private void Each(Canvas canvas, long nowMs, Action<Canvas>? panel, Action<Canvas>? settings)
    {
        var t = (float)_slide.Value(nowMs);
        var origin = canvas.Transform;
        if (t < 1 && panel is not null)
        {
            canvas.Transform = Matrix3x2.CreateTranslation(-PageWidth * t, 0) * origin;
            panel(canvas);
        }
        if (t > 0 && settings is not null)
        {
            canvas.Transform = Matrix3x2.CreateTranslation(PageWidth * (1 - t), 0) * origin;
            settings(canvas);
        }
        canvas.Transform = origin;
    }
}
