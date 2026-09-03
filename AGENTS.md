# limit-tray

Windows tray uygulamasi: Claude Code ve Codex CLI kota yuzdelerini tek panelde gosterir.

## Bu repoda gecerli kurallar

- Tum mantik `src/LimitTray.Core` icindedir ve WPF'e bagimli degildir.
  `src/LimitTray.App` yalnizca cizim yapar; oraya is mantigi yazilmaz.
- Hicbir hata durumu `%0` olarak gosterilmez. `%0` yalnizca saglayicidan gelen
  gercek degerdir. Hata durumlari `HealthState` ile tasinir.
- Token loglanmaz, diske yazilmaz, istisna metnine sizmaz.
- Harici NuGet bagimliligi eklenmez (test projesindeki xUnit haric).
- Kullaniciya gorunen metin Turkce; kod, dosya adi ve commit mesaji ASCII.
- Veri kaynaklarinin ikisi de resmi degildir. Kaynak degisirse dogru davranis
  `ProtocolBroken` gostermektir, tahmin uretmek degildir.

Tasarim: `docs/specs/2026-09-03-agent-quota-tray-design.md`
Plan: `docs/superpowers/plans/2026-09-03-agent-quota-tray.md`
