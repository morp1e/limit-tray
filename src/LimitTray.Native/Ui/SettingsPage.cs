using System.Globalization;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using LimitTray.Native.Graphics;

namespace LimitTray.Native.Ui;

/// <summary>
/// The settings page: v0.3's SettingsPage.xaml and SettingsViewModel without WPF. Every
/// change applies at once through the host; there is no save button. Coordinates are
/// panel DIPs, laid out with the same WPF rules as the panel page (v0.3.2 screenshot:
/// 498 DIPs tall, which this layout reproduces). The combo box list is an overlay drawn
/// inside the popup's own surface; v0.3 opened a second window for it.
/// </summary>
internal sealed class SettingsPage
{
    private const float ContentLeft = 32;
    private const float ContentRight = 328;

    private readonly List<SettingRow> _rows = new();
    private readonly List<(string Text, RectF Box)> _groups = new();
    private readonly Tween _dropdownFade = new(0);
    private AppSettings _settings = AppSettings.Default;
    private bool _startup;
    private bool _saveFailed;
    private string _version = string.Empty;

    private RectF _back, _title, _banner, _footerBack, _footerVersion, _github;
    private RectF _dropdown;
    private float _dropdownItemHeight;

    public Palette Palette { get; set; } = Palette.DarkTheme;
    public Strings Strings { get; set; } = Strings.English;
    public bool AnimationsEnabled { get; set; } = true;
    public float Height { get; private set; }

    /// <summary>Key of the control under the pointer ("back", "footer-back", "github" or a row key).</summary>
    public string? Hovered { get; set; }
    public int HoveredItem { get; set; } = -1;

    /// <summary>Row key of the open combo box list, or null.</summary>
    public string? OpenDropdown { get; private set; }

