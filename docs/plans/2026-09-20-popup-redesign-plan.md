# Lim'it v0.3 Panel Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lim'it panelini etkileşimli ve ayarlanabilir bir ürüne çevirmek: marka renkli cam görünüm, eşikte durum rengine geçen bar/halka, tıklanınca genişleyen kartlar, elle yenile, terminal açma, popup içinde ayarlar sayfası ve üç stilli tray ikonu.

**Architecture:** Her karar Core'a gider ve testlenir: `Settings` (ayar modeli + dosya), `Theme` (tek renk fonksiyonu), `TrayIconModel` (ikon ne çizer), `RequestRefresh` (toplayıcı arayüzü). App yalnız çizer: `TrayIconRenderer` modeli piksele çevirir, `QuotaPopup` XAML `DataTemplate` + küçük view-model'lerle iki sayfayı (Panel, Ayarlar) gösterir. Mevcut 158 test yeşil kalır.

**Tech Stack:** .NET 9, C# 13, WPF (`net9.0-windows`), WinForms `NotifyIcon`, System.Drawing (ikon), xUnit. Harici NuGet yok. Acrylic için `dwmapi.dll` P/Invoke.

**Spec:** `docs/specs/2026-09-20-popup-redesign-design.md`

## Global Constraints

- `src/LimitTray.Core` WPF/WinForms referansı **almaz**; `src/LimitTray.App` yalnız çizer. Testler yalnız Core'a bağlanır.
- Harici NuGet yok (test projesindeki xUnit hariç). Acrylic, tema izleme, terminal açma hepsi BCL + P/Invoke.
- Hiçbir hata durumu `%0` olarak çizilmez. `HealthState` sözle gösterilir. Kaynak seçili tray penceresi hatalıysa başka veriyle örtülmez.
- Token loglanmaz, diske yazılmaz, ekrana çıkmaz. `settings.json`'a yalnız ayar değerleri yazılır (testle korunur).
- Burn rate / sparkline eşikleri düşürülmez.
- Kullanıcıya görünen her metin `Strings.cs`'te, iki dilde. Kod, tanımlayıcı, dosya adı, commit mesajı ASCII.
- `TreatWarningsAsErrors=true`, `Nullable=enable` her projede.
- Yayınlanan yüzeylerde (README, docs, release notu) uzun tire yok; CI `no-em-dash` adımı bunu kontrol eder.
- Test komutu: `dotnet test tests/LimitTray.Tests -v q`. Derleme: `dotnet build LimitTray.sln -c Release`.
- Ekran doğrulaması DPI-aware yakalama ile yapılır (ekran %150). Yakalama aracı `SetProcessDPIAware` çağırmadan ölçüyorsa sonuç geçersizdir.
- Her görev sonunda commit. Codex'e delege ediliyorsa geçici `git worktree` içinde `codex exec -s workspace-write`; diff Git'ten okunur.

---

## Dosya haritası

**Core (yeni):**
- `src/LimitTray.Core/Settings/AppSettings.cs` — enum'lar, `QuotaThresholds`, `AppSettings` record ve varsayılanlar
- `src/LimitTray.Core/Settings/SettingsFile.cs` — JSON yaz/oku, bilinmeyen alan yok sayılır
- `src/LimitTray.Core/Settings/SettingsStore.cs` — `HistoryStore` deseni, `LastSaveFailed`
- `src/LimitTray.Core/Presentation/Theme.cs` — `Rgb`, `Palette`, `Theme.ColourFor`
- `src/LimitTray.Core/Presentation/TrayIconModel.cs` — `TrayBar`, `TrayIconModel`, `TrayIconModelBuilder`

**Core (değişen):**
- `Presentation/QuotaFormatter.cs` — `SeverityFor(percent, thresholds)` overload
- `Presentation/QuotaAlerts.cs` — eşik enjekte edilir, `UpdateThresholds`
- `Presentation/Strings.cs` — yeni metinler
- `Collectors/IQuotaCollector.cs` — `RequestRefresh()`
- `Claude/ClaudeCollector.cs` — aralık delegesi, uyandırma
- `Codex/CodexCollector.cs` — aktif sürece anında okuma, sürüm metni

**App (yeni):**
- `src/LimitTray.App/ViewModels/ObservableObject.cs` — `INotifyPropertyChanged` tabanı
- `src/LimitTray.App/ViewModels/WindowRowViewModel.cs`
- `src/LimitTray.App/ViewModels/ProviderCardViewModel.cs`
- `src/LimitTray.App/ViewModels/PanelViewModel.cs`
- `src/LimitTray.App/ViewModels/SettingsViewModel.cs`
- `src/LimitTray.App/Views/PanelPage.xaml(.cs)` — kartlar
- `src/LimitTray.App/Views/SettingsPage.xaml(.cs)` — ayarlar
- `src/LimitTray.App/Themes/Dark.xaml`, `Themes/Light.xaml` — yüzey fırçaları
- `src/LimitTray.App/Brushes.cs` — `Rgb` → `SolidColorBrush`
- `src/LimitTray.App/TerminalLauncher.cs`
- `src/LimitTray.App/WindowBackdrop.cs` — acrylic P/Invoke
- `src/LimitTray.App/SystemTheme.cs` — kayıt defteri okuma + değişiklik olayı

**App (değişen):**
- `QuotaPopup.xaml(.cs)` — iki sayfalı kabuk, geçiş animasyonu
- `TrayIconRenderer.cs` — üç stil, `TrayIconModel` alır
- `App.xaml.cs` — ayar yükleme, canlı uygulama, yenile, tema
- `App.xaml` — tema sözlüğü
- `LimitTray.App.csproj` — sürüm 0.3.0

**Test (yeni):** `tests/LimitTray.Tests/Settings/AppSettingsTests.cs`, `SettingsFileTests.cs`, `SettingsStoreTests.cs`; `Presentation/ThemeTests.cs`, `TrayIconModelTests.cs`; mevcut `QuotaAlertsTests`, `ClaudeCollectorTests`, `CodexCollectorTests` genişler.

---

### Task 1: Ayar modeli (`AppSettings`, `QuotaThresholds`)

**Files:**
- Create: `src/LimitTray.Core/Settings/AppSettings.cs`
- Test: `tests/LimitTray.Tests/Settings/AppSettingsTests.cs`

**Interfaces:**
- Produces:
  - `enum ThemeMode { System, Dark, Light }`
  - `enum LanguageMode { System, Turkish, English }`
  - `enum TrayIconStyle { Ring, Number, DualBar }`
  - `enum TrayIconSource { Highest, ClaudeSession, ClaudeWeekly, CodexSession, CodexWeekly }`
  - `record QuotaThresholds(double Caution, double Warning)` + `Default` + `bool IsValid`
  - `record AppSettings(ThemeMode Theme, LanguageMode Language, bool GlassEffect, int RefreshSeconds, QuotaThresholds Thresholds, bool Notifications, TrayIconStyle TrayStyle, TrayIconSource TraySource, IReadOnlySet<string> ExpandedProviders)` + `Default` + `AllowedRefreshSeconds` + `Normalised()`

- [ ] **Step 1: Failing test**

```csharp
// tests/LimitTray.Tests/Settings/AppSettingsTests.cs
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Default_MatchesSpec()
    {
        var s = AppSettings.Default;
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(LanguageMode.System, s.Language);
        Assert.True(s.GlassEffect);
        Assert.Equal(120, s.RefreshSeconds);
        Assert.Equal(60, s.Thresholds.Caution);
        Assert.Equal(85, s.Thresholds.Warning);
        Assert.True(s.Notifications);
        Assert.Equal(TrayIconStyle.DualBar, s.TrayStyle);
        Assert.Equal(TrayIconSource.Highest, s.TraySource);
        Assert.Empty(s.ExpandedProviders);
    }

    [Theory]
    [InlineData(60, 85, true)]
    [InlineData(50, 55, true)]
    [InlineData(80, 95, true)]
    [InlineData(45, 85, false)]   // caution below 50
    [InlineData(85, 90, false)]   // caution above 80
    [InlineData(62, 85, false)]   // not a multiple of 5
    [InlineData(60, 60, false)]   // warning must be caution + 5 or more
    [InlineData(60, 100, false)]  // warning above 95
    public void Thresholds_IsValid(double caution, double warning, bool expected) =>
        Assert.Equal(expected, new QuotaThresholds(caution, warning).IsValid);

    [Fact]
    public void Normalised_RepairsInvalidValuesToDefaults()
    {
        var broken = AppSettings.Default with
        {
            RefreshSeconds = 7,
            Thresholds = new QuotaThresholds(90, 80),
        };

        var fixedUp = broken.Normalised();

        Assert.Equal(120, fixedUp.RefreshSeconds);
        Assert.Equal(QuotaThresholds.Default, fixedUp.Thresholds);
    }

    [Fact]
    public void Normalised_KeepsValidValues()
    {
        var s = AppSettings.Default with { RefreshSeconds = 300, Thresholds = new QuotaThresholds(70, 90) };
        Assert.Equal(s, s.Normalised());
    }
}
```

- [ ] **Step 2: Run, expect compile failure** — `dotnet test tests/LimitTray.Tests -v q --filter "FullyQualifiedName~AppSettingsTests"` → `namespace 'LimitTray.Core.Settings' not found`.

- [ ] **Step 3: Implement**

