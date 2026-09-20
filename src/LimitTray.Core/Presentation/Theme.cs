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