    public SettingsPage()
    {
        var v = typeof(SettingsPage).Assembly.GetName().Version;
        _version = v is null ? string.Empty : $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    public void Update(AppSettings settings, bool startupEnabled, bool saveFailed)
    {
        _settings = settings;
        _startup = startupEnabled;
        _saveFailed = saveFailed;
    }

    public void OpenList(string key, long nowMs)
    {
        OpenDropdown = key;
        HoveredItem = -1;
        _dropdownFade.Run(0, 1, nowMs, AnimationsEnabled ? Durations.Slide : 0);
    }

    public void CloseList()
    {
        OpenDropdown = null;
        HoveredItem = -1;
    }

    public bool IsAnimating(long nowMs) => _dropdownFade.IsAnimating(nowMs);

    // ---- model --------------------------------------------------------------------

    private IEnumerable<SettingRow> BuildRows()
    {
        var s = Strings;
        yield return Segment("theme", s.GroupAppearance, s.Theme,
            new[] { s.ThemeSystem, s.ThemeDark, s.ThemeLight }, (int)_settings.Theme);
        yield return Segment("language", s.GroupAppearance, s.Language,
            new[] { s.LanguageSystem, "TR", "EN" }, (int)_settings.Language);

        var refresh = AppSettings.AllowedRefreshSeconds;
        yield return Combo("refresh", s.GroupData, s.RefreshInterval,
            refresh.Select(RefreshLabel).ToArray(), Array.IndexOf(refresh, _settings.RefreshSeconds));
        yield return Combo("caution", s.GroupData, s.CautionThreshold,
            CautionValues().Select(Percent).ToArray(), Array.IndexOf(CautionValues(), _settings.Thresholds.Caution));
        var warnings = WarningValues(_settings.Thresholds.Caution);
        yield return Combo("warning", s.GroupData, s.WarningThreshold,
            warnings.Select(Percent).ToArray(), Array.IndexOf(warnings, _settings.Thresholds.Warning));
        yield return Toggle("notifications", s.GroupData, s.Notifications, _settings.Notifications);

        yield return Segment("tray-style", s.GroupTray, s.TrayStyle,
            new[] { s.TrayStyleRing, s.TrayStyleNumber, s.TrayStyleDualBar }, (int)_settings.TrayStyle);
        yield return Combo("tray-source", s.GroupTray, s.TraySource,
            new[] { s.TraySourceHighest, s.TraySourceClaudeSession, s.TraySourceClaudeWeekly,
                s.TraySourceCodexSession, s.TraySourceCodexWeekly }, (int)_settings.TraySource);

        yield return Toggle("startup", s.GroupSystem, s.StartWithWindows, _startup);
    }

    private static SettingRow Segment(string key, string group, string label, string[] options, int selected) =>
        new(key, group, label, RowKind.Segment, options, selected, false);

    private static SettingRow Combo(string key, string group, string label, string[] options, int selected) =>
        new(key, group, label, RowKind.Combo, options, Math.Max(0, selected), false);

    private static SettingRow Toggle(string key, string group, string label, bool on) =>
        new(key, group, label, RowKind.Toggle, Array.Empty<string>(), -1, on);

    private static double[] CautionValues() => Enumerable.Range(0, 7).Select(i => 50.0 + i * 5).ToArray();

    private static double[] WarningValues(double caution) =>
        Enumerable.Range(0, (int)((95 - caution) / 5)).Select(i => caution + 5 + i * 5).ToArray();

    private static string Percent(double value) => value.ToString("0", CultureInfo.InvariantCulture) + "%";

    private string RefreshLabel(int seconds) => seconds % 60 == 0
        ? string.Format(CultureInfo.InvariantCulture, Strings.Minutes, seconds / 60)
        : string.Format(CultureInfo.InvariantCulture, Strings.Seconds, seconds);

    /// <summary>The settings a click on a row option produces; the startup toggle is not a setting.</summary>
    public AppSettings? Choose(string key, int index)
    {
        var s = _settings;
        switch (key)
        {
            case "theme": return s with { Theme = (ThemeMode)index };
            case "language": return s with { Language = (LanguageMode)index };
            case "refresh": return s with { RefreshSeconds = AppSettings.AllowedRefreshSeconds[index] };
            case "caution":
            {
                var caution = CautionValues()[index];
                var warning = s.Thresholds.Warning < caution + 5 ? caution + 5 : s.Thresholds.Warning;
                return s with { Thresholds = new QuotaThresholds(caution, warning) };
            }
            case "warning":
                return s with { Thresholds = new QuotaThresholds(s.Thresholds.Caution, WarningValues(s.Thresholds.Caution)[index]) };
            case "notifications": return s with { Notifications = !s.Notifications };
            case "tray-style": return s with { TrayStyle = (TrayIconStyle)index };
            case "tray-source": return s with { TraySource = (TrayIconSource)index };
            default: return null;
        }
    }

    // ---- layout -----------------------------------------------------------------------

    public float Layout(ITextMeasure m, float scale)
    {
        float Line(TextStyle style) => Snap.ToPixels(m.LineHeight(style), scale);

        // Root Grid Margin 16,14,16,12 inside the 1 DIP border.
        var y = 1f + 14f;
        var back = Styles.IconButton(m, Glyphs.Back, Styles.HeaderIcon, scale);
        var headerHeight = MathF.Max(back.H, Line(Styles.PageTitle));
        _back = new RectF(ContentLeft, y, back.W, headerHeight);
        _title = new RectF(_back.Right + 4, y, 200, headerHeight);
        y += headerHeight + 12;

        if (_saveFailed)
        {
            var height = 5 + Line(Styles.Body) + 5;
            _banner = new RectF(ContentLeft, y, ContentRight - ContentLeft, height);
            y += height + 10;
        }
        else
        {
            _banner = RectF.Empty;
        }

        _rows.Clear();
        _groups.Clear();
        var rows = BuildRows().ToList();
        string? group = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var lastGroup = row.Group == Strings.GroupSystem;
            if (row.Group != group)
            {
                group = row.Group;
                // GroupHeaderStyle Margin 0,4,0,2; the body StackPanel adds 2 on top.
                y += 4;
                var height = Line(Styles.Group);
                _groups.Add((row.Group, new RectF(ContentLeft, y, 200, height)));
                y += height + 2;
                if (!lastGroup) y += 2;
            }

            // SettingRowStyle: MinHeight 28, Margin 0,2,0,2.
            y += 2;
            var rowBox = new RectF(ContentLeft, y, ContentRight - ContentLeft, 28);
            row.Box = rowBox;
            row.Label = rowBox with { W = 180 };
            row.Control = ControlBox(row, m, scale, rowBox);
            _rows.Add(row);
            y += 28 + 2;

            var nextGroup = i + 1 < rows.Count ? rows[i + 1].Group : null;
            if (nextGroup != row.Group) y += lastGroup ? 8 : 10;
        }

        // Footer DockPanel Margin 2,6,2,0: "‹ Back" button left, "vX · GitHub" right.
        y += 6;
        var footerBack = Styles.IconButton(m, BackText, Styles.Body, scale);
        // Measured as one run: a trailing space is not part of a measured width, so measuring
        // "vX · " alone glued "GitHub" to the dot.
        var wholeWidth = Snap.ToPixels(m.Measure(VersionRun + "GitHub", Styles.Small).W, scale);
        var githubWidth = Snap.ToPixels(m.Measure("GitHub", Styles.Small).W, scale);
        var footerHeight = MathF.Max(footerBack.H, Line(Styles.Small));
        _footerBack = new RectF(ContentLeft + 2, y, footerBack.W, footerHeight);
        _github = new RectF(ContentRight - 2 - githubWidth, y, githubWidth, footerHeight);
        _footerVersion = new RectF(ContentRight - 2 - wholeWidth, y, wholeWidth - githubWidth, footerHeight);
        y += footerHeight;

        Height = y + 12 + 1;
        LayoutDropdown(m, scale);
        return Height;
    }

