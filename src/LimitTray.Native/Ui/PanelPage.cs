using System.Numerics;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using LimitTray.Native.Graphics;

namespace LimitTray.Native.Ui;

internal enum HitKind { None, Card, Terminal, Refresh, Settings, Back, GitHub, Control, DropdownItem }

internal readonly record struct Hit(HitKind Kind, string? Key = null, int Index = -1)
{
    public static readonly Hit None = new(HitKind.None);
}

/// <summary>
/// The quota panel: a straight port of v0.3's PanelPage.xaml, laid out with WPF's rules
/// (a fixed-width UserControl is centred, Grid margins, DockPanel order) so the result
/// lands on the same pixels. Coordinates are DIPs relative to the panel's outer edge.
/// Drawing happens in three passes so the coloured glows can be blurred on their own:
/// base (backgrounds, tracks, dimmed cards), glow sources, and top (bars, rings, text).
/// </summary>
internal sealed class PanelPage : Popup.IPopupScene
{
    public const float PanelWidth = 360;
    private const float ContentLeft = 32;   // border 1 + centred 328-wide page ((358-328)/2) + margin 16
    private const float ContentWidth = 296;
    private const float RingBox = 52;
    // v0.3 drew the track as an Ellipse (stroke inside its 42 DIP bounds: radius 18.5) and the
    // arc as a Path of radius 21 (stroke centred on it), so the arc sits over the track's outer
    // half. Zoomed against the v0.3.2 screenshot, that offset is part of the look; it is kept.
    private const float TrackRadius = 18.5f;
    private const float ArcRadius = 21f;
    private const float RingStroke = 5;

    private readonly Dictionary<string, CardState> _cards = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private readonly Tween _refreshAngle = new(0, Easing.Linear);
    private string _footer = string.Empty;
    private readonly string _version;

    private RectF _refreshButton, _settingsButton, _brand;
    private PointF[] _points = new PointF[UsageHistory.MaxSamples];
    private float _headerHeight;
    private float _footerY, _footerHeight;

