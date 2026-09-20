---
title: "Lim'it v0.3 - Panel yeniden tasarımı"
created: 2026-09-20
modified: 2026-09-20
type: spec
status: implemented
tags: [proje, csharp, wpf, kota, claude, codex, ui]
---

# Lim'it v0.3 - Panel yeniden tasarımı

## Sorun

v0.2 paneli doğru veriyi gösteriyor ama yalnız bakılıp kapatılan bir yüzey: hover yok,
tıklama yok, ayar yok, tema yok. Her davranış koda gömülü (eşik, aralık, dil yalnız
komut satırından). Tray ikonu tek bir gösterge işareti taşıyor ve hangi sağlayıcının
sıkıştığını söylemiyor. Uygulama işlevsel ama "profesyonel bir ürün" gibi hissettirmiyor.

## Başarı ölçütü

Panel açan biri tek bakışta hangi sağlayıcının hangi penceresinin sıkıştığını renkten
görür, karta tıklayıp ayrıntıya iner, dişliden tema/dil/aralık/eşik/tray stilini değiştirir
ve değişiklik anında uygulanır. Tray ikonu kullanıcının seçtiği stilde veri taşır.
Hafiflik korunur: tek dosya .NET, harici NuGet yok, Electron yok, boşta CPU sıfıra yakın.

## Sabit kalan kurallar

`AGENTS.md`'deki kuralların hepsi geçerli. Bu tasarımın dokunduğu üçü:

- Hata asla `%0` olarak çizilmez; `HealthState` sözle gösterilir.
- Veri desteklemeyen tahmin (burn rate, sparkline) çizilmez; eşikler düşürülmez.
- Core WPF/WinForms bilmez. Renk kuralı, ikon modeli, ayar modeli Core'dadır ve testlidir.

## Kapsam dışı (bilinçli)

Ayrı ana pencere, büyütülebilir geçmiş grafiği, transcript'ten token/maliyet okuma,
çoklu hesap, global klavye kısayolu, otomatik güncelleme, Gemini ve diğer sağlayıcılar.

## Görsel yön

Karşılaştırmalı mockup'lardan seçildi (`.superpowers/brainstorm/`, repo dışı):
**cam ve marka renkleri**. Koyu zemin üzerinde hafif radyal marka parıltısı, sağlayıcı
başına marka rengi, kartta halka gösterge.

Marka renkleri: Claude `#D97757` (açık ucu `#F2A27E`), Codex `#10A37F` (`#5EE3B6`).
Durum renkleri: dikkat `#E8B84A`, uyarı `#E5484D`, eski/hata `#6B7280`.

### Renk kuralı (tek fonksiyon)

`Theme.ColourFor(provider, severity, health)`:

| health   | severity | renk          |
|----------|----------|---------------|
| Fresh    | Normal   | marka rengi   |
| Fresh    | Caution  | dikkat (sarı) |
| Fresh    | Warning  | uyarı (kırmızı) |
| Stale    | herhangi | gri, %55 opaklık |
| diğer hata | -      | gri; yüzde çizilmez, metin çizilir |

Popup barı, halkası, yüzde metni ve tray ikonu aynı fonksiyonu kullanır. Renk hiçbir
XAML'de veya code-behind'da sabit yazılmaz.

Eşikler bugünkü `QuotaFormatter` değerleriyle başlar (dikkat 60, uyarı 85) ve
ayarlanabilir olur. `QuotaAlerts` bildirim eşiği uyarı eşiğine bağlanır.

## Popup