    private string BackText => "‹ " + Strings.BackToPanel;

    /// <summary>
    /// v0.3's footer was three inlines on separate XAML lines, and WPF turns each line break
    /// between inlines into a space: the text on screen was "v0.3.2  ·  GitHub".
    /// </summary>
    private string VersionRun => _version + "  ·  ";

    private RectF ControlBox(SettingRow row, ITextMeasure m, float scale, RectF rowBox)
    {
        switch (row.Kind)
        {
            case RowKind.Segment:
            {
                // ListBox (Padding 1) of items: Padding 8,4, Margin 0,0,4,0, CornerRadius 7. The
                // horizontal StackPanel stretches items to the ListBox's height (28 - 2), so an
                // item is 26 tall, not text + padding (measured on the v0.3.2 screenshot).
                row.Items.Clear();
                var itemHeight = rowBox.H - 2;
                var widths = row.Options.Select(o => Snap.ToPixels(m.Measure(o, Styles.Body).W, scale) + 16).ToArray();
                var total = widths.Sum() + 4 * widths.Length + 2;
                var x = ContentRight - total + 1;
                var top = rowBox.Y + 1;
                foreach (var w in widths)
                {
                    row.Items.Add(new RectF(x, top, w, itemHeight));
                    x += w + 4;
                }
                return new RectF(ContentRight - total, rowBox.Y, total, rowBox.H);
            }
            case RowKind.Combo:
            {
                var label = row.Options.Length > 0 ? row.Options[row.Selected] : string.Empty;
                var width = MathF.Max(92, Snap.ToPixels(m.Measure(label, Styles.Body).W, scale) + 10 + 24);
                return new RectF(ContentRight - width, rowBox.Y + (rowBox.H - 26) / 2, width, 26);
            }
            default:
                return new RectF(ContentRight - 32, rowBox.Y + (rowBox.H - 18) / 2, 32, 18);
        }
    }