    public PanelPage()
    {
        var v = typeof(PanelPage).Assembly.GetName().Version;
        _version = v is null ? string.Empty : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public Palette Palette { get; set; } = Palette.DarkTheme;
    public Strings Strings { get; set; } = Strings.English;
    public bool AnimationsEnabled { get; set; } = true;
    public string? Hovered { get; set; }
    public HitKind HoveredKind { get; set; }
    public float Height { get; private set; }

    /// <summary>Cards expanded by the user; persisted by the host through settings.</summary>
    public IReadOnlySet<string> Expanded =>
        _cards.Values.Where(card => card.Expanded).Select(card => card.Model.Provider).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Refreshes cards in place. Only values that actually changed animate; the first
    /// reading of a card jumps, as v0.3 did (its previous value was NaN).
    /// </summary>
    public void Update(IReadOnlyList<QuotaSnapshot> snapshots, UsageHistory history, AppSettings settings,
        DateTimeOffset now, long nowMs)
    {
        var duration = AnimationsEnabled ? Durations.Value : 0;
        _order.Clear();
        foreach (var snapshot in snapshots)
        {
            var model = PanelModel.Card(snapshot, history, settings, Strings, now);
            _order.Add(model.Provider);
            if (!_cards.TryGetValue(model.Provider, out var card))
            {
                card = new CardState(model, settings.ExpandedProviders.Contains(model.Provider));
                _cards[model.Provider] = card;
                continue;
            }

            card.Model = model;
            card.Ring.MoveTo(model.RingPercent, nowMs, duration);
            foreach (var row in model.Rows)
            {
                if (card.Bars.TryGetValue(row.Kind, out var bar)) bar.MoveTo(row.Percent, nowMs, duration);
                else card.Bars[row.Kind] = new Tween(row.Percent);
            }
        }

        foreach (var gone in _cards.Keys.Except(_order).ToList()) _cards.Remove(gone);
        _footer = PanelModel.Footer(snapshots, now, Strings);
    }

    public void ToggleExpanded(string provider)
    {
        if (_cards.TryGetValue(provider, out var card)) card.Expanded = !card.Expanded;
    }

    public void StartRefreshTurn(long nowMs) =>
        _refreshAngle.Run(0, 360, nowMs, AnimationsEnabled ? Durations.RefreshTurn : 0);

    public void ShowTerminalError(string provider, long untilMs)
    {
        if (_cards.TryGetValue(provider, out var card)) card.TerminalErrorUntil = untilMs;
    }

    public bool IsAnimating(long nowMs) =>
        _refreshAngle.IsAnimating(nowMs) ||
        _cards.Values.Any(card => card.Ring.IsAnimating(nowMs) || card.Bars.Values.Any(bar => bar.IsAnimating(nowMs)));

    /// <summary>The earliest moment a transient message expires, so the host can schedule a redraw.</summary>
    public long? NextExpiry(long nowMs) =>
        _cards.Values.Where(card => card.TerminalErrorUntil > nowMs).Select(card => (long?)card.TerminalErrorUntil).Min();

    // ---- layout ---------------------------------------------------------------

    public float Layout(ITextMeasure m, float scale, long nowMs)
    {
        float Line(TextStyle style) => Snap.ToPixels(m.LineHeight(style), scale);

        // Header: DockPanel, brand left, two icon buttons right (Padding 5,1 + border 1).
        var top = 1f + 12f;
        var settings = Styles.IconButton(m, Glyphs.Settings, Styles.HeaderIcon, scale);
        var refresh = Styles.IconButton(m, Glyphs.Refresh, Styles.HeaderIcon, scale);
        _headerHeight = MathF.Max(MathF.Max(settings.H, refresh.H), Line(Styles.Brand));
        _settingsButton = new RectF(ContentLeft + ContentWidth - settings.W, top, settings.W, _headerHeight);
        _refreshButton = new RectF(_settingsButton.X - refresh.W, top, refresh.W, _headerHeight);
        _brand = new RectF(ContentLeft, top, 200, _headerHeight);

        var y = top + _headerHeight + 12;
        foreach (var provider in _order)
        {
            var card = _cards[provider];
            y = LayoutCard(card, m, scale, y, nowMs) + 10;
        }

        // Footer: DockPanel Margin 2,4,2,0, then the page's bottom margin and the border.
        _footerY = y + 4;
        _footerHeight = Line(Styles.Small);
        Height = _footerY + _footerHeight + 12 + 1;
        return Height;
    }

    private float LayoutCard(CardState card, ITextMeasure m, float scale, float y, long nowMs)
    {
        float Line(TextStyle style) => Snap.ToPixels(m.LineHeight(style), scale);
        var model = card.Model;

        // Border 1 + Padding 14,12. Grid columns 60 | *, right column has Margin 8,0,0,0.
        var contentX = ContentLeft + 1 + 14;
        var contentTop = y + 1 + 12;
        var rightX = contentX + 60 + 8;
        var rightWidth = ContentWidth - 2 - 28 - 60 - 8;

        card.RingBox = new RectF(contentX + (60 - RingBox) / 2, contentTop, RingBox, RingBox);

        // Row 0: title | reset text | terminal button (docked right, first child is outermost).
        var terminal = Styles.IconButton(m, Glyphs.Terminal, Styles.TerminalIcon, scale);
        var terminalHeight = terminal.H;
        card.Terminal = new RectF(rightX + rightWidth - terminal.W, contentTop - 2, terminal.W, terminalHeight);
        var resetWidth = Snap.ToPixels(m.Measure(model.ResetShortText, Styles.Small).W, scale);
        card.Reset = new RectF(card.Terminal.X - 6 - resetWidth, contentTop + 2, resetWidth, Line(Styles.Small));
        var row0 = MathF.Max(Line(Styles.CardTitle), MathF.Max(Line(Styles.Small) + 2, terminalHeight - 2));
        card.Title = new RectF(rightX, contentTop, card.Reset.X - rightX, Line(Styles.CardTitle));

        var cursor = contentTop + row0;
        card.Rows.Clear();
        if (model.HasData)
        {
            foreach (var row in model.Rows)
            {
                cursor += 3;
                var rowHeight = MathF.Max(Line(Styles.Body), Line(Styles.BodySemiTabular));
                var layout = new RowLayout(row)
                {
                    Label = new RectF(rightX, cursor, 52, rowHeight),
                    Track = new RectF(rightX + 52 + 4, cursor + (rowHeight - 6) / 2, rightWidth - 52 - 38 - 4 - 10, 6),
                    Percent = new RectF(rightX + rightWidth - 38, cursor, 38, rowHeight),
                };
                cursor += rowHeight;

                if (card.Expanded && row.Sparkline is not null)
                {
                    cursor += 4;
                    layout.Sparkline = new RectF(rightX + 56, cursor, rightWidth - 56 - 38, 24);
                    cursor += 24;
                }

                if (card.Expanded && row.BurnRateText is { } burn)
                {
                    cursor += 2;
                    var width = rightWidth - 56;
                    var height = Snap.ToPixels(m.Measure(burn, Styles.SmallWrap, width).H, scale);
                    layout.Burn = new RectF(rightX + 56, cursor, width, height);
                    cursor += height;
                }

                card.Rows.Add(layout);
            }
        }

        if (model.HealthText is { } health)
        {
            cursor += 6;
            var height = Snap.ToPixels(m.Measure(health, Styles.SmallWrap, rightWidth).H, scale);
            card.Health = new RectF(rightX, cursor, rightWidth, height);
            cursor += height;
        }
        else
        {
            card.Health = RectF.Empty;
        }

        card.LastUpdated = RectF.Empty;
        card.TerminalErrorBox = RectF.Empty;
        if (card.Expanded)
        {
            cursor += 7;
            card.LastUpdated = new RectF(rightX, cursor, rightWidth, Line(Styles.Small));
            cursor += card.LastUpdated.H;
            if (card.TerminalErrorUntil > nowMs)
            {
                cursor += 3;
                card.TerminalErrorBox = new RectF(rightX, cursor, rightWidth, Line(Styles.Body));
                cursor += card.TerminalErrorBox.H;
            }
        }

        var contentHeight = MathF.Max(RingBox, cursor - contentTop);
        card.Bounds = new RectF(ContentLeft, y, ContentWidth, 1 + 12 + contentHeight + 12 + 1);
        return card.Bounds.Bottom;
    }

    // ---- drawing --------------------------------------------------------------

    /// <summary>Card backgrounds, tracks, and every dimmed card in full (no glow, one opacity group).</summary>
    public void DrawBase(Canvas c, long nowMs)
    {
        foreach (var provider in _order)
        {
            var card = _cards[provider];
            if (card.Model.IsDimmed)
            {
                // Opacity applies to the card as a group, like WPF's Border.Opacity.
                c.PushLayer(null, 0, 0.55f);
                DrawCardBackground(c, card);
                DrawCardContent(c, card, nowMs);
                c.PopLayer();
                continue;
            }

            DrawCardBackground(c, card);
            c.StrokeEllipse(RingCentre(card), TrackRadius, TrackRadius, Palette.Track, RingStroke);
            foreach (var row in card.Rows) c.FillRoundedRect(row.Track, 3, Palette.Track);
        }
    }

    /// <summary>What the WPF DropShadowEffect glows were made of: ring arcs and bars, fresh cards only.</summary>
    public void DrawGlowSources(Canvas c, long nowMs)
    {
        foreach (var provider in _order)
        {
            var card = _cards[provider];
            if (card.Model.IsDimmed) continue;
            if (card.Model.RingGlow)
            {
                var sweep = (float)(card.Ring.Value(nowMs) * 3.6);
                c.Arc(RingCentre(card), ArcRadius, -90, sweep,
                    Palette.From(card.Model.RingColour, (byte)(0.45 * 255)), RingStroke);
            }

            foreach (var row in card.Rows)
            {
                if (!row.Model.Glow) continue;
                var bar = BarRect(card, row, nowMs);
                if (bar.W > 0) c.FillRoundedRect(bar, 3, Palette.From(row.Model.Colour, (byte)(0.55 * 255)));
            }
        }
    }

    /// <summary>Header, fresh cards' content, footer.</summary>
    public void DrawTop(Canvas c, long nowMs)
    {
        c.Text("LIM'IT", Styles.Brand, _brand, Palette.Text, TextAlign.Leading, centreVertically: true);
        DrawIconButton(c, _refreshButton, Glyphs.Refresh, Styles.HeaderIcon, HitKind.Refresh,
            rotation: (float)_refreshAngle.Value(nowMs));
        DrawIconButton(c, _settingsButton, Glyphs.Settings, Styles.HeaderIcon, HitKind.Settings, 0);

        foreach (var provider in _order)
        {
            var card = _cards[provider];
            if (!card.Model.IsDimmed) DrawCardContent(c, card, nowMs);
        }

        var footer = new RectF(ContentLeft + 2, _footerY, ContentWidth - 4, _footerHeight);
        c.Text(_version, Styles.Small, footer, Palette.MutedText, TextAlign.Trailing);
        c.Text(_footer, Styles.Small, footer, Palette.MutedText);
    }

    private void DrawCardBackground(Canvas c, CardState card)
    {
        var hovered = Hovered == card.Model.Provider;
        c.FillRoundedRect(card.Bounds.Inflate(-1, -1), 13, Palette.Card);
        c.StrokeRoundedRect(card.Bounds, 14, hovered ? Palette.CardHoverBorder : Palette.CardBorder, 1);
    }

    private void DrawCardContent(Canvas c, CardState card, long nowMs)
    {
        var model = card.Model;
        var dimmed = model.IsDimmed;
        if (dimmed)
        {
            // In the dimmed group the tracks are drawn here; fresh cards drew them in the base pass.
            c.StrokeEllipse(RingCentre(card), TrackRadius, TrackRadius, Palette.Track, RingStroke);
            foreach (var row in card.Rows) c.FillRoundedRect(row.Track, 3, Palette.Track);
        }

        // Ring.
        var sweep = (float)(card.Ring.Value(nowMs) * 3.6);
        c.Arc(RingCentre(card), ArcRadius, -90, sweep, Palette.From(model.RingColour, model.Alpha), RingStroke);
        c.Text(model.RingPercentText, Styles.RingNumber, card.RingBox,
            Palette.From(Palette.Lighter(model.RingColour), model.Alpha), TextAlign.Centre, centreVertically: true);

        // Header row.
        c.Text(model.Title, Styles.CardTitle, card.Title, Palette.Text);
        c.Text(model.ResetShortText, Styles.Small, card.Reset, Palette.MutedText);
        if (Hovered == model.Provider)
            DrawIconButton(c, card.Terminal, Glyphs.Terminal, Styles.TerminalIcon, HitKind.Terminal, 0);

        // Rows.
        foreach (var row in card.Rows)
        {
            c.Text(row.Model.Label, Styles.Body, row.Label, Palette.MutedText, TextAlign.Leading, centreVertically: true);
            var bar = BarRect(card, row, nowMs);
            if (bar.W > 0)
            {
                c.FillRoundedRectHorizontalGradient(bar, MathF.Min(3, bar.W / 2),
                    Palette.From(row.Model.Colour, row.Model.Alpha),
                    Palette.From(Palette.Lighter(row.Model.Colour), row.Model.Alpha));
            }
            c.Text(row.Model.PercentText, Styles.BodySemiTabular, row.Percent,
                Palette.From(Palette.Lighter(row.Model.Colour), row.Model.Alpha), TextAlign.Trailing, centreVertically: true);

            if (row.Model.Sparkline is { } spark && row.Sparkline.W > 0)
            {
                if (_points.Length < spark.Count) _points = new PointF[spark.Count];
                var points = _points.AsSpan(0, spark.Count);
                var box = row.Sparkline;
                for (var i = 0; i < spark.Count; i++)
                {
                    points[i] = new PointF(
                        box.X + 1 + spark[i].X * (box.W - 2),
                        box.Y + box.H - 1 - spark[i].Y * (box.H - 2));
                }
                c.Polyline(points, Palette.From(row.Model.Colour, row.Model.Alpha), 1.5f);
            }

            if (row.Model.BurnRateText is { } burn && row.Burn.W > 0)
                c.Text(burn, Styles.SmallWrap, row.Burn, Palette.MutedText);
        }

        if (model.HealthText is { } health && card.Health.W > 0)
            c.Text(health, Styles.SmallWrap, card.Health, Palette.Text);

        if (card.LastUpdated.W > 0)
            c.Text(model.LastUpdatedText, Styles.Small, card.LastUpdated, Palette.MutedText);
        if (card.TerminalErrorBox.W > 0)
            c.Text(Strings.TerminalFailed, Styles.Body, card.TerminalErrorBox, Palette.From(model.RingColour, model.Alpha));
    }

    private void DrawIconButton(Canvas c, RectF r, string glyph, TextStyle style, HitKind kind, float rotation)
    {
        var hovered = HoveredKind == kind && (kind != HitKind.Terminal || Hovered is not null);
        if (hovered) c.StrokeRoundedRect(r, 5, Palette.CardHoverBorder, 1);

        var previous = c.Transform;
        if (rotation != 0)
        {
            var centre = new Vector2(r.CentreX, r.CentreY);
            c.Transform = Matrix3x2.CreateRotation(rotation * MathF.PI / 180, centre) * previous;
        }
        c.Text(glyph, style, r, hovered ? Palette.Text : Palette.MutedText, TextAlign.Centre, centreVertically: true);
        c.Transform = previous;
    }

    private static PointF RingCentre(CardState card) =>
        new(card.RingBox.X + RingBox / 2, card.RingBox.Y + RingBox / 2);

    private static RectF BarRect(CardState card, RowLayout row, long nowMs)
    {
        var percent = card.Bars.TryGetValue(row.Model.Kind, out var tween) ? tween.Value(nowMs) : row.Model.Percent;
        var width = (float)(Math.Clamp(percent, 0, 100) / 100.0 * row.Track.W);
        return row.Track with { W = width };
    }

    // ---- hit testing -----------------------------------------------------------

    public Hit HitTest(float x, float y)
    {
        if (_refreshButton.Contains(x, y)) return new Hit(HitKind.Refresh);
        if (_settingsButton.Contains(x, y)) return new Hit(HitKind.Settings);
        foreach (var provider in _order)
        {
            var card = _cards[provider];
            if (!card.Bounds.Contains(x, y)) continue;
            // The terminal button is laid out always and only faded in on hover, so it is
            // hit-testable whenever the pointer is over its card, as in v0.3.
            return card.Terminal.Contains(x, y) ? new Hit(HitKind.Terminal, provider) : new Hit(HitKind.Card, provider);
        }
        return Hit.None;
    }

    private sealed class CardState
    {
        public CardState(CardModel model, bool expanded)
        {
            Model = model;
            Expanded = expanded;
            Ring = new Tween(model.RingPercent);
            foreach (var row in model.Rows) Bars[row.Kind] = new Tween(row.Percent);
        }

        public CardModel Model;
        public bool Expanded;
        public readonly Tween Ring;
        public readonly Dictionary<WindowKind, Tween> Bars = new();
        public long TerminalErrorUntil;

        public RectF Bounds, RingBox, Title, Reset, Terminal, Health, LastUpdated, TerminalErrorBox;
        public readonly List<RowLayout> Rows = new();
    }

    private sealed class RowLayout(RowModel model)
    {
        public RowModel Model { get; } = model;
        public RectF Label, Track, Percent, Sparkline, Burn;
    }
}