Tek `Window`, iki sayfa: **Panel** ve **Ayarlar**. Dişli ikonu ayarlara, geri oku panele
döner; geçiş 150 ms yatay kayma. Konumlama ve `Deactivated` kapanma davranışı v0.2'deki
gibi kalır (issue #1 düzeltmesi korunur).

Code-behind'daki elle `StackPanel` kurma modeli bırakılır. Panel XAML `ItemsControl` +
`DataTemplate` ile çizilir; görünüm modelleri (`ProviderCardViewModel`,
`WindowRowViewModel`, `SettingsViewModel`) `LimitTray.App/ViewModels/` altında yaşar ve
yalnız Core tiplerini biçime çevirir. İş mantığı Core'da kalır.

### Panel sayfası

- **Başlık çubuğu:** `LIM'IT` solda; sağda elle yenile (↻) ve dişli (⚙). Yenile tıklanınca
  ikon 1 tur döner ve her iki toplayıcıya `RefreshNow()` sinyali gider; 5 sn içinde ikinci
  tıklama yok sayılır (endpoint paylaşımlı, 429 riski). `RefreshNow()` `IQuotaCollector`'a
  eklenir: Claude'da bekleyen gecikmeyi iptal edip hemen ister, geri çekilme (backoff)
  durumundaysa yok sayılır; Codex'te `account/rateLimits/read` çağrısı yapılır.
- **Sağlayıcı kartı** (her sağlayıcı için bir tane):
  - Sol: 44 px halka, kartın en dolu penceresinin yüzdesi; renk kuralına göre.
  - Sağ üst: sağlayıcı adı, karşısında en yakın sıfırlanma ("↺ 2s 3d").
  - İki satır: `etiket · bar · yüzde` (Oturum / Hafta). Bar 6 px, yüzde tabular rakam.
  - **Daraltılmış/genişletilmiş:** karta tıklayınca genişler; genişlemede sparkline,
    burn rate satırı ve "son güncelleme" gelir. Durum ayarlarda sağlayıcı başına saklanır.
  - **Hover:** kenarlık aydınlanır; sağ üstte "terminal aç" ikonu görünür. Tıklanınca yeni
    Windows Terminal (yoksa PowerShell) penceresinde `claude` veya `codex` başlatılır.
    `/usage` veya `/status` otomatik yazılmaz; bunlar client-only komut, ölçülmüştü.
  - **Hata ve eski veri:** eski veri gri kartta gerçek yaşıyla ("ESKİ · 6d" rozeti);
    veri yoksa yüzde ve bar yerine `HealthText` satırı, kırmızı.
- **Alt bilgi:** "az önce güncellendi" solda, sürüm sağda.

### Ayarlar sayfası

Gruplar ve kontroller; her değişiklik anında uygulanır ve `settings.json`'a yazılır,
kaydet düğmesi yoktur.

| Grup     | Ayar               | Değerler                                  | Varsayılan |
|----------|--------------------|-------------------------------------------|------------|
| Görünüm  | Tema               | Sistem / Koyu / Açık                      | Sistem     |
| Görünüm  | Dil                | Sistem / TR / EN                          | Sistem     |
| Görünüm  | Cam efekti         | açık / kapalı (yalnız Win11'de görünür)   | açık       |
| Veri     | Yenileme aralığı   | 60 / 120 / 300 sn (Claude için; Codex push) | 120      |
| Veri     | Dikkat eşiği       | 50-80, 5'er adım                          | 60         |
| Veri     | Uyarı eşiği        | dikkat+5 ile 95 arası                     | 85         |
| Veri     | Bildirim           | açık / kapalı                             | açık       |
| Tray     | İkon stili         | Halka / Sayı / İki bar                    | İki bar    |
| Tray     | İkon kaynağı       | En dolu / Claude 5s / Claude 7g / Codex 5s / Codex 7g | En dolu |
| Sistem   | Windows ile başlat | açık / kapalı                             | kapalı     |

Alt bilgi: "‹ Panele dön" solda, "v0.3.0 · GitHub" sağda (link tarayıcıda açılır).
`--lang` komut satırı argümanı ayarı geçersiz kılar (mevcut davranış).

## Tray ikonu

Core'da `TrayIconStyle { Ring, Number, DualBar }` ve
`TrayIconSource { Highest, ClaudeSession, ClaudeWeekly, CodexSession, CodexWeekly }`.
`TrayIconModel.Build(snapshots, settings)` "ne çizilecek"i döner: stil, birincil yüzde,
birincil renk, DualBar için sağlayıcı başına (yüzde, renk) çifti, veri yoksa `null`
(soru işareti çizilir). App'teki `TrayIconRenderer` yalnız bu modeli piksele çevirir.

- **Ring:** v0.2'deki 270 derecelik gösterge; renk kuralına göre.
- **Number:** yuvarlatılmış dolu kare, iki haneli yüzde; 100'de rakam yerine dolu blok.
  Arka plan renk kuralına göre, yazı kontrast rengi.
- **DualBar:** sol Claude, sağ Codex dikey bar; her biri kendi en dolu penceresi, kendi
  marka rengi, eşikte durum rengi. Kaynak ayarı bu stilde yok sayılır.
- Kaynak seçili pencere hatalıysa Highest'a düşülmez; ikon o sağlayıcının hata halini
  (gri, soru işareti) gösterir. Hata sessizce başka veriyle örtülmez.
- Tooltip: `Claude 50% · 25% | Codex 39% · 52%`; 63 karakter sınırı korunur.
- Handle disiplini v0.2'deki gibi: her yeniden çizimde önceki `RenderedIcon` dispose edilir.

## Ayar modeli ve kalıcılık

`Core/Settings/AppSettings` immutable record; `SettingsStore`, `HistoryStore` ile aynı
desende: `%LOCALAPPDATA%\limit-tray\settings.json`, `read/write` delegeleri enjekte
edilebilir, bozuk veya eksik dosya sessizce varsayılan sayılır, bilinmeyen alan yok
sayılır. Dosyaya yalnız ayar değerleri yazılır; token, hesap veya snapshot ayrıntısı
asla girmez (testle korunur, `history.json` kuralıyla aynı).

Değişiklik akışı: `SettingsViewModel` → `SettingsStore.Save` → `App.OnSettingsChanged`
→ ilgili bileşen (toplayıcı aralığı, tema, tray ikonu) yeniden yapılandırılır.
Toplayıcı aralığı canlı değişir; süreç yeniden başlatılmaz.

## Görsel katman (App)

- Windows 11 22H2+ üzerinde `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, Acrylic)`
  P/Invoke ile cam; başarısız olursa veya ayar kapalıysa düz `#0D0F14`. Ek paket yok.
- Tema Sistem/Koyu/Açık; Sistem seçiliyse `Apps use light theme` kayıt değeri izlenir.
  Açık tema: yüzeyler `#F4F5F8`/`#FFFFFF`, metin `#14161B`, marka ve durum renkleri aynı.
- Bar genişliği ve halka açısı değeri değişince 300 ms ease-out ile hareket eder.
  Sayfa geçişi 150 ms. Animasyon `SystemParameters.ClientAreaAnimation` kapalıysa kapanır.
- Font: `Segoe UI Variable Text` varsa, yoksa `Segoe UI`.
- Popup genişliği 360 px kalır; yükseklik içeriğe göre.

## Hata yönetimi

- Ayar dosyası yazılamazsa (izin, disk): ayar bellekte uygulanır, ayarlar sayfasında tek
  satır uyarı ("Ayarlar kaydedilemedi"), süreç devam eder.
- Terminal açma başarısız olursa (WT ve PowerShell bulunamadı): kartta 3 sn'lik sözlü uyarı,
  istisna yutulmaz ama kullanıcıya yol adı veya token gösterilmez.
- Acrylic başarısız: sessiz düşüş, düz arka plan. Loglama yok (uygulamada log dosyası yok).
- Eşik kombinasyonu geçersizse (uyarı ≤ dikkat) `SettingsStore` yüklemede varsayılanlara
  döner; UI zaten geçersiz değeri seçtirmez.

## Test

**Core (xUnit, ağsız):**
- `SettingsStore`: varsayılan, tam dosya, bozuk JSON, bilinmeyen alan, geçersiz eşik,
  yazılamayan hedef; dosyada yasak alan olmadığı testi.
- `Theme.ColourFor`: sağlayıcı × severity × health tam tablosu.
- `TrayIconModel.Build`: stil × kaynak × sağlık kombinasyonları; kaynak hatalıyken
  Highest'a düşmediği; DualBar'ın kaynağı yok saydığı; veri yokken `null`.
- `QuotaAlerts` ayarlanabilir eşikle.
- Mevcut 158 test değişmeden yeşil kalır (rename dışında).

**Uygulama (elle, kanıt ekran görüntüsü):**
- DPI-aware yakalama ile beş kart durumu: normal, dikkat, uyarı, eski, 429.
- Ayarlar sayfası; her ayarın değişince anında yansıdığı (tema, tray stili, aralık).
- Üç tray stili 100% ve 150% ölçekte, 16 ve 24 px.
- Win11 acrylic açık/kapalı; açık tema.
- Gerçek sağlayıcıyla canlı koşum: iki sağlayıcı da veri getiriyor, elle yenile çalışıyor,
  terminal açma çalışıyor. "Yeşil test kanıt değildir" kuralı gereği zorunlu.

## Teslimat

Sürüm `0.3.0`. README ekran görüntüleri ve özellik listesi yenilenir; `docs/screenshot.png`
yeni panelle değiştirilir. Release workflow'u değişmez.

## Roller

Spec, plan ve inceleme Claude'da; C# üretimi görev görev Codex'e (`codex exec`, geçici
worktree) delege edilir. Her görev derleme çıktısı, test sonucu ve gerektiğinde çalışan
uygulamanın gözlemiyle kabul edilir; Codex'in "tamamlandı" beyanı kanıt sayılmaz.