```csharp
// src/LimitTray.Core/Settings/AppSettings.cs
namespace LimitTray.Core.Settings;

public enum ThemeMode { System, Dark, Light }
public enum LanguageMode { System, Turkish, English }
public enum TrayIconStyle { Ring, Number, DualBar }
public enum TrayIconSource { Highest, ClaudeSession, ClaudeWeekly, CodexSession, CodexWeekly }

/// <summary>
/// Where the colour changes. Caution is a multiple of 5 between 50 and 80; warning sits
/// at least 5 above it and at most 95. The UI never offers anything else, and the file
/// reader repairs anything else to the defaults.
/// </summary>
public sealed record QuotaThresholds(double Caution, double Warning)
{
    public static readonly QuotaThresholds Default = new(60, 85);

    public bool IsValid =>
        Caution >= 50 && Caution <= 80 && Caution % 5 == 0
        && Warning >= Caution + 5 && Warning <= 95;
}

public sealed record AppSettings(
    ThemeMode Theme,
    LanguageMode Language,
    bool GlassEffect,
    int RefreshSeconds,
    QuotaThresholds Thresholds,
    bool Notifications,
    TrayIconStyle TrayStyle,
    TrayIconSource TraySource,
    IReadOnlySet<string> ExpandedProviders)
{
    public static readonly int[] AllowedRefreshSeconds = { 60, 120, 300 };

    public static readonly AppSettings Default = new(
        ThemeMode.System, LanguageMode.System, GlassEffect: true, RefreshSeconds: 120,
        QuotaThresholds.Default, Notifications: true, TrayIconStyle.DualBar,
        TrayIconSource.Highest, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Returns a copy with every out-of-range value replaced by its default.</summary>
    public AppSettings Normalised() => this with
    {
        RefreshSeconds = Array.IndexOf(AllowedRefreshSeconds, RefreshSeconds) >= 0
            ? RefreshSeconds : Default.RefreshSeconds,
        Thresholds = Thresholds.IsValid ? Thresholds : QuotaThresholds.Default,
    };

    public bool Equals(AppSettings? other) =>
        other is not null
        && Theme == other.Theme && Language == other.Language
        && GlassEffect == other.GlassEffect && RefreshSeconds == other.RefreshSeconds
        && Thresholds == other.Thresholds && Notifications == other.Notifications
        && TrayStyle == other.TrayStyle && TraySource == other.TraySource
        && ExpandedProviders.SetEquals(other.ExpandedProviders);

    public override int GetHashCode() => HashCode.Combine(
        Theme, Language, GlassEffect, RefreshSeconds, Thresholds, Notifications, TrayStyle, TraySource);
}
```

Not: `Equals` elle yazıldı çünkü record'un varsayılan eşitliği `IReadOnlySet` referansını karşılaştırır ve `Normalised_KeepsValidValues` yanlış negatif verir.

- [ ] **Step 4: Run, expect PASS** — aynı komut, 12 test geçer.
- [ ] **Step 5: Commit** — `git add src/LimitTray.Core/Settings tests/LimitTray.Tests/Settings && git commit -m "feat(core): settings model with validated thresholds"`

---

### Task 2: Ayar dosyası ve deposu (`SettingsFile`, `SettingsStore`)

**Files:**
- Create: `src/LimitTray.Core/Settings/SettingsFile.cs`, `src/LimitTray.Core/Settings/SettingsStore.cs`
- Test: `tests/LimitTray.Tests/Settings/SettingsFileTests.cs`, `tests/LimitTray.Tests/Settings/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: Task 1 tipleri.
- Produces:
  - `static string SettingsFile.Write(AppSettings)`; `static AppSettings? SettingsFile.Read(string json)` (bozuksa `null`, bilinmeyen alan yok sayılır, eksik alan varsayılan, sonuç `Normalised()`).
  - `SettingsStore(Func<string?> read, Action<string> write)`, `static ForDefaultPath()`, `static string DefaultPath`, `AppSettings Load()`, `void Save(AppSettings)`, `bool LastSaveFailed { get; }`.

- [ ] **Step 1: Failing tests**

```csharp
// tests/LimitTray.Tests/Settings/SettingsFileTests.cs
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Settings;

public class SettingsFileTests
{
    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var original = AppSettings.Default with
        {
            Theme = ThemeMode.Light, Language = LanguageMode.English, GlassEffect = false,
            RefreshSeconds = 300, Thresholds = new QuotaThresholds(70, 90), Notifications = false,
            TrayStyle = TrayIconStyle.Number, TraySource = TrayIconSource.CodexWeekly,
            ExpandedProviders = new HashSet<string> { "claude" },
        };