    private void LayoutDropdown(ITextMeasure m, float scale)
    {
        _dropdown = RectF.Empty;
        var row = _rows.FirstOrDefault(r => r.Key == OpenDropdown);
        if (row is null) return;

        // Popup: Border Padding 4, 1 DIP border, items Padding 10,5, 4 DIPs below the pill.
        _dropdownItemHeight = Snap.ToPixels(m.LineHeight(Styles.Body), scale) + 10;
        var widest = row.Options.Max(o => Snap.ToPixels(m.Measure(o, Styles.Body).W, scale)) + 20 + 8 + 2;
        var width = MathF.Max(row.Control.W, widest);
        var height = row.Options.Length * _dropdownItemHeight + 8 + 2;
        var below = new RectF(row.Control.X, row.Control.Bottom + 4, width, height);
        // Like WPF's popup placement, it opens upwards when there is no room below.
        _dropdown = below.Bottom <= Height - 1
            ? below
            : new RectF(row.Control.X, row.Control.Y - 4 - height, width, height);
        if (_dropdown.Right > ContentRight + 16) _dropdown = _dropdown with { X = ContentRight + 16 - width };
    }

    // ---- drawing ------------------------------------------------------------------------

    public void DrawTop(Canvas c, long nowMs)
    {
        DrawIconButton(c, _back, Glyphs.Back, Styles.HeaderIcon, Hovered == "back");
        c.Text(Strings.Settings, Styles.PageTitle, _title, Palette.Text, TextAlign.Leading, centreVertically: true);

        if (_banner.W > 0)
        {
            var (background, foreground) = InfoColours();
            c.FillRect(_banner, background);
            c.Text(Strings.SettingsSaveFailed, Styles.Body, _banner.Inflate(-8, -5), foreground);
        }

        foreach (var (text, box) in _groups)
            c.Text(text, Styles.Group, box, Palette.MutedText);

        foreach (var row in _rows)
        {
            c.Text(row.LabelText, Styles.Body, row.Label, Palette.Text, TextAlign.Leading, centreVertically: true);
            switch (row.Kind)
            {
                case RowKind.Segment: DrawSegment(c, row); break;
                case RowKind.Combo: DrawCombo(c, row); break;
                default: DrawToggle(c, row); break;
            }
        }

        DrawIconButton(c, _footerBack, BackText, Styles.Body, Hovered == "footer-back");
        c.Text(VersionRun, Styles.Small, _footerVersion, Palette.MutedText, TextAlign.Leading, centreVertically: true);
        c.Text("GitHub", Styles.Small, _github, Palette.MutedText, TextAlign.Leading, centreVertically: true, underline: true);

        if (_dropdown.W > 0) DrawDropdown(c, nowMs);
    }

    private void DrawSegment(Canvas c, SettingRow row)
    {
        for (var i = 0; i < row.Items.Count; i++)
        {
            var selected = i == row.Selected;
            var hovered = Hovered == row.Key && HoveredItem == i;
            if (selected) c.FillRoundedRect(row.Items[i], 7, Palette.CardHoverBorder);
            c.Text(row.Options[i], Styles.Body, row.Items[i], selected || hovered ? Palette.Text : Palette.MutedText,
                TextAlign.Centre, centreVertically: true);
        }
    }

    private void DrawCombo(Canvas c, SettingRow row)
    {
        var hovered = Hovered == row.Key || OpenDropdown == row.Key;
        c.FillRoundedRect(row.Control, 6, hovered ? Palette.CardHoverBorder : Palette.Track);
        var label = row.Options.Length > 0 ? row.Options[row.Selected] : string.Empty;
        c.Text(label, Styles.Body, new RectF(row.Control.X + 10, row.Control.Y, row.Control.W - 34, row.Control.H),
            Palette.Text, TextAlign.Leading, centreVertically: true);
        // Path M0,0 L4,4 L8,0, stroke 1.5, 9 DIPs from the right edge, centred.
        var x = row.Control.Right - 9 - 8;
        var y = row.Control.CentreY - 2;
        Span<PointF> chevron = stackalloc PointF[] { new(x, y), new(x + 4, y + 4), new(x + 8, y) };
        c.Polyline(chevron, Palette.MutedText, 1.5f);
    }

    private void DrawToggle(Canvas c, SettingRow row)
    {
        var box = row.Control;
        c.FillRoundedRect(box, 9, row.Checked ? Palette.Accent : Palette.Track);
        var thumbX = row.Checked ? box.Right - 2 - 7 : box.X + 2 + 7;
        var thumb = Hovered == row.Key ? Palette.Text : row.Checked ? Colour.White : Palette.MutedText;
        c.FillEllipse(new PointF(thumbX, box.CentreY), 7, 7, thumb);
    }