        var restored = SettingsFile.Read(SettingsFile.Write(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Read_MissingFieldsFallBackToDefaults()
    {
        var restored = SettingsFile.Read("""{"version":1,"theme":"dark"}""");
        Assert.NotNull(restored);
        Assert.Equal(ThemeMode.Dark, restored!.Theme);
        Assert.Equal(AppSettings.Default.RefreshSeconds, restored.RefreshSeconds);
        Assert.Equal(AppSettings.Default.TrayStyle, restored.TrayStyle);
    }

    [Fact]
    public void Read_UnknownFieldIsIgnored() =>
        Assert.NotNull(SettingsFile.Read("""{"version":1,"futureThing":true}"""));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"version":99}""")]
    public void Read_UnusableDocumentIsNull(string json) => Assert.Null(SettingsFile.Read(json));

    [Fact]
    public void Read_InvalidThresholdsAreRepaired()
    {
        var restored = SettingsFile.Read("""{"version":1,"cautionPercent":90,"warningPercent":80}""");
        Assert.Equal(QuotaThresholds.Default, restored!.Thresholds);
    }

    [Fact]
    public void Read_UnknownEnumValueFallsBackToDefault()
    {
        var restored = SettingsFile.Read("""{"version":1,"trayStyle":"hologram"}""");
        Assert.Equal(AppSettings.Default.TrayStyle, restored!.TrayStyle);
    }

    [Fact]
    public void Write_ContainsOnlySettingKeys()
    {
        // The file sits on disk. Nothing but settings goes in: no token, no account,
        // no snapshot detail, nothing derived from either.
        var json = SettingsFile.Write(AppSettings.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        var allowed = new HashSet<string>
        {
            "version", "theme", "language", "glassEffect", "refreshSeconds",
            "cautionPercent", "warningPercent", "notifications", "trayStyle", "traySource",
            "expandedProviders",
        };
        Assert.Subset(allowed, keys);
    }
}
```

```csharp
// tests/LimitTray.Tests/Settings/SettingsStoreTests.cs
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Settings;

public class SettingsStoreTests
{
    [Fact]
    public void Load_MissingFileIsDefault()
    {
        var store = new SettingsStore(() => null, _ => { });
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Load_ReadThrowingIsDefault()
    {
        var store = new SettingsStore(() => throw new IOException("locked"), _ => { });
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Load_CorruptFileIsDefault()
    {
        var store = new SettingsStore(() => "{{{", _ => { });
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Save_WritesOnlyWhenChanged()
    {
        var writes = new List<string>();
        var store = new SettingsStore(() => null, writes.Add);

        store.Save(AppSettings.Default);
        store.Save(AppSettings.Default);
        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark });

        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public void Save_WriteFailureIsFlaggedNotThrown()
    {
        var store = new SettingsStore(() => null, _ => throw new UnauthorizedAccessException());

        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark });

        Assert.True(store.LastSaveFailed);
    }

    [Fact]
    public void Save_SuccessClearsTheFlag()
    {
        var fail = true;
        var store = new SettingsStore(() => null, _ => { if (fail) throw new IOException(); });

        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark });
        fail = false;
        store.Save(AppSettings.Default with { Theme = ThemeMode.Light });

        Assert.False(store.LastSaveFailed);
    }
}
```

- [ ] **Step 2: Run, expect compile failure** — `SettingsFile` / `SettingsStore` yok.

- [ ] **Step 3: Implement**

```csharp
// src/LimitTray.Core/Settings/SettingsFile.cs
using System.Buffers;
using System.Text.Json;

namespace LimitTray.Core.Settings;

/// <summary>
/// JSON for <see cref="AppSettings"/>. Only setting values are written; unknown keys
/// are ignored on read so a newer file survives an older build, and missing keys take
/// their defaults so an older file survives a newer build.
/// </summary>
public static class SettingsFile
{
    public const int Version = 1;

    public static string Write(AppSettings s)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteString("theme", s.Theme.ToString());
            w.WriteString("language", s.Language.ToString());
            w.WriteBoolean("glassEffect", s.GlassEffect);
            w.WriteNumber("refreshSeconds", s.RefreshSeconds);
            w.WriteNumber("cautionPercent", s.Thresholds.Caution);
            w.WriteNumber("warningPercent", s.Thresholds.Warning);
            w.WriteBoolean("notifications", s.Notifications);
            w.WriteString("trayStyle", s.TrayStyle.ToString());
            w.WriteString("traySource", s.TraySource.ToString());
            w.WriteStartArray("expandedProviders");
            foreach (var p in s.ExpandedProviders.Order(StringComparer.Ordinal)) w.WriteStringValue(p);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Null for anything that is not a version-1 settings object.</summary>
    public static AppSettings? Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number
                || v.GetInt32() != Version) return null;

            var d = AppSettings.Default;
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("expandedProviders", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) expanded.Add(item.GetString()!);

            return new AppSettings(
                Enum(root, "theme", d.Theme),
                Enum(root, "language", d.Language),
                Bool(root, "glassEffect", d.GlassEffect),
                Int(root, "refreshSeconds", d.RefreshSeconds),
                new QuotaThresholds(
                    Double(root, "cautionPercent", d.Thresholds.Caution),
                    Double(root, "warningPercent", d.Thresholds.Warning)),
                Bool(root, "notifications", d.Notifications),
                Enum(root, "trayStyle", d.TrayStyle),
                Enum(root, "traySource", d.TraySource),
                expanded).Normalised();
        }
    }

    private static T Enum<T>(JsonElement root, string key, T fallback) where T : struct, System.Enum =>
        root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String
        && System.Enum.TryParse<T>(e.GetString(), ignoreCase: true, out var parsed)
        && System.Enum.IsDefined(parsed)
            ? parsed : fallback;

    private static bool Bool(JsonElement root, string key, bool fallback) =>
        root.TryGetProperty(key, out var e)
        && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;

    private static int Int(JsonElement root, string key, int fallback) =>
        root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number
        && e.TryGetInt32(out var n) ? n : fallback;

    private static double Double(JsonElement root, string key, double fallback) =>
        root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : fallback;
}
```

```csharp
// src/LimitTray.Core/Settings/SettingsStore.cs
using System.Text;

namespace LimitTray.Core.Settings;

/// <summary>
/// Same contract as HistoryStore: never throws, an unusable file is a first run, a
/// lost write is reported through <see cref="LastSaveFailed"/> rather than by taking
/// the application down. The settings page shows one line when the flag is set.
/// </summary>
public sealed class SettingsStore
{
    private readonly Func<string?> _read;
    private readonly Action<string> _write;
    private string? _lastWritten;

    public SettingsStore(Func<string?> read, Action<string> write)
    {
        _read = read;
        _write = write;
    }

    public static SettingsStore ForDefaultPath() =>
        new(() => File.Exists(DefaultPath) ? File.ReadAllText(DefaultPath) : null,
            content => WriteFile(DefaultPath, content));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "limit-tray", "settings.json");

    public bool LastSaveFailed { get; private set; }

    public AppSettings Load()
    {
        string? content;
        try { content = _read(); }
        catch (Exception) { return AppSettings.Default; }

        if (string.IsNullOrWhiteSpace(content)) return AppSettings.Default;

        var settings = SettingsFile.Read(content);
        if (settings is null) return AppSettings.Default;

        _lastWritten = content;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var content = SettingsFile.Write(settings);
        if (string.Equals(content, _lastWritten, StringComparison.Ordinal)) return;

        try
        {
            _write(content);
            _lastWritten = content;
            LastSaveFailed = false;
        }
        catch (Exception)
        {
            LastSaveFailed = true;
        }
    }

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run, expect PASS** — `dotnet test tests/LimitTray.Tests -v q --filter "FullyQualifiedName~Settings"`.
- [ ] **Step 5: Commit** — `git commit -m "feat(core): settings file and store, failures flagged not thrown"`

---

### Task 3: Renk kuralı (`Theme`) ve ayarlanabilir eşikler

**Files:**
- Create: `src/LimitTray.Core/Presentation/Theme.cs`
- Modify: `src/LimitTray.Core/Presentation/QuotaFormatter.cs:14-22`, `src/LimitTray.Core/Presentation/QuotaAlerts.cs`
- Test: `tests/LimitTray.Tests/Presentation/ThemeTests.cs`, `tests/LimitTray.Tests/Presentation/QuotaAlertsTests.cs` (ekle)

**Interfaces:**
- Consumes: `QuotaThresholds` (Task 1).
- Produces:
  - `readonly record struct Rgb(byte R, byte G, byte B)`
  - `static class Palette { ClaudeBrand, ClaudeBrandLight, CodexBrand, CodexBrandLight, NeutralBrand, Caution, Warning, Muted }`
  - `static class Theme { Rgb BrandFor(string provider); Rgb ColourFor(string provider, QuotaSeverity severity, HealthState health); byte OpacityFor(HealthState health) }`
  - `QuotaFormatter.SeverityFor(double percent, QuotaThresholds thresholds)`; eski tek parametreli overload `QuotaThresholds.Default` kullanır.
  - `QuotaAlerts(QuotaThresholds thresholds)` + parametresiz ctor; `void UpdateThresholds(QuotaThresholds)`.

- [ ] **Step 1: Failing tests**

```csharp
// tests/LimitTray.Tests/Presentation/ThemeTests.cs
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class ThemeTests
{
    [Theory]
    [InlineData("claude", QuotaSeverity.Normal, HealthState.Fresh, "D97757")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.Fresh, "10A37F")]
    [InlineData("other", QuotaSeverity.Normal, HealthState.Fresh, "50C878")]
    [InlineData("claude", QuotaSeverity.Caution, HealthState.Fresh, "E8B84A")]
    [InlineData("codex", QuotaSeverity.Warning, HealthState.Fresh, "E5484D")]
    [InlineData("claude", QuotaSeverity.Normal, HealthState.Stale, "6B7280")]
    [InlineData("claude", QuotaSeverity.Warning, HealthState.Stale, "6B7280")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.RateLimited, "6B7280")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.AuthMissing, "6B7280")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.ProtocolBroken, "6B7280")]
    public void ColourFor_FollowsTheSingleRule(string provider, QuotaSeverity severity, HealthState health, string hex)
    {
        var c = Theme.ColourFor(provider, severity, health);
        Assert.Equal(hex, $"{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    [Fact]
    public void OpacityFor_StaleIsDimmed()
    {
        Assert.Equal(255, Theme.OpacityFor(HealthState.Fresh));
        Assert.Equal(140, Theme.OpacityFor(HealthState.Stale));
    }

    [Theory]
    [InlineData(59.9, QuotaSeverity.Normal)]
    [InlineData(60, QuotaSeverity.Caution)]
    [InlineData(85, QuotaSeverity.Caution)]
    [InlineData(85.1, QuotaSeverity.Warning)]
    public void SeverityFor_DefaultThresholdsUnchanged(double percent, QuotaSeverity expected) =>
        Assert.Equal(expected, QuotaFormatter.SeverityFor(percent));

    [Fact]
    public void SeverityFor_HonoursCustomThresholds()
    {
        var t = new QuotaThresholds(70, 90);
        Assert.Equal(QuotaSeverity.Normal, QuotaFormatter.SeverityFor(65, t));
        Assert.Equal(QuotaSeverity.Caution, QuotaFormatter.SeverityFor(70, t));
        Assert.Equal(QuotaSeverity.Warning, QuotaFormatter.SeverityFor(91, t));
    }
}
```

`QuotaAlertsTests.cs` dosyasına ekle (mevcut yardımcıları kullan; dosyada `Fresh(...)` benzeri bir snapshot fabrikası varsa onu kullan, yoksa aşağıdaki gibi yaz):

```csharp
    [Fact]
    public void Inspect_UsesConfiguredWarningThreshold()
    {
        var alerts = new QuotaAlerts(new QuotaThresholds(50, 70));
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new QuotaSnapshot("claude",
            new QuotaWindow(75, now.AddHours(1), TimeSpan.FromHours(5)), null, HealthState.Fresh, now, null);

        Assert.Single(alerts.Inspect(snapshot));
    }

    [Fact]
    public void UpdateThresholds_RearmsOnlyWhenBelowTheNewLine()
    {
        var alerts = new QuotaAlerts(new QuotaThresholds(60, 85));
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var at90 = new QuotaSnapshot("claude",
            new QuotaWindow(90, now.AddHours(1), TimeSpan.FromHours(5)), null, HealthState.Fresh, now, null);

        Assert.Single(alerts.Inspect(at90));
        alerts.UpdateThresholds(new QuotaThresholds(60, 95));
        Assert.Empty(alerts.Inspect(at90));   // 90 is below 95: silent, and re-armed
        alerts.UpdateThresholds(new QuotaThresholds(60, 85));
        Assert.Single(alerts.Inspect(at90));  // crossed again after re-arming
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

```csharp
// src/LimitTray.Core/Presentation/Theme.cs
using LimitTray.Core.Model;

namespace LimitTray.Core.Presentation;

public readonly record struct Rgb(byte R, byte G, byte B);

public static class Palette
{
    public static readonly Rgb ClaudeBrand = new(0xD9, 0x77, 0x57);
    public static readonly Rgb ClaudeBrandLight = new(0xF2, 0xA2, 0x7E);
    public static readonly Rgb CodexBrand = new(0x10, 0xA3, 0x7F);
    public static readonly Rgb CodexBrandLight = new(0x5E, 0xE3, 0xB6);
    /// <summary>The v0.2 green, kept for a provider without a brand colour.</summary>
    public static readonly Rgb NeutralBrand = new(0x50, 0xC8, 0x78);
    public static readonly Rgb Caution = new(0xE8, 0xB8, 0x4A);
    public static readonly Rgb Warning = new(0xE5, 0x48, 0x4D);
    public static readonly Rgb Muted = new(0x6B, 0x72, 0x80);
}

/// <summary>
/// The one place a colour decision is made. Brand colour means "fresh and fine";
/// caution and warning take over at the thresholds; anything that is not fresh is
/// muted. The popup bar, ring, percentage text and the tray icon all call this, so
/// they cannot disagree.
/// </summary>
public static class Theme
{
    public static Rgb BrandFor(string provider) => provider switch
    {
        "claude" => Palette.ClaudeBrand,
        "codex" => Palette.CodexBrand,
        _ => Palette.NeutralBrand,
    };

    public static Rgb BrandLightFor(string provider) => provider switch
    {
        "claude" => Palette.ClaudeBrandLight,
        "codex" => Palette.CodexBrandLight,
        _ => Palette.NeutralBrand,
    };

    public static Rgb ColourFor(string provider, QuotaSeverity severity, HealthState health)
    {
        if (health != HealthState.Fresh) return Palette.Muted;

        return severity switch
        {
            QuotaSeverity.Warning => Palette.Warning,
            QuotaSeverity.Caution => Palette.Caution,
            _ => BrandFor(provider),
        };
    }

    public static byte OpacityFor(HealthState health) => health == HealthState.Stale ? (byte)140 : (byte)255;
}
```

`QuotaFormatter.cs` içinde:

```csharp
    public static QuotaSeverity SeverityFor(double percent) => SeverityFor(percent, QuotaThresholds.Default);

    public static QuotaSeverity SeverityFor(double percent, QuotaThresholds thresholds) => percent switch
    {
        var p when p > thresholds.Warning => QuotaSeverity.Warning,
        var p when p >= thresholds.Caution => QuotaSeverity.Caution,
        _ => QuotaSeverity.Normal,
    };
```

`CautionThreshold`/`WarningThreshold` sabitleri kalsın ama `QuotaThresholds.Default`'tan okusun (`= QuotaThresholds.Default.Caution` şeklinde `static readonly`; `const` olamaz). `using LimitTray.Core.Settings;` ekle.

`QuotaAlerts.cs` içinde:

```csharp
    private QuotaThresholds _thresholds;

    public QuotaAlerts() : this(QuotaThresholds.Default) { }
    public QuotaAlerts(QuotaThresholds thresholds) => _thresholds = thresholds;

    /// <summary>
    /// Raising the warning line above a window that already fired re-arms it: the user
    /// asked to be told later, so they will be told again when it crosses the new line.
    /// </summary>
    public void UpdateThresholds(QuotaThresholds thresholds)
    {
        lock (_gate) _thresholds = thresholds;
    }
```

ve `Consider` içindeki satır: `var above = QuotaFormatter.SeverityFor(window.Percent, _thresholds) == QuotaSeverity.Warning;`

- [ ] **Step 4: Run full suite, expect PASS** — `dotnet test tests/LimitTray.Tests -v q`. Mevcut 158 + yeni testler.
- [ ] **Step 5: Commit** — `git commit -m "feat(core): single colour rule and configurable thresholds"`

---

### Task 4: Tray ikon modeli (`TrayIconModel`)

**Files:**
- Create: `src/LimitTray.Core/Presentation/TrayIconModel.cs`
- Test: `tests/LimitTray.Tests/Presentation/TrayIconModelTests.cs`

**Interfaces:**
- Consumes: `Theme`, `QuotaFormatter.SeverityFor(percent, thresholds)`, `AppSettings`.
- Produces:
  - `record TrayBar(double Percent, Rgb Colour, byte Opacity)`
  - `record TrayIconModel(TrayIconStyle Style, TrayBar? Primary, TrayBar? Left, TrayBar? Right, bool HasUnhealthy)`
  - `static TrayIconModel TrayIconModelBuilder.Build(IReadOnlyList<QuotaSnapshot> snapshots, AppSettings settings)`

Kurallar: Ring/Number → `Primary`; `Highest` kaynağı sağlıklı (Fresh/Stale) snapshot'lar arasından en yüksek pencere, rengi o sağlayıcı/severity/health ile; belirli kaynak → o sağlayıcının o penceresi, yoksa veya sağlıksızsa `Primary = null` ve `HasUnhealthy = true` (soru işareti; başka veriyle örtülmez). DualBar → `Left` claude, `Right` codex, her biri kendi en yüksek penceresi; kaynak yok sayılır; sağlayıcı eksik/sağlıksızsa o taraf `null`.

- [ ] **Step 1: Failing tests**

```csharp
// tests/LimitTray.Tests/Presentation/TrayIconModelTests.cs
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class TrayIconModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static QuotaSnapshot Snap(string provider, double session, double weekly, HealthState health = HealthState.Fresh) =>
        new(provider,
            new QuotaWindow(session, Now.AddHours(1), TimeSpan.FromHours(5)),
            new QuotaWindow(weekly, Now.AddDays(1), TimeSpan.FromDays(7)),
            health, Now, null);

    private static AppSettings With(TrayIconStyle style, TrayIconSource source = TrayIconSource.Highest) =>
        AppSettings.Default with { TrayStyle = style, TraySource = source };

    [Fact]
    public void Ring_HighestPicksTheFullestWindowAcrossProviders()
    {
        var m = TrayIconModelBuilder.Build(new[] { Snap("claude", 50, 25), Snap("codex", 39, 52) }, With(TrayIconStyle.Ring));

        Assert.Equal(TrayIconStyle.Ring, m.Style);
        Assert.Equal(52, m.Primary!.Percent);
        Assert.Equal(Palette.CodexBrand, m.Primary.Colour);
        Assert.False(m.HasUnhealthy);
    }

    [Fact]
    public void Number_ColourFollowsSeverity()
    {
        var m = TrayIconModelBuilder.Build(new[] { Snap("claude", 93, 25) }, With(TrayIconStyle.Number));
        Assert.Equal(Palette.Warning, m.Primary!.Colour);
    }

    [Fact]
    public void SpecificSource_UsesThatWindowOnly()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { Snap("claude", 50, 25), Snap("codex", 39, 52) },
            With(TrayIconStyle.Ring, TrayIconSource.ClaudeWeekly));
        Assert.Equal(25, m.Primary!.Percent);
        Assert.Equal(Palette.ClaudeBrand, m.Primary.Colour);
    }

    [Fact]
    public void SpecificSource_UnhealthyProviderIsNotReplacedByAnotherProvider()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { QuotaSnapshot.Unhealthy("claude", HealthState.RateLimited, Now, "429"), Snap("codex", 39, 52) },
            With(TrayIconStyle.Ring, TrayIconSource.ClaudeSession));
        Assert.Null(m.Primary);
        Assert.True(m.HasUnhealthy);
    }

    [Fact]
    public void Highest_IgnoresUnhealthyButKeepsStale()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { QuotaSnapshot.Unhealthy("claude", HealthState.AuthMissing, Now, "x"), Snap("codex", 39, 52, HealthState.Stale) },
            With(TrayIconStyle.Ring));
        Assert.Equal(52, m.Primary!.Percent);
        Assert.Equal(Palette.Muted, m.Primary.Colour);
        Assert.Equal(140, m.Primary.Opacity);
        Assert.True(m.HasUnhealthy);
    }

    [Fact]
    public void NoData_PrimaryIsNull()
    {
        var m = TrayIconModelBuilder.Build(Array.Empty<QuotaSnapshot>(), With(TrayIconStyle.Number));
        Assert.Null(m.Primary);
        Assert.False(m.HasUnhealthy);
    }

    [Fact]
    public void DualBar_LeftClaudeRightCodex_SourceIgnored()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { Snap("claude", 50, 25), Snap("codex", 39, 52) },
            With(TrayIconStyle.DualBar, TrayIconSource.CodexWeekly));
        Assert.Equal(50, m.Left!.Percent);
        Assert.Equal(Palette.ClaudeBrand, m.Left.Colour);
        Assert.Equal(52, m.Right!.Percent);
        Assert.Equal(Palette.CodexBrand, m.Right.Colour);
        Assert.Null(m.Primary);
    }

    [Fact]
    public void DualBar_MissingProviderSideIsNull()
    {
        var m = TrayIconModelBuilder.Build(new[] { Snap("codex", 39, 52) }, With(TrayIconStyle.DualBar));
        Assert.Null(m.Left);
        Assert.NotNull(m.Right);
    }

    [Fact]
    public void CustomThresholds_ChangeTheColour()
    {
        var settings = With(TrayIconStyle.Ring) with { Thresholds = new QuotaThresholds(50, 70) };
        var m = TrayIconModelBuilder.Build(new[] { Snap("claude", 55, 10) }, settings);
        Assert.Equal(Palette.Caution, m.Primary!.Colour);
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

```csharp
// src/LimitTray.Core/Presentation/TrayIconModel.cs
using LimitTray.Core.Model;
using LimitTray.Core.Settings;

namespace LimitTray.Core.Presentation;

public sealed record TrayBar(double Percent, Rgb Colour, byte Opacity);

/// <summary>
/// What the tray icon draws, decided here so the renderer is only pixels. A null bar
/// means "draw the question mark": the value is unknown, and unknown is never zero.
/// </summary>
public sealed record TrayIconModel(
    TrayIconStyle Style,
    TrayBar? Primary,
    TrayBar? Left,
    TrayBar? Right,
    bool HasUnhealthy);

public static class TrayIconModelBuilder
{
    public static TrayIconModel Build(IReadOnlyList<QuotaSnapshot> snapshots, AppSettings settings)
    {
        var unhealthy = QuotaFormatter.HasUnhealthy(snapshots);

        if (settings.TrayStyle == TrayIconStyle.DualBar)
            return new TrayIconModel(
                TrayIconStyle.DualBar, null,
                Fullest(Find(snapshots, "claude"), settings.Thresholds),
                Fullest(Find(snapshots, "codex"), settings.Thresholds),
                unhealthy);

        var primary = settings.TraySource switch
        {
            TrayIconSource.Highest => Highest(snapshots, settings.Thresholds),
            TrayIconSource.ClaudeSession => Pick(Find(snapshots, "claude"), WindowKind.Session, settings.Thresholds),
            TrayIconSource.ClaudeWeekly => Pick(Find(snapshots, "claude"), WindowKind.Weekly, settings.Thresholds),
            TrayIconSource.CodexSession => Pick(Find(snapshots, "codex"), WindowKind.Session, settings.Thresholds),
            TrayIconSource.CodexWeekly => Pick(Find(snapshots, "codex"), WindowKind.Weekly, settings.Thresholds),
            _ => null,
        };

        // A chosen source that cannot be shown is itself an unhealthy state for the icon,
        // even when the other provider is fine. The icon must not quietly show the other one.
        if (primary is null && settings.TraySource != TrayIconSource.Highest && snapshots.Count > 0)
            unhealthy = true;

        return new TrayIconModel(settings.TrayStyle, primary, null, null, unhealthy);
    }

    private static QuotaSnapshot? Find(IReadOnlyList<QuotaSnapshot> snapshots, string provider) =>
        snapshots.FirstOrDefault(s => s.Provider == provider);

    private static bool Usable(QuotaSnapshot? s) =>
        s is not null && s.Health is HealthState.Fresh or HealthState.Stale;

    private static TrayBar Bar(QuotaSnapshot s, QuotaWindow w, QuotaThresholds t) =>
        new(w.Percent,
            Theme.ColourFor(s.Provider, QuotaFormatter.SeverityFor(w.Percent, t), s.Health),
            Theme.OpacityFor(s.Health));

    private static TrayBar? Pick(QuotaSnapshot? s, WindowKind kind, QuotaThresholds t)
    {
        if (!Usable(s)) return null;
        var w = kind == WindowKind.Session ? s!.Session : s!.Weekly;
        return w is null ? null : Bar(s, w, t);
    }

    private static TrayBar? Fullest(QuotaSnapshot? s, QuotaThresholds t)
    {
        if (!Usable(s)) return null;
        QuotaWindow? best = null;
        foreach (var w in new[] { s!.Session, s.Weekly })
            if (w is not null && (best is null || w.Percent > best.Percent)) best = w;
        return best is null ? null : Bar(s, best, t);
    }

    private static TrayBar? Highest(IReadOnlyList<QuotaSnapshot> snapshots, QuotaThresholds t)
    {
        TrayBar? best = null;
        foreach (var s in snapshots)
        {
            var candidate = Fullest(s, t);
            if (candidate is not null && (best is null || candidate.Percent > best.Percent)) best = candidate;
        }
        return best;
    }
}
```

- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `git commit -m "feat(core): tray icon model for ring, number and dual bar styles"`

---

### Task 5: Toplayıcılarda `RequestRefresh` ve canlı aralık

**Files:**
- Modify: `src/LimitTray.Core/Collectors/IQuotaCollector.cs`, `src/LimitTray.Core/Claude/ClaudeCollector.cs`, `src/LimitTray.Core/Codex/CodexCollector.cs`
- Test: `tests/LimitTray.Tests/Claude/ClaudeCollectorTests.cs`, `tests/LimitTray.Tests/Codex/CodexCollectorTests.cs` (ekle)

**Interfaces:**
- Produces: `IQuotaCollector.RequestRefresh()`; `ClaudeCollector(..., Func<TimeSpan>? interval = null)` (varsayılan 120 sn); `CodexCollector.RequestRefresh()` aktif süreç varsa `account/rateLimits/read` gönderir, yoksa sessiz.

- [ ] **Step 1: Failing tests**

`ClaudeCollectorTests.cs` içine ekle. Mevcut `Build` yardımcısına `Func<TimeSpan>? interval = null` parametresi ekle ve ctor'a geçir.

```csharp
    [Fact]
    public async Task Watch_UsesTheInjectedInterval()
    {
        var delays = new List<TimeSpan>();
        var transport = new FakeTransport(Ok(), Ok());
        var collector = Build(transport, "tok", delays, () => TimeSpan.FromSeconds(300));

        await Take(collector, 2);

        Assert.Equal(TimeSpan.FromSeconds(300), delays[0]);
    }

    [Fact]
    public async Task RequestRefresh_CutsTheWaitShort()
    {
        // The fake delay completes only when its token is cancelled, which is exactly
        // what RequestRefresh must do to the pending wait.
        var transport = new FakeTransport(Ok(), Ok());
        var collector = new ClaudeCollector(transport, new ClaudeCredentialReader(() => "tok"), () => Now,
            (d, token) =>
            {
                var tcs = new TaskCompletionSource();
                token.Register(() => tcs.TrySetCanceled(token));
                return tcs.Task;
            });

        var seen = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            await foreach (var s in collector.Watch(cts.Token))
            {
                seen.Add(s);
                if (seen.Count == 1) collector.RequestRefresh();
                if (seen.Count == 2) { cts.Cancel(); break; }
            }
        });

        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, seen.Count);
        Assert.Equal(2, transport.SeenHeaders.Count);
    }

    [Fact]
    public async Task RequestRefresh_IsIgnoredWhileBackingOff()
    {
        var transport = new FakeTransport(RateLimited(), Ok());
        var waited = new TaskCompletionSource();
        var collector = new ClaudeCollector(transport, new ClaudeCredentialReader(() => "tok"), () => Now,
            (d, token) =>
            {
                waited.TrySetResult();
                var tcs = new TaskCompletionSource();
                token.Register(() => tcs.TrySetCanceled(token));
                return tcs.Task;
            });

        using var cts = new CancellationTokenSource();
        var enumerator = collector.Watch(cts.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());           // the 429 snapshot
        var second = enumerator.MoveNextAsync();
        await waited.Task.WaitAsync(TimeSpan.FromSeconds(5));   // now inside the backoff wait
        collector.RequestRefresh();
        await Task.Delay(100);
        Assert.False(second.IsCompleted);                        // still waiting: refresh ignored
        cts.Cancel();
    }
```

`Ok()` ve `RateLimited()` dosyada zaten yoksa şu şekilde ekle:

```csharp
    private static HttpTransportResult Ok() => new(200,
        """{"five_hour":{"utilization":50,"resets_at":"2026-09-04T00:00:00Z"},"seven_day":{"utilization":25,"resets_at":"2026-09-10T00:00:00Z"}}""");
    private static HttpTransportResult RateLimited() => new(429, "");
```

(`HttpTransportResult` ctor imzasını `src/LimitTray.Core/Http/HttpTransportResult.cs` içinden doğrula; parametre adları farklıysa uyarla. Claude yanıt JSON'unun alan adlarını `ClaudeUsageParserTests.cs` içindeki örnekten kopyala.)

`CodexCollectorTests.cs` içine ekle (dosyadaki `FakeProcess` sınıfını kullan; gönderilen satırları tutan bir `Sent` listesi yoksa ekle):

```csharp
    [Fact]
    public async Task RequestRefresh_SendsReadToTheActiveProcess()
    {
        var process = new FakeProcess(/* initialize response line, then block */);
        var collector = new CodexCollector(() => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        using var cts = new CancellationTokenSource();
        var enumerator = collector.Watch(cts.Token).GetAsyncEnumerator();
        var pump = enumerator.MoveNextAsync();
        await process.InitializedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var before = process.Sent.Count(l => l.Contains("account/rateLimits/read"));
        collector.RequestRefresh();
        await Task.Delay(100);

        Assert.Equal(before + 1, process.Sent.Count(l => l.Contains("account/rateLimits/read")));
        cts.Cancel();
    }

    [Fact]
    public void RequestRefresh_WithoutAProcessIsSilent()
    {
        var collector = new CodexCollector(() => throw new InvalidOperationException(), () => Now, (_, _) => Task.CompletedTask, () => null);
        collector.RequestRefresh(); // must not throw
    }
```

`FakeProcess` mevcut yapısına göre: `SendAsync` satırı `Sent`'e ekler; `ReadLines` initialize yanıtını (`{"jsonrpc":"2.0","id":1,"result":{}}`) döner, ardından `InitializedSignal`'i set edip token iptaline kadar bekler.

- [ ] **Step 2: Run, expect compile failure** (`RequestRefresh` yok).

- [ ] **Step 3: Implement**

`IQuotaCollector.cs`:

```csharp
public interface IQuotaCollector
{
    string Provider { get; }
    IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);

    /// <summary>
    /// Asks for a fetch now instead of at the next tick. Best effort: ignored while a
    /// provider is backing off after a 429, and a no-op when nothing is running.
    /// </summary>
    void RequestRefresh();
}
```

`ClaudeCollector.cs`: alanlar ve ctor:

```csharp
    private readonly Func<TimeSpan> _interval;
    private CancellationTokenSource _wake = new();
    private volatile bool _backingOff;

    public ClaudeCollector(
        IHttpTransport transport,
        ClaudeCredentialReader credentials,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<TimeSpan>? interval = null)
    {
        _transport = transport;
        _credentials = credentials;
        _clock = clock;
        _delay = delay;
        _interval = interval ?? (() => NormalInterval);
    }

    public void RequestRefresh()
    {
        // Backoff is the endpoint telling us to stop; a manual click does not override it.
        if (_backingOff) return;
        _wake.Cancel();
    }
```

`Watch` içindeki bekleme bloğu:

```csharp
            TimeSpan wait;
            if (rateLimited)
            {
                wait = backoff;
                backoff = backoff >= MaxBackoff ? MaxBackoff : Min(backoff + backoff, MaxBackoff);
            }
            else
            {
                wait = _interval();
                backoff = FirstBackoff;
            }
            _backingOff = rateLimited;

            var wake = new CancellationTokenSource();
            _wake = wake;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, wake.Token);
            try
            {
                await _delay(wait, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Woken by RequestRefresh: fall through to the next fetch.
            }
```

`CodexCollector.cs`:

```csharp
    private volatile IJsonRpcProcess? _active;

    public void RequestRefresh()
    {
        var process = _active;
        if (process is null) return;
        _ = process.SendAsync(ReadMessage, CancellationToken.None)
            .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }
```

`RunSession` içinde initialize yanıtı alındığında (`initialized = true;` satırından sonra) `_active = process;`; `finally` bloğunda `_active = null;`. Ayrıca `InitializeMessage` içindeki `"version":"0.2.0"` → `"0.3.0"`.

- [ ] **Step 4: Run full suite, expect PASS.**
- [ ] **Step 5: Commit** — `git commit -m "feat(core): manual refresh and live interval on both collectors"`

---

### Task 6: Yeni metinler (`Strings`)

**Files:**
- Modify: `src/LimitTray.Core/Presentation/Strings.cs`

**Interfaces:**
- Produces (her biri `required string`, İngilizce / Türkçe):

| Property | English | Türkçe |
|---|---|---|
| `Refresh` | Refresh | Yenile |
| `Settings` | Settings | Ayarlar |
| `BackToPanel` | Back to panel | Panele dön |
| `OpenTerminal` | Open in terminal | Terminalde aç |
| `TerminalFailed` | Could not open a terminal | Terminal açılamadı |
| `StaleBadge` | STALE · {0} | ESKİ · {0} |
| `LastUpdated` | Last updated {0} | Son güncelleme {0} |
| `GroupAppearance` | Appearance | Görünüm |
| `GroupData` | Data | Veri |
| `GroupTray` | Tray icon | Tray ikonu |
| `GroupSystem` | System | Sistem |
| `Theme` | Theme | Tema |
| `ThemeSystem` | System | Sistem |
| `ThemeDark` | Dark | Koyu |
| `ThemeLight` | Light | Açık |
| `Language` | Language | Dil |
| `LanguageSystem` | System | Sistem |
| `GlassEffect` | Glass effect | Cam efekti |
| `RefreshInterval` | Refresh interval | Yenileme aralığı |
| `Seconds` | {0} s | {0} sn |
| `Minutes` | {0} min | {0} dk |
| `CautionThreshold` | Caution threshold | Dikkat eşiği |
| `WarningThreshold` | Warning threshold | Uyarı eşiği |
| `Notifications` | Notifications | Bildirim |
| `TrayStyle` | Icon style | İkon stili |
| `TrayStyleRing` | Ring | Halka |
| `TrayStyleNumber` | Number | Sayı |
| `TrayStyleDualBar` | Two bars | İki bar |
| `TraySource` | Icon source | İkon kaynağı |
| `TraySourceHighest` | Fullest window | En dolu pencere |
| `TraySourceClaudeSession` | Claude 5h | Claude 5s |
| `TraySourceClaudeWeekly` | Claude 7d | Claude 7g |
| `TraySourceCodexSession` | Codex 5h | Codex 5s |
| `TraySourceCodexWeekly` | Codex 7d | Codex 7g |
| `SettingsSaveFailed` | Settings could not be saved | Ayarlar kaydedilemedi |
| `ResetShort` | ↺ {0} | ↺ {0} |

- [ ] **Step 1:** Her satırı `English` ve `Turkish` nesnelerine ve `required string` property listesine ekle. Türkçe değerlerde gerçek Türkçe karakter kullan (dosyanın bilinçli istisnası).
- [ ] **Step 2: Build** — `dotnet build LimitTray.sln -c Release`; `required` property eksikse derleme hatası verir, ikisi de tam olmalı.
- [ ] **Step 3: Run full suite, expect PASS.**
- [ ] **Step 4: Commit** — `git commit -m "feat(core): strings for the settings page and panel interactions"`

---

### Task 7: Tray ikonu çizimi (`TrayIconRenderer` üç stil)

**Files:**
- Modify: `src/LimitTray.App/TrayIconRenderer.cs` (tamamen yeniden), `src/LimitTray.App/App.xaml.cs:196-215` (`UpdateTray`)
- Create: `src/LimitTray.App/Brushes.cs`

**Interfaces:**
- Consumes: `TrayIconModel`, `TrayIconModelBuilder.Build`, `Rgb`.
- Produces: `static RenderedIcon TrayIconRenderer.Render(TrayIconModel model)`; `static Color Brushes.ToDrawing(Rgb, byte alpha = 255)`; `static System.Windows.Media.Color Brushes.ToMedia(Rgb, byte alpha = 255)`; `static SolidColorBrush Brushes.Solid(Rgb, byte alpha = 255)`.

- [ ] **Step 1: `Brushes.cs`**

```csharp
// src/LimitTray.App/Brushes.cs
using LimitTray.Core.Presentation;

namespace LimitTray.App;

/// <summary>Core decides colours as Rgb; this is the only place they become UI types.</summary>
public static class Brushes
{
    public static System.Drawing.Color ToDrawing(Rgb c, byte alpha = 255) =>
        System.Drawing.Color.FromArgb(alpha, c.R, c.G, c.B);

    public static System.Windows.Media.Color ToMedia(Rgb c, byte alpha = 255) =>
        System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B);

    public static System.Windows.Media.SolidColorBrush Solid(Rgb c, byte alpha = 255)
    {
        var brush = new System.Windows.Media.SolidColorBrush(ToMedia(c, alpha));
        brush.Freeze();
        return brush;
    }
}
```

- [ ] **Step 2: `TrayIconRenderer.cs`** — `Render(TrayIconModel model)`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.App;

/// <summary>
/// Turns a <see cref="TrayIconModel"/> into pixels. No decision about what to show is
/// made here; that is the model's job and it is tested. A null bar is drawn as the
/// question mark, never as an empty gauge, because empty reads as zero.
/// </summary>
public static class TrayIconRenderer
{
    private const int Size = 32;
    private const float StartAngle = 135f;
    private const float TotalSweep = 270f;

    public static RenderedIcon Render(TrayIconModel model)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            switch (model.Style)
            {
                case TrayIconStyle.Number: DrawNumber(g, model); break;
                case TrayIconStyle.DualBar: DrawDualBar(g, model); break;
                default: DrawRing(g, model); break;
            }
        }
        return new RenderedIcon(bitmap.GetHicon());
    }

    private static void DrawRing(Graphics g, TrayIconModel m)
    {
        var rect = new Rectangle(3, 3, Size - 7, Size - 7);
        using var track = new Pen(TrackColour(m.HasUnhealthy), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(track, rect, StartAngle, TotalSweep);

        if (m.Primary is null) { DrawQuestionMark(g); return; }

        using var arc = new Pen(Brushes.ToDrawing(m.Primary.Colour, m.Primary.Opacity), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var sweep = (float)(Math.Clamp(m.Primary.Percent, 0, 100) / 100.0 * TotalSweep);
        if (sweep > 0) g.DrawArc(arc, rect, StartAngle, sweep);
    }

    private static void DrawNumber(Graphics g, TrayIconModel m)
    {
        var rect = new Rectangle(1, 1, Size - 2, Size - 2);
        using var path = RoundedRect(rect, 7);

        if (m.Primary is null)
        {
            using var dark = new SolidBrush(Color.FromArgb(200, 60, 64, 72));
            g.FillPath(dark, path);
            DrawQuestionMark(g);
            return;
        }

        using var fill = new SolidBrush(Brushes.ToDrawing(m.Primary.Colour, m.Primary.Opacity));
        g.FillPath(fill, path);

        var percent = (int)Math.Round(Math.Clamp(m.Primary.Percent, 0, 100), MidpointRounding.AwayFromZero);
        if (percent >= 100)
        {
            // A filled block, not "100": three digits do not fit at 16 px and a
            // truncated "10" would be a lie.
            using var block = new SolidBrush(Contrast(m.Primary.Colour));
            g.FillRectangle(block, new Rectangle(9, 9, Size - 18, Size - 18));
            return;
        }

        using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Contrast(m.Primary.Colour));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(percent.ToString(System.Globalization.CultureInfo.InvariantCulture), font, text, new RectangleF(0, 1, Size, Size), format);
    }

    private static void DrawDualBar(Graphics g, TrayIconModel m)
    {
        DrawVerticalBar(g, m.Left, new Rectangle(4, 3, 10, Size - 6));
        DrawVerticalBar(g, m.Right, new Rectangle(Size - 14, 3, 10, Size - 6));
        if (m.Left is null && m.Right is null) DrawQuestionMark(g);
    }

    private static void DrawVerticalBar(Graphics g, TrayBar? bar, Rectangle track)
    {
        using var trackPath = RoundedRect(track, 3);
        using var trackBrush = new SolidBrush(bar is null && true ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(70, 255, 255, 255));
        g.FillPath(trackBrush, trackPath);

        if (bar is null) return;

        var height = (int)Math.Round(Math.Clamp(bar.Percent, 0, 100) / 100.0 * track.Height);
        if (height <= 0) return;
        var fill = new Rectangle(track.X, track.Bottom - height, track.Width, height);
        using var fillPath = RoundedRect(fill, 3);
        using var fillBrush = new SolidBrush(Brushes.ToDrawing(bar.Colour, bar.Opacity));
        g.FillPath(fillBrush, fillPath);
    }

    private static void DrawQuestionMark(Graphics g)
    {
        using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(230, 200, 200, 200));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("?", font, brush, new RectangleF(0, 0, Size, Size), format);
    }

    private static Color TrackColour(bool hasUnhealthy) =>
        hasUnhealthy ? Color.FromArgb(150, 229, 72, 77) : Color.FromArgb(70, 255, 255, 255);

    private static Color Contrast(Rgb c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? Color.FromArgb(255, 20, 18, 10) : Color.White;

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
```

Eski `ColorFor(QuotaSeverity)` kaldırılır; `QuotaPopup` bu turda onu kullanıyor, Task 9'da popup yeniden yazılana kadar derlemenin kırılmaması için `QuotaPopup.cs`'teki üç `TrayIconRenderer.ColorFor(...)` çağrısını geçici olarak `Brushes.ToDrawing(Theme.ColourFor(snapshot.Provider, QuotaFormatter.SeverityFor(window.Percent), snapshot.Health))` ile değiştir.

- [ ] **Step 3: `App.xaml.cs` `UpdateTray`** — `TrayIconRenderer.Render(QuotaFormatter.HighestPercent(...), QuotaFormatter.HasUnhealthy(...))` yerine `TrayIconRenderer.Render(TrayIconModelBuilder.Build(snapshots, _settings))`. `_settings` alanı şimdilik `AppSettings.Default` (Task 8'de dosyadan yüklenecek). `OnStartup` içindeki ilk ikon: `TrayIconRenderer.Render(TrayIconModelBuilder.Build(Array.Empty<QuotaSnapshot>(), _settings))`.

- [ ] **Step 4: Build + tests** — `dotnet build LimitTray.sln -c Release` sıfır uyarı; `dotnet test` yeşil.

- [ ] **Step 5: Elle doğrulama** — Uygulamayı çalıştır (`dotnet run --project src/LimitTray.App`). `_settings` içinde `TrayStyle`'ı geçici olarak üç değerle sırayla değiştirip her birinde tray'e bak: DualBar'da iki renkli bar, Number'da rakam, Ring'de halka. `%LOCALAPPDATA%\limit-tray\history.json` geçici olarak taşınıp uygulama başlatılınca soru işareti görülmeli. Gözlemi commit mesajına yaz.

- [ ] **Step 6: Commit** — `git commit -m "feat(app): tray icon renders the model in ring, number and dual bar styles"`

---

### Task 8: Ayarların App'e bağlanması ve canlı uygulama

**Files:**
- Modify: `src/LimitTray.App/App.xaml.cs`
- Create: `src/LimitTray.App/SystemTheme.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `AppSettings`, `QuotaAlerts.UpdateThresholds`, `IQuotaCollector.RequestRefresh`, `ClaudeCollector` aralık delegesi, `LanguageMode`.
- Produces (App içi): `AppSettings App.Settings { get; }`, `event Action<AppSettings>? SettingsChanged`, `void App.ApplySettings(AppSettings next)`, `void App.RefreshNow()`, `bool SystemTheme.IsLight()`, `event EventHandler SystemTheme.Changed`.

- [ ] **Step 1: `SystemTheme.cs`**

```csharp
using System;
using Microsoft.Win32;

namespace LimitTray.App;

/// <summary>
/// Reads Windows' "apps use light theme" switch and raises Changed when it flips.
/// Missing key or denied read counts as dark, which is what the app looked like before.
/// </summary>
public static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static event EventHandler? Changed;

    static SystemTheme() =>
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) Changed?.Invoke(null, EventArgs.Empty);
        };

    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch (Exception) { return false; }
    }
}
```

- [ ] **Step 2: `App.xaml.cs` değişiklikleri**

Alanlar:

```csharp
    private readonly SettingsStore _settingsStore = SettingsStore.ForDefaultPath();
    private AppSettings _settings = AppSettings.Default;
    private readonly List<IQuotaCollector> _collectors = new();
    private DateTimeOffset _lastManualRefresh = DateTimeOffset.MinValue;

    public AppSettings Settings => _settings;
    public SettingsStore SettingsStore => _settingsStore;
    public event Action<AppSettings>? SettingsChanged;
```

`OnStartup` başında:

```csharp
        _settings = _settingsStore.Load();
        _strings = ResolveStrings(e.Args, _settings.Language);
        _alerts.UpdateThresholds(_settings.Thresholds);
```

```csharp
    private static Strings ResolveStrings(IReadOnlyList<string> args, LanguageMode mode)
    {
        // The command line still wins, as it did in v0.2; the setting is the default under it.
        var fromArgs = LanguageArguments.Resolve(args, CultureInfo.CurrentUICulture);
        var explicitArg = args.Any(a => a.StartsWith("--lang", StringComparison.OrdinalIgnoreCase));
        if (explicitArg) return fromArgs;
        return mode switch
        {
            LanguageMode.Turkish => Strings.Turkish,
            LanguageMode.English => Strings.English,
            _ => Strings.ForCulture(CultureInfo.CurrentUICulture),
        };
    }
```

`BuildClaudeCollector` artık instance metodu ve aralığı ayardan okur: `new ClaudeCollector(transport, ClaudeCredentialReader.FromDefaultPath(), () => DateTimeOffset.Now, Task.Delay, () => TimeSpan.FromSeconds(_settings.RefreshSeconds))`. `StartCollector` toplayıcıyı `_collectors`'a ekler.

```csharp
    public void ApplySettings(AppSettings next)
    {
        var previous = _settings;
        _settings = next.Normalised();
        _settingsStore.Save(_settings);

        if (previous.Thresholds != _settings.Thresholds) _alerts.UpdateThresholds(_settings.Thresholds);
        if (previous.Language != _settings.Language)
        {
            _strings = ResolveStrings(_arguments, _settings.Language);
            _trayIcon!.ContextMenuStrip = BuildMenu();
        }
        // The interval delegate reads _settings on the next tick; a shorter interval takes
        // effect immediately by cutting the current wait short.
        if (previous.RefreshSeconds > _settings.RefreshSeconds) foreach (var c in _collectors) c.RequestRefresh();

        UpdateTray();
        SettingsChanged?.Invoke(_settings);
    }

    /// <summary>Manual refresh, rate limited to one per five seconds: the endpoint is shared.</summary>
    public void RefreshNow()
    {
        var now = DateTimeOffset.Now;
        if (now - _lastManualRefresh < TimeSpan.FromSeconds(5)) return;
        _lastManualRefresh = now;
        foreach (var c in _collectors) c.RequestRefresh();
    }
```

`Notify` başına `if (!_settings.Notifications) return;`. `OnExit`'te `_settingsStore.Save(_settings)`.

- [ ] **Step 3: Build + tests yeşil.**
- [ ] **Step 4: Elle doğrulama** — `%LOCALAPPDATA%\limit-tray\settings.json` dosyasını elle `{"version":1,"trayStyle":"Ring","language":"English"}` yazıp başlat: tray halka, menü İngilizce. Dosyayı `{{{` yapıp başlat: varsayılanlar, çökme yok.
- [ ] **Step 5: Commit** — `git commit -m "feat(app): load settings, apply them live, manual refresh with a five second guard"`

---

### Task 9: Panel sayfası (view-model + XAML) ve etkileşimler

**Files:**
- Create: `src/LimitTray.App/ViewModels/ObservableObject.cs`, `WindowRowViewModel.cs`, `ProviderCardViewModel.cs`, `PanelViewModel.cs`, `src/LimitTray.App/Views/PanelPage.xaml(.cs)`, `src/LimitTray.App/TerminalLauncher.cs`, `src/LimitTray.App/Themes/Dark.xaml`
- Modify: `src/LimitTray.App/QuotaPopup.xaml(.cs)` (kabuk), `src/LimitTray.App/App.xaml` (tema sözlüğü)

**Interfaces:**
- Consumes: `Theme`, `QuotaFormatter`, `UsageHistory`, `App.Settings`, `App.RefreshNow`, `App.ApplySettings`.
- Produces: `PanelViewModel.Update(IReadOnlyList<QuotaSnapshot>, DateTimeOffset)`; `QuotaPopup.Show(snapshots, now)` imzası korunur; `QuotaPopup.ShowSettings()` / `ShowPanel()` (Task 10 kullanır).

- [ ] **Step 1: `ObservableObject`**

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LimitTray.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
```

- [ ] **Step 2: `WindowRowViewModel`** — özellikler: `Label` (Oturum/Haftalık), `Percent` (double, 0-100, bar genişliği bağlanır), `PercentText`, `Colour` (`SolidColorBrush`), `ResetText`, `BurnRateText` (null ise satır gizli), `SparklinePoints` (`PointCollection?`, 5 örnek + 1 puan aralık kuralı v0.2'deki gibi, `QuotaPopup.BuildSparkline` mantığı buraya taşınır). Yapıcı `(WindowKind kind, QuotaWindow window, QuotaSnapshot snapshot, UsageHistory history, AppSettings settings, Strings strings, DateTimeOffset now)`; renk `Theme.ColourFor(snapshot.Provider, QuotaFormatter.SeverityFor(window.Percent, settings.Thresholds), snapshot.Health)` + `Theme.OpacityFor`.

- [ ] **Step 3: `ProviderCardViewModel`** — `Provider`, `Title`, `RingPercent` (en dolu pencere), `RingColour`, `ResetShortText` (`Strings.ResetShort` ile en yakın sıfırlanma), `Rows` (`List<WindowRowViewModel>`), `IsExpanded` (set → `App.ApplySettings(settings with ExpandedProviders ±provider)`), `IsStale`, `StaleBadgeText` (`Strings.StaleBadge` + `QuotaFormatter.Age`), `HealthText` (veri yoksa), `HasData`, `LastUpdatedText`, `OpenTerminalCommand` (aşağıdaki `RelayCommand`), `TerminalError` (3 sn görünen metin).

```csharp
public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _run;
    public RelayCommand(Action run) => _run = run;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? _) => true;
    public void Execute(object? _) => _run();
}
```

- [ ] **Step 4: `TerminalLauncher`**

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace LimitTray.App;

/// <summary>
/// Opens the provider's CLI in a fresh terminal: Windows Terminal when it is on PATH,
/// otherwise PowerShell. Nothing is typed into the session; /usage and /status are
/// client-side commands and cannot be passed in. Returns false when neither launches.
/// </summary>
public static class TerminalLauncher
{
    public static bool Open(string provider)
    {
        var command = provider switch { "claude" => "claude", "codex" => "codex", _ => null };
        if (command is null) return false;

        return TryStart("wt.exe", $"new-tab {command}")
            || TryStart("powershell.exe", $"-NoExit -Command {command}");
    }

    private static bool TryStart(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
```

- [ ] **Step 5: `PanelViewModel`** — `Cards` (`ObservableCollection<ProviderCardViewModel>`), `FooterText`, `RefreshCommand` (`App.RefreshNow()` + `IsRefreshing` 600 ms true → döndürme animasyonu tetikler), `SettingsCommand` (popup'a `ShowSettings`), `VersionText`. `Update(snapshots, now)` kartları yeniden kurar (aynı `Provider` sırası korunur).

- [ ] **Step 6: `Themes/Dark.xaml`** — `ResourceDictionary` içinde `SolidColorBrush` anahtarları: `SurfaceBrush` `#0D0F14`, `CardBrush` `#0DFFFFFF`, `CardBorderBrush` `#14FFFFFF`, `CardHoverBorderBrush` `#33FFFFFF`, `TextBrush` `#EEF1F6`, `MutedTextBrush` `#8A93A3`, `TrackBrush` `#1AFFFFFF`, `SeparatorBrush` `#0FFFFFFF`. `App.xaml` `Application.Resources` → `MergedDictionaries` ile `Themes/Dark.xaml`.

- [ ] **Step 7: `PanelPage.xaml`** — `UserControl`; üstte `DockPanel` (sol `LIM'IT`, sağda iki `Button` `↻` ve `⚙`, `Style` düz şeffaf, hover'da `CardHoverBorderBrush`), ortada `ItemsControl ItemsSource="{Binding Cards}"`, `DataTemplate`'te `Border` kart (CornerRadius 14, Padding 12, `MouseEnter/Leave` ile kenarlık, `MouseLeftButtonUp` → `IsExpanded` toggle): `Grid` 2 sütun (52 px halka + içerik). Halka: `Ellipse` arka + `Path` `ArcSegment` (`RingPercent` → `MultiBinding` converter `PercentToArcConverter`, 0'da `Visibility.Collapsed`), ortada `PercentText`. İçerik: ad + `ResetShortText`; `ItemsControl Rows`: `Grid` 3 sütun (etiket 52 px, bar `*`, yüzde 38 px), bar `Border` (`Height 6`, `CornerRadius 3`, `TrackBrush`) içinde `Border` genişliği `Percent` × `ActualWidth` (`PercentToWidthConverter`, `MultiBinding`). Genişlemede (`IsExpanded` → `Visibility`): sparkline `Polyline`, `BurnRateText`, `LastUpdatedText`. Hover ikon: sağ üst köşe `Button` `OpenTerminalCommand`, `Visibility` hover'da. `IsStale` → kart `Opacity 0.55` + rozet `Border` sağ üst. `HasData=false` → yüzde/bar yerine `HealthText` kırmızı. Alt `DockPanel`: `FooterText` sol, `VersionText` sağ. Animasyon: bar genişliği ve halka açısı değişince `DoubleAnimation` 300 ms `CubicEase EaseOut` (`SystemParameters.ClientAreaAnimation` false ise süre 0). Tüm metin `Strings` üzerinden VM'de hazırlanır; XAML'de sabit kullanıcı metni yok (`LIM'IT` marka adı hariç).

- [ ] **Step 8: `QuotaPopup` kabuğu** — XAML: `Grid` içinde iki `ContentPresenter` (`PanelHost`, `SettingsHost`); `ShowSettings()`/`ShowPanel()` `TranslateTransform.X` üzerinde 150 ms `DoubleAnimation`. `Show(snapshots, now)` → `_panel.Update(snapshots, now)`; açılış/konumlama/`Deactivated` davranışı olduğu gibi kalır (`Left=-32000` hilesi, `SizeChanged` → `PositionNearTray`). Eski `BuildProviderBlock`/`Text`/`BuildBar`/`BuildSparkline` silinir. `Background="Transparent"`, `AllowsTransparency` **kullanılmaz** (acrylic ile çakışır); yüzey rengi `Border`'da `SurfaceBrush`.

- [ ] **Step 9: Build** sıfır uyarı; tests yeşil (Core değişmedi).

- [ ] **Step 10: Elle doğrulama (DPI-aware)** — Uygulama çalışırken:
  1. Sol tık: panel açılır, iki kart, halka + iki bar, marka renkleri.
  2. Karta tık: genişler, sparkline/burn rate (veri yeterliyse) görünür; kapat/aç tekrar; uygulamayı kapatıp açınca genişleme hatırlanır (`settings.json` → `expandedProviders`).
  3. Hover: kenarlık aydınlanır, terminal ikonu çıkar; tık → yeni terminalde `claude` açılır.
  4. ↻: ikon döner; 5 sn içinde ikinci tık etkisiz; Codex için `account/rateLimits/read` gönderildiği panelde "az önce güncellendi" ile görünür.
  5. `history.json`'u 6 dk eski tarihli yapıp Claude token dosyasını geçici yeniden adlandır: Claude kartı `HealthText` (giriş gerekli), Codex normal; `%0` hiçbir yerde yok.
  Ekran görüntülerini `docs/verify/2026-09-20/` altına kaydet (commit'e girmez; `.gitignore`'a `docs/verify/` ekle).

- [ ] **Step 11: Commit** — `git commit -m "feat(app): panel page with cards, ring, expand, hover, refresh and terminal launch"`

---

### Task 10: Ayarlar sayfası

**Files:**
- Create: `src/LimitTray.App/ViewModels/SettingsViewModel.cs`, `src/LimitTray.App/Views/SettingsPage.xaml(.cs)`
- Modify: `src/LimitTray.App/QuotaPopup.xaml.cs` (SettingsHost bağlama), `src/LimitTray.App/App.xaml.cs` (`BuildMenu` → "Ayarlar" maddesi ekle)

**Interfaces:**
- Consumes: `App.Settings`, `App.ApplySettings`, `App.SettingsStore.LastSaveFailed`, `StartupRegistration`, `Strings` (Task 6).
- Produces: `SettingsViewModel` (her ayar için property; set → `App.ApplySettings(Settings with {...})`), `BackCommand`, `OpenGitHubCommand`, `SaveFailedText` (null ise gizli), `StartWithWindows` (get: `StartupRegistration.IsEnabled()`; set: `SetEnabled` sonra geri oku, v0.2 kuralı).

- [ ] **Step 1: `SettingsViewModel`** — Enum seçenekleri `IReadOnlyList<Choice<T>>` (`record Choice<T>(T Value, string Label)`) olarak `Strings`'ten üretilir: tema (3), dil (3), yenileme (`AllowedRefreshSeconds` → `Seconds`/`Minutes` biçimi), dikkat eşiği (50..80 adım 5), uyarı eşiği (`Caution+5 .. 95` adım 5; dikkat değişince liste yeniden kurulur ve uyarı geçersiz kaldıysa `Caution+5`'e çekilir), tray stili (3), tray kaynağı (5). `GlassEffectVisible` = `Environment.OSVersion.Version.Build >= 22621`.

- [ ] **Step 2: `SettingsPage.xaml`** — üst çubuk: `‹` geri butonu + `Settings` başlığı. Gruplar `TextBlock` (`Strings.GroupAppearance` vb., küçük büyük harf, `MutedTextBrush`). Her satır `Grid` (etiket sol, kontrol sağ). Segment kontrolü: `ListBox` yatay, `ItemContainerStyle` ile pill görünümü (seçili `#24FFFFFF`), `SelectedValuePath="Value"`. Toggle: `CheckBox` özel `ControlTemplate` (32×18 kaydırmalı). Eşikler: `ComboBox`. Alt satır: `‹ Panele dön` sol, `v0.3.0 · GitHub` sağ (`Hyperlink` → `Process.Start("https://github.com/morp1e/limit-tray")` `UseShellExecute=true`). `SaveFailedText` üstte sarı satır.

- [ ] **Step 3: `BuildMenu`** — `Exit` üstüne `_strings.Settings` maddesi: popup'ı aç ve `ShowSettings()`.

- [ ] **Step 4: Build + tests yeşil.**

- [ ] **Step 5: Elle doğrulama** — Her ayarı değiştir ve anında etkisini gör: tema (Task 11'e kadar yalnız koyu var; değer dosyaya yazılmalı), dil (panel ve menü metinleri hemen değişir), yenileme (60 seçince Claude 1 dk içinde yenilenir; süre `settings.json`'da), eşikler (dikkat 50 seçince %50'nin üstündeki bar sarıya döner), bildirim kapalı (eşiği geçen pencere balon çıkarmaz; test için uyarı eşiğini mevcut yüzdenin altına çek), tray stili ve kaynağı (ikon hemen değişir), Windows ile başlat (kayıt defteri `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`). `settings.json`'u salt okunur yap → "Ayarlar kaydedilemedi" satırı görünür, uygulama çalışmaya devam eder.

- [ ] **Step 6: Commit** — `git commit -m "feat(app): settings page inside the popup, applied live"`

---

### Task 11: Tema (açık/koyu/sistem), acrylic, geçiş animasyonları

**Files:**
- Create: `src/LimitTray.App/Themes/Light.xaml`, `src/LimitTray.App/WindowBackdrop.cs`
- Modify: `src/LimitTray.App/App.xaml.cs` (tema uygulama), `src/LimitTray.App/QuotaPopup.xaml.cs` (`SourceInitialized` → backdrop)

**Interfaces:**
- Consumes: `SystemTheme`, `ThemeMode`, `App.SettingsChanged`.
- Produces: `static bool WindowBackdrop.TryApplyAcrylic(Window, bool dark)`; `static void WindowBackdrop.Clear(Window)`.

- [ ] **Step 1: `Light.xaml`** — aynı anahtarlar: `SurfaceBrush` `#F4F5F8`, `CardBrush` `#FFFFFFFF`, `CardBorderBrush` `#14000000`, `CardHoverBorderBrush` `#33000000`, `TextBrush` `#14161B`, `MutedTextBrush` `#5B6270`, `TrackBrush` `#14000000`, `SeparatorBrush` `#0F000000`. Marka ve durum renkleri değişmez (Core'dan gelir).

- [ ] **Step 2: `WindowBackdrop.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitTray.App;

/// <summary>
/// Windows 11 22H2+ system backdrop through DWM. Anything older, or a DWM that refuses,
/// silently keeps the flat surface: the effect is decoration and never worth an error.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropTransient = 3; // acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    public static bool TryApplyAcrylic(Window window, bool dark)
    {
        if (!IsSupported) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int));

        var backdrop = BackdropTransient;
        var result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        if (result != 0) return false;

        // The backdrop shows only through a transparent client area.
        HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        return true;
    }

    public static void Clear(Window window)
    {
        if (!IsSupported) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var none = BackdropNone;
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
    }
}
```

`QuotaPopup`: `SourceInitialized` olayında ve `SettingsChanged`'de `ApplyBackdrop()`: `GlassEffect && TryApplyAcrylic(this, dark)` başarılıysa kök `Border` arka planı `SurfaceBrush`'ın %72 opaklıklı hali, değilse tam `SurfaceBrush`. `WindowChrome` (`GlassFrameThickness="-1"`, `CaptionHeight="0"`, `CornerRadius="12"`) XAML'e eklenir; acrylic görünmüyorsa bu kısım ölçülür: `HwndSource` arka planı gerçekten şeffaf mı, `Border` opaklığı 1 mi.

- [ ] **Step 3: Tema uygulama (`App.xaml.cs`)**

```csharp
    private void ApplyTheme()
    {
        var light = _settings.Theme switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => SystemTheme.IsLight(),
        };
        var uri = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        _popup?.ApplyBackdrop(dark: !light);
    }
```

`OnStartup`'ta `ApplyTheme()`; `SystemTheme.Changed += (_, _) => Dispatcher.Invoke(ApplyTheme)`; `ApplySettings` içinde tema veya cam değiştiyse `ApplyTheme()`. Fırçalar XAML'de `{DynamicResource}` ile bağlanır ki sözlük değişince yeniden boyansın.

- [ ] **Step 4: Font** — `App.xaml` `Application.Resources` içinde `FontFamily` kaynağı: `"Segoe UI Variable Text, Segoe UI"` (WPF fallback listesini destekler). `QuotaPopup.xaml` `FontFamily="{StaticResource AppFont}"`.

- [ ] **Step 5: Build + tests yeşil.**

- [ ] **Step 6: Elle doğrulama (DPI-aware)** — Koyu/açık/sistem üçü; Windows temasını değiştirince Sistem modunda panel anında döner; cam efekti açık/kapalı farkı görünür (arkadaki pencere bulanık geçiyor); Win10'da test edilemiyorsa `IsSupported=false` yolu `Environment.OSVersion` sahte değeriyle değil, geçici olarak `Build >= 999999` yazılıp düz arka plan gözlenerek doğrulanır ve geri alınır. Animasyonlar: eşiği değiştirince bar rengi anında, genişlik 300 ms'de kayar.

- [ ] **Step 7: Commit** — `git commit -m "feat(app): light and system themes, acrylic backdrop, eased transitions"`

---

### Task 12: Sürüm, README, ekran görüntüsü, spec notu

**Files:**
- Modify: `src/LimitTray.App/LimitTray.App.csproj` (0.3.0), `README.md`, `docs/screenshot.png`, `AGENTS.md` (yeni kurallar), `docs/specs/2026-09-20-popup-redesign-design.md` (`status: implemented`)

- [ ] **Step 1: Sürüm** — csproj `Version`/`AssemblyVersion`/`FileVersion` → `0.3.0`, `0.3.0.0`. `CodexCollector.InitializeMessage` sürümü Task 5'te değişti; `grep -rn "0.2.0" src` sıfır sonuç vermeli (README kurulum satırları hariç, onlar Step 3'te güncellenir).

- [ ] **Step 2: Ekran görüntüsü** — DPI-aware yakalama ile yeni panel (koyu, cam, her iki sağlayıcı veri getirmiş, biri genişletilmiş). `docs/screenshot.png` değiştirilir; ek `docs/screenshot-settings.png`.

- [ ] **Step 3: README** — "Why this exists" altına yeni paragraf: renk kuralı (marka = iyi, sarı/kırmızı eşik, gri eski), tıklanır kartlar, ayarlar, üç tray stili. Kurulum bölümündeki `v0.2.0` dosya adı `v0.3.0`. "How it works" altına `settings.json` konumu ve içeriği (yalnız ayarlar). Dürüstlük satırı güncellenir: bu tur kodun kim tarafından yazıldığı (Codex/Claude, gerçek duruma göre). Uzun tire yok: `pwsh ./.github/scripts/Test-NoEmDash.ps1` yerelde geçmeli.

- [ ] **Step 4: `AGENTS.md`** — "Rules that hold in this repo" altına iki madde:
  - Colour is decided in `Theme.ColourFor` and nowhere else; no hex value in XAML or code-behind for a quota colour.
  - The tray icon draws `TrayIconModel` and makes no decision of its own; a null bar is a question mark, never an empty gauge.

- [ ] **Step 5: Spec** — `status: implemented`, `modified` tarihi.

- [ ] **Step 6: Tam doğrulama** — `dotnet build LimitTray.sln -c Release` sıfır uyarı; `dotnet test` (sayı commit mesajına yazılır); `dotnet publish src/LimitTray.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` tek dosya üretir, boyut not edilir; yayınlanan exe çalıştırılıp iki sağlayıcıdan canlı veri gözlenir.

- [ ] **Step 7: Commit** — `git commit -m "chore(release): v0.3.0, refreshed readme and screenshots"`. Tag ve push Özenç'in onayıyla: `git tag v0.3.0 && git push origin main --tags` (release workflow etiketten binary üretir).

---

## Self-review notları

- **Spec kapsamı:** renk kuralı (T3), popup iki sayfa + kart + genişleme + hover + terminal + yenile (T5, T9), ayarlar tablosu (T1, T2, T10), tray üç stil + kaynak + hata örtmeme (T4, T7), ayar kalıcılığı ve yazma hatası (T2, T10), acrylic/tema/animasyon/font (T11), hata yönetimi (T2, T9 terminal, T11 acrylic), test listesi (T1-T5), teslimat (T12), `--lang` önceliği (T8). Roller: plan başlığı.
- **Tip tutarlılığı:** `QuotaThresholds` her yerde `Settings` namespace'inden; `SeverityFor(double, QuotaThresholds)` T3'te tanımlı, T4/T9 kullanıyor; `RequestRefresh` T5'te arayüzde, T8 çağırıyor; `Strings` üyeleri T6'da, T9/T10 kullanıyor; `TrayIconModelBuilder.Build(snapshots, settings)` T4/T7/T8 aynı imza.
- **Bilinen sınır:** T5 Codex testi mevcut `FakeProcess`'in şekline bağlı; uygulayıcı dosyayı açıp yardımcıyı uyarlar, testin kanıtladığı şey değişmez: aktif sürece bir `read` daha gider.