    private void DrawDropdown(Canvas c, long nowMs)
    {
        var row = _rows.First(r => r.Key == OpenDropdown);
        c.PushLayer(null, 0, (float)_dropdownFade.Value(nowMs));
        c.FillRoundedRect(_dropdown.Inflate(-1, -1), 7, Palette.Popup);
        c.StrokeRoundedRect(_dropdown, 8, Palette.CardBorder, 1);
        for (var i = 0; i < row.Options.Length; i++)
        {
            var item = DropdownItem(i);
            if (i == HoveredItem || i == row.Selected) c.FillRoundedRect(item, 4, Palette.CardHoverBorder);
            c.Text(row.Options[i], Styles.Body, new RectF(item.X + 10, item.Y, item.W - 20, item.H), Palette.Text,
                TextAlign.Leading, centreVertically: true);
        }
        c.PopLayer();
    }

    private RectF DropdownItem(int index) =>
        new(_dropdown.X + 5, _dropdown.Y + 5 + index * _dropdownItemHeight, _dropdown.W - 10, _dropdownItemHeight);

    private void DrawIconButton(Canvas c, RectF r, string text, TextStyle style, bool hovered)
    {
        if (hovered) c.StrokeRoundedRect(r, 5, Palette.CardHoverBorder, 1);
        c.Text(text, style, r, hovered ? Palette.Text : Palette.MutedText, TextAlign.Centre, centreVertically: true);
    }

    /// <summary>SystemColors.InfoBrush / InfoTextBrush, which v0.3 used for the save-failed line.</summary>
    private static (Colour Background, Colour Foreground) InfoColours()
    {
        static Colour FromSys(uint bgr) => new((byte)bgr, (byte)(bgr >> 8), (byte)(bgr >> 16));
        return (FromSys(Windows.Win32.PInvoke.GetSysColor(Windows.Win32.Graphics.Gdi.SYS_COLOR_INDEX.COLOR_INFOBK)),
            FromSys(Windows.Win32.PInvoke.GetSysColor(Windows.Win32.Graphics.Gdi.SYS_COLOR_INDEX.COLOR_INFOTEXT)));
    }

    // ---- hit testing --------------------------------------------------------------------

    public Hit HitTest(float x, float y)
    {
        if (OpenDropdown is { } open)
        {
            // While the list is open it takes every click, as WPF's StaysOpen=False popup did.
            if (_dropdown.Contains(x, y))
            {
                var index = (int)((y - _dropdown.Y - 5) / _dropdownItemHeight);
                var count = _rows.First(r => r.Key == open).Options.Length;
                return index >= 0 && index < count ? new Hit(HitKind.DropdownItem, open, index) : Hit.None;
            }
            return new Hit(HitKind.None, "outside");
        }

        if (_back.Contains(x, y)) return new Hit(HitKind.Back, "back");
        if (_footerBack.Contains(x, y)) return new Hit(HitKind.Back, "footer-back");
        if (_github.Contains(x, y)) return new Hit(HitKind.GitHub, "github");
        foreach (var row in _rows)
        {
            if (row.Kind == RowKind.Segment)
            {
                for (var i = 0; i < row.Items.Count; i++)
                    if (row.Items[i].Contains(x, y)) return new Hit(HitKind.Control, row.Key, i);
            }
            else if (row.Control.Contains(x, y))
            {
                return new Hit(HitKind.Control, row.Key, row.Selected);
            }
        }
        return Hit.None;
    }

    private enum RowKind { Segment, Combo, Toggle }

    private sealed class SettingRow(string key, string group, string label, RowKind kind, string[] options, int selected, bool isChecked)
    {
        public string Key { get; } = key;
        public string Group { get; } = group;
        public string LabelText { get; } = label;
        public RowKind Kind { get; } = kind;
        public string[] Options { get; } = options;
        public int Selected { get; } = selected;
        public bool Checked { get; } = isChecked;
        public RectF Box, Label, Control;
        public readonly List<RectF> Items = new();
    }
}
