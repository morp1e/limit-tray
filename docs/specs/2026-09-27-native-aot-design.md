---
title: "Lim'it v0.4 - Native AOT yeniden yazımı"
created: 2026-09-27
modified: 2026-09-27
type: spec
status: approved
tags: [proje, csharp, nativeaot, win32, direct2d, kota, performans]
---

# Lim'it v0.4 - Native AOT yeniden yazımı

## Sorun

Bir tepsi ikonu için Lim'it çok bellek tutuyor. 2026-09-27'de v0.3.4 Release
(framework-dependent) derlemesi, popup kapalıyken ölçüldü:

| Ölçüm | Değer |
|---|---|
| LimitTray.App özel bellek (Private Bytes) | 180 MB; tek bir olayla 255 MB, geri inmedi |
| LimitTray.App çalışma kümesi | 155 MB, sonra 246 MB |
| `codex app-server` alt süreci | 21 MB özel, 106 MB çalışma kümesi, sürekli açık |
| Boşta CPU | ~%0,05; 10 sn'lik pencerelerin çoğunda 0 ms |
| Handle | 850-940 arası salınım, büyüme yok (sızıntı yok) |

Belleğin kaynakları (kaynak ve kod satırıyla doğrulandı):

1. WPF'in donanım render yolu. Bu makinede Intel Iris Xe + iki monitör var; boş bir WPF
   uygulaması bu yapılandırmada ~160 MB tutuyor (`dotnet/wpf#7704`, açık).
2. Popup açılışta kuruluyor ve yalnız gizleniyor (`App.xaml.cs:53`, `:246`).
3. Yayın sıkıştırmalı tek dosya (`release.yml:55`); Microsoft: "On application start, the
   assemblies must be decompressed into memory". İndirilen exe ölçülenden kötü olabilir.
4. WPF ve WinForms birlikte yükleniyor; WinForms yalnız tepsi ikonu için var.

Bağlam dalgası: vault `📥 000-Inbox/Dump/limit-optimizasyon-2026-09-27/` (s1 kod, s2 platform).

## Karar

Arayüz C# + .NET 10 + NativeAOT + saf Win32 ile yeniden yazılır. WPF ve WinForms bırakılır.
Rust değerlendirildi ve seçilmedi: belleği dil değil arayüz yığını yiyor, Core ve testleri
korunur. Hedef .NET 10 LTS (.NET 9 desteği 2026-11-10'da bitiyor).

Kabul edilen ödünler (Özenç, 2026-09-27): popup açılışı gecikebilir, Codex güncellemesi
anlık olmayabilir. Görsel sonuç v0.3.4 ile **birebir aynı, animasyonlar dahil**.

## Başarı ölçütü

Ölçülen sayı Private Bytes (commit). Görev Yöneticisi'nin "Bellek" sütunu kullanılmaz;
çalışma kümesi kırpılınca düşük görünür ama commit düşmez.

| Ölçüt | Hedef |
|---|---|
| Popup hiç açılmamış, boşta | Private Bytes ≤ 20 MB |
| Aç-genişlet-ayarlar-geri-kapat döngüsünden 60 sn sonra | Boştaki tabanın en fazla +3 MB üstü |
| Boşta CPU | v0.3.4'ten yüksek değil; planlı uyanmalar dışındaki 10 sn'lik pencereler 0 ms |
| Alt süreç | Codex okuması dışında hiç yok |
| AOT/trim uyarısı | Sıfır (IL2xxx/IL3xxx hata sayılır) |
| Görünüm | Aynı veriyle v0.3.4 ekran görüntüleriyle yan yana kıyas; son kabul Özenç |

İlk görev boş bir tepsi + popup iskeletinin tabanını ölçer. Taban 20 MB'ın üstündeyse iş
durur ve Özenç'le konuşulur; eşik sessizce gevşetilmez.

## Sabit kalan kurallar

Repo `AGENTS.md`'sindeki kuralların hepsi geçerli: hata asla `%0` değil, token hiçbir yere
yazılmaz, veri desteklemeyen tahmin çizilmez, renk yalnız `Theme.ColourFor`'da, tepsi ikonu
`TrayIconModel`'i çizer ve karar vermez, metin yalnız `Strings.cs`'te, kaynak dosyalar ASCII.
WPF'e özgü kurallar (XAML bağlama, sabit 392x700 pencere) bu sürümle yenilenir; bkz. Teslim.

## Mimari

### Projeler

- `src/LimitTray.Core`: .NET 10 (`net10.0`), `IsAotCompatible=true`. İş mantığı aynen kalır.
  Değişen yerler: Codex veri yolu, sağlayıcı başına eskime süresi, geçmişin yalnız kirliyken
  yazılması, sabit veri modu.
- `src/LimitTray.Native` (yeni exe, `net10.0-windows`): `PublishAot=true`, `UseWPF` ve
  `UseWindowsForms` yok. Win32, Direct2D ve DirectWrite imzaları CsWin32'den
  (`allowMarshaling: false`, AOT uyumlu işlev işaretçisi yapıları).
- `src/LimitTray.App` (WPF): Native aynı işlevi verince bu sürümde silinir.
- `tests/LimitTray.Tests`: .NET 10'da mevcut testler yeşil kalır; Core'a eklenenlerin testleri.
- `tests/LimitTray.Native.Tests`: Native'in saf mantığı (yerleşim, tıklama hedefi, animasyon
  zamanlaması, uyanma takvimi). Çizimin kendisi testle değil ekran görüntüsüyle doğrulanır.

**NuGet istisnası.** Repo kuralı "harici NuGet yok" der. CsWin32
(`Microsoft.Windows.CsWin32`) bilinçli istisnadır: yalnız derleme anında çalışan bir kaynak
üreticisidir, çalışan exe'ye kütüphane eklemez. El yazısı COM vtable'ları (Direct2D'de
yüzlerce yuva) yanlış yuva indeksine açık ve çökmeyi sessizce üretir. Kural metni
"çalışma zamanı NuGet bağımlılığı yok; Microsoft'un derleme anı kaynak üreticileri serbest"
olarak güncellenir.

### Native birimleri

| Birim | İşi | Bağımlı olduğu |
|---|---|---|
| `Host/AppHost` | Mesaj döngüsü, bileşenlerin bağlanması, ayar değişikliği akışı | Core, diğer birimler |
| `Host/UiThread` | Arka plandan UI thread'ine iş gönderme (`PostMessage(WM_APP)` + kuyruk) | Interop |
| `Tray/TrayIcon` | `Shell_NotifyIconW`: ekle/değiştir/sil, tooltip (127 karakter), balon bildirimi, sol/sağ tık, `TaskbarCreated` gelince yeniden ekleme | Interop |
| `Tray/TrayMenu` | Sağ tık menüsü (`TrackPopupMenu`): Windows ile başlat, Ayarlar, Çıkış | Interop, Core `Strings` |
| `Graphics/Canvas` | D2D yazılım render hedefi (DC render target) + DirectWrite üstünde ince katman: yuvarlak dikdörtgen, yay, çizgi, yol, doğrusal/radyal gradyan, metin ölçme/çizme, kırpma, opaklık | Interop |
| `Graphics/Surface` | 32 bit premultiplied DIB bölümü; `Canvas` bunun üstüne çizer | Interop |
| `Graphics/TrayIconPainter` | `TrayIconModel` → HICON (16/20/24/32 px, DPI'ya göre) | Canvas, Core |
| `Ui/Palette` | v0.3.4 `Themes/Dark.xaml`, `Light.xaml`, `Brushes.cs` karşılığı; renkler `Theme.ColourFor`'dan türer | Core |
| `Ui/Layout` | Saf fonksiyon: görünüm durumu + DPI → her öğenin dikdörtgeni | Core |
| `Ui/HitTest` | Nokta → öğe (kart, dişli, yenile, terminal, ayar kontrolü) | Layout |
| `Ui/Animator` | Zamana dayalı geçişler (ease-out); animasyon yokken kare zamanlayıcısı durur | - |
| `Ui/PanelPage`, `Ui/SettingsPage` | Yerleşimden çizim; v0.3.4'ün birebir karşılığı | Canvas, Layout, Palette, Core |
| `Popup/PopupWindow` | Katmanlı pencere, `UpdateLayeredWindow`, monitör başına DPI (PMv2), tepsiye göre konum (çoklu monitör), odağı kaybedince kapanma, kapanınca yok edilme | Interop, Ui |
| `Platform/*` | Windows ile başlatma (HKCU Run), terminal açma, tema izleme (`WM_SETTINGCHANGE` + kayıt değeri), tarayıcıda link | Interop |
| `Measure/FixtureMode` | `--fixture <dosya>` ve `--data-dir <klasör>` argümanları | Core |

WPF'teki `INotifyPropertyChanged` görünüm modelleri gider; yerlerine düz durum kayıtları
gelir. Biçimlendirme zaten Core'da (`QuotaFormatter`, `Strings`, `Theme`, `TrayIconModel`).

### Thread modeli

Tek UI thread; mesaj döngüsü `GetMessage` ile bloklanır, iş yoksa CPU harcamaz. Toplayıcılar
bugünkü gibi thread pool'da async döngü olarak çalışır; her snapshot `UiThread.Post` ile UI
thread'ine gelir ve `QuotaStore`'a orada uygulanır. `Dispatcher` yok, kilit yalnız
`QuotaStore` içinde (mevcut).

### Popup penceresi

- `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, `WS_POPUP`.
- İçerik `UpdateLayeredWindow` ile verilir. Bu çağrı konumu, boyutu ve piksel içeriğini tek
  atomik adımda günceller. Böylece genişletme/daraltmada pencere içeriğe göre boyutlanır ve
  v0.3.3-v0.3.4'teki eski kare (ghosting) oluşmaz. v0.3.4'ün sabit 392x700 saydam pencere
  çözümü buna gerek bırakmaz; kural "popup asla yeniden boyutlanmaz"dan "popup yalnız
  `UpdateLayeredWindow` ile, içerikle aynı adımda boyutlanır"a döner.
- Köşe yumuşatma ve gölge kendi çizimimiz (v0.3.4 ile aynı yarıçap ve gölge). DWM pencere
  nitelikleri (köşe, kenarlık, backdrop) **uygulanmaz**; v0.3.4'te katmanlı pencereye DWM
  niteliği uygulamak Özenç'in ekranında tonlu blok üretmişti.
- Açılış: pencere ve yüzey kurulur, ilk kare çizilir, sonra gösterilir. Kapanış (odak kaybı,
  tepsiye ikinci tık, Esc): pencere yok edilir, yüzey, render hedefi, DirectWrite metin
  düzenleri ve D2D/DirectWrite fabrikaları bırakılır.
- DPI: işlem PMv2 bildirir; popup açıldığı monitörün DPI'siyle çizilir, `WM_DPICHANGED`'de
  yeniden yerleşir.

### Çizim

- Direct2D, `D2D1_RENDER_TARGET_TYPE_SOFTWARE` ile DC render target; hedef bir DIB bölümü.
  GPU sürücüsüne dokunulmaz; Iris Xe maliyeti gelmez.
- Metin DirectWrite; font `Segoe UI Variable Text`, yoksa `Segoe UI` (v0.3 ile aynı). Metin
  yumuşatma gri tonlu (katmanlı pencerede ClearType alfa ile doğru birleşmez).
- Animasyonlar v0.3.4 ile aynı: bar ve halka 300 ms ease-out, sayfa geçişi 150 ms yatay
  kayma, yenile ikonu bir tur, arka plan radyal marka parıltısı. Kare zamanlayıcısı yalnız
  en az bir animasyon sürerken çalışır. `SPI_GETCLIENTAREAANIMATION` kapalıysa animasyonlar
  anında biter.

## Bellek kuralları

1. Popup kapalıyken ona ait hiçbir kaynak bellekte durmaz (yukarıda).
2. Tepsi ikonu yalnız `TrayIconModel` değişince (değer eşitliği) veya DPI/tema değişince
   yeniden çizilir; çizim kaynakları çizimden sonra bırakılır, önceki HICON yok edilir.
3. Alt süreç yalnız Codex app-server okuması sırasında vardır.
4. Geçmiş yalnız değiştiyse diske yazılır; bugünkü 30 sn'lik koşulsuz JSON serileştirmesi
   kalkar. Yazım snapshot gelişinde 10 sn debounce ile ve çıkışta yapılır.
5. GC workstation, non-concurrent (`ConcurrentGarbageCollection=false`): arka plan GC
   thread'i yok.
6. Ölçüm kapısından geçenler: `System.GC.ConserveMemory`, `InvariantGlobalization`,
   `OptimizationPreference=Size`, `UseSystemResourceKeys`, HttpClient yerine doğrudan WinHTTP.
   Her biri ancak ölçüm düzeneği kazanç gösterirse ve testler + ekran görüntüleri geçerse
   alınır. `EmptyWorkingSet` kullanılmaz (kozmetik: commit düşmez, sayfalar geri döner).

## CPU kuralları

Popup kapalıyken yalnız şu uyanmalar vardır:

| Uyanma | Aralık |
|---|---|
| Claude isteği | Ayardaki aralık (60/120/300 sn) |
| Codex rollout boyut kontrolü | 30 sn |
| Codex app-server okuması | 10 dk; ayrıca bilinen her `resets_at` + 30 sn |
| Eskime değerlendirmesi | 60 sn (serileştirme yok) |

Popup açıkken ek olarak:

- Yaş ve geri sayım metinleri dakika sınırında bir kez güncellenir (metin dakika çözünürlüğünde).
- Kare zamanlayıcısı yalnız animasyon sürerken.
- Hover yalnız hedef öğe değişince yeniden çizer.

## Veri yolu

### Claude

Değişmez: `GET /api/oauth/usage`, 429'da geri çekilme, son bilinen değer yaşıyla gösterilir.
HttpClient yerine WinHTTP yalnız ölçüm kapısıyla.

### Codex

Kalıcı `codex app-server` oturumu kalkar. Yerine iki kaynak:

**1. Rollout kuyruğu (`CodexRolloutTail`).**

- Kök `~/.codex/sessions/YYYY/MM/DD/`. En yeni iki gün klasörü yalnız klasör adlarından
  bulunur (dosya taranmaz). Bu klasörlerde ada göre en yeni 4 `rollout-*.jsonl` izlenir.
- Değişiklik dosyanın **gerçek boyutuyla** yakalanır: dosya `FileShare.ReadWrite | Delete`
  ile açılır, uzunluğu okunur, önceki uzunlukla karşılaştırılır. Klasör girdisindeki
  değişiklik zamanı kullanılmaz: 2026-09-27'de yazılmakta olan 1 MB'lık dosyanın değişiklik
  zamanı oluşturulma anında takılı ölçüldü.
- Büyüyen (veya ilk kez görülen) dosyanın son 64 KB'ı okunur. Tamam olmayan son satır atılır;
  `"rate_limits"` içeren son tam satır `JsonDocument` ile ayrıştırılır. Dosya asla bütün
  okunmaz (bugünkü yedek yol dosyayı `File.ReadAllLines` ile bütün okuyor; en büyüğü 57 MB).
- Örnek zamanı satırın kendi `timestamp` alanıdır. Rollout okuması `Fresh`'tir: satır, Codex
  CLI'ın sunucudan o anda aldığı değeri taşır. Eskimeyi `QuotaStore` yönetir.
- Tampon tek ve yeniden kullanılır (`ArrayPool`).

**2. Tek atımlık app-server okuması (`CodexServerReader`).**

- Başlat → `initialize` → `initialized` → `account/rateLimits/read` → yanıt → stdin kapat →
  süreç çıkar (5 sn içinde çıkmazsa öldürülür). Sert zaman aşımı 20 sn (ölçülen 5-13 sn).
- stdin BOM'suz UTF-8 (v0.1'deki BOM kusurunun regresyon testi korunur).
- Ölçüm (2026-09-27, 3 koşum): okuma başına 125-172 ms CPU, 41-46 MB tepe özel bellek.

**Takvim (`CodexCollector`).**

- Rollout: 30 sn'de bir.
- app-server: 10 dk'da bir; bilinen her pencerenin `resets_at` + 30 sn anında; ve
  `RequestRefresh()` çağrısında (elle yenile; popup açılışında değer 2 dk'dan eskiyse host
  çağırır).
- Birleştirme: yeni okuma, eldeki değerden zaman damgası daha yeniyse yayımlanır; tek
  pencere taşıyan okuma diğer pencereyi silmez (bugünkü `Merge` kuralı).
- `resets_at`'i geçmiş bir pencerenin değeri, sıfırlanmadan sonraki bir okuma gelene kadar
  bilinmez: snapshot `Stale` olur ve yaşıyla gösterilir. Sıfırlandı diye `%0` **çıkarılmaz**.
- app-server üst üste 3 kez başarısız olursa `ProtocolBroken` yayımlanır; son bilinen sayılar
  `QuotaStore`'un mevcut kuralıyla korunur, hata metni altına yazılır. Sonraki deneme normal
  takvimde (hızlı tekrar yok). Rollout çalışmaya devam eder ve yeni değer geldiğinde sağlık
  `Fresh`'e döner.
- Bugünkü `account/rateLimits/updated` anlık bildirimi kalkar; bu makinedeki Codex
  kullanımında yerini rollout alır. Başka cihazdaki kullanım en geç 10 dk'da görünür.

### Sağlayıcı başına eskime

`QuotaStore.StaleAfter` bugün her sağlayıcı için 5 dk. Codex 10 dk'da bir okununca doğru
değer zamanın yarısında `Stale` görünürdü (v0.2'deki "doğru veri eski etiketi yiyor"
kusurunun aynısı). Eskime süresi sağlayıcı başına verilir:

- Claude: `max(5 dk, 2,5 × yenileme aralığı)`.
- Codex: 25 dk (app-server aralığının 2,5 katı).

### Sabit veri modu

`--fixture <dosya>`: toplayıcılar yerine dosyadaki snapshot'lar yayımlanır (ağ yok, alt süreç
yok). `--data-dir <klasör>`: `history.json` ve `settings.json` orada tutulur. İki amaç:
ölçüm düzeneği canlı API'yi 429'a sokmadan tekrar tekrar çalışır; beş kart durumu
(normal, dikkat, uyarı, eski, 429) ekran görüntüsü için tekrarlanabilir üretilir.

Tek örnek kilidi: `--data-dir`'e bağlı adlandırılmış mutex. İkinci örnek sessizce çıkar;
iki örnek aynı hesabı iki kez yoklayıp 429 riskini ikiye katlamaz.

## Hata yönetimi

v0.3.x ile aynı: `HealthState` sözle gösterilir, token veya yol adı gösterilmez. Native'e
özgü:

- D2D/DirectWrite kurulamazsa popup açılmaz, tepsi tooltip'i çalışmaya devam eder; bir kez
  balon bildirimi gösterilir. Uygulama çökmez.
- `Shell_NotifyIcon` ekleme başarısız olursa (Explorer hazır değil) `TaskbarCreated` beklenir.
- Toplayıcı istisnası bugünkü gibi `ProtocolBroken` + 60 sn sonra yeniden başlatma.

## Ölçüm düzeneği

`tools/Footprint/Measure-Footprint.ps1`:

- Verilen exe'yi `--fixture` ve geçici `--data-dir` ile başlatır.
- Senaryolar: (1) boşta, popup hiç açılmamış, 60 sn ısınma + 5 dk örnekleme; (2) UiProbe ile
  aç, genişlet, daralt, ayarlar, geri, kapat, sonra 2 dk; (3) popup açık 1 dk.
- 10 sn'de bir Private Bytes, çalışma kümesi, handle, thread, CPU farkı. CSV kültürden
  bağımsız yazılır (2026-09-27'de Türkçe ondalık virgülü sütunları kaydırdı).
- "Önce" sayısı: v0.3.4 yayın exe'si (kullanıcının indirdiği), gerçek ağla, tek sefer.

## Test

**Core (xUnit, ağsız):**

- `CodexRolloutTail`: yarım son satır, eşzamanlı oturumlar (en yeni zaman damgası kazanır),
  gece yarısı geçişi (iki gün klasörü), kota bloğu olmayan dosya, 64 KB'dan büyük dosyada
  yalnız sonun okunduğu, dosya büyümediyse yeniden okunmadığı, bozuk JSON.
- `CodexServerReader`: sahte süreçle mutlu yol, `initialize` yanıtsız zaman aşımı, süreç
  erken çıkışı, BOM'suz yazım.
- `CodexCollector`: takvim (sahte saat ve gecikme), `resets_at` + 30 sn okuması, zaman
  damgasıyla birleştirme, `resets_at` geçince `Stale` (asla `%0`), 3 başarısızlıkta
  `ProtocolBroken`, `RequestRefresh`.
- `QuotaStore`: sağlayıcı başına eskime.
- `HistoryStore`: kirli değilse yazmaz.
- Sabit veri modu: dosya okuma, bozuk dosya.
- Mevcut testler (token, `history.json` alanları, renk tablosu, ikon modeli) değişmeden yeşil.

**Native (xUnit):** yerleşim (kart sayısı ve genişletme durumuna göre yükseklik), tıklama
hedefi, animasyon eğrisi ve bitişi, uyanma takvimi.

**Uygulama (elle, kanıt ekran görüntüsü + ölçüm CSV'si):**

- DPI-aware yakalama; aynı sabit veriyle v0.3.4 karşılaştırması: daraltılmış ve genişletilmiş
  panel, beş kart durumu, ayarlar, açık tema, üç tepsi stili (%100/%150, 16/24 px).
- UiProbe tıklama dizisi native popup'a uyarlanır: pencere yaşıyor, eski kare yok.
- Gerçek sağlayıcıyla canlı koşum: iki sağlayıcı veri getiriyor, elle yenile, terminal açma,
  Windows ile başlat, Explorer yeniden başlatılınca ikon geri geliyor.

## Teslim

- Sürüm 0.4.0, dal `native-aot`. Codex lane'leri bu daldan açılan geçici worktree'lerde.
- Release workflow: `dotnet publish src/LimitTray.Native -r win-x64 -p:PublishAot=true`;
  SHA-256 ve `gh` adımları aynen. CI .NET 10'a geçer. Exe boyutu ölçülünce yazılır.
- `LimitTray.App` ve WPF/WinForms'a özgü dosyalar silinir; `tools/UiProbe` native popup'a
  uyarlanır.
- `AGENTS.md`: WPF'e özgü kurallar (XAML bağlama, sabit pencere) native karşılıklarıyla
  değiştirilir; NuGet istisnası ve bellek/CPU kuralları eklenir.
- `SECURITY.md` ve README: uygulama `~/.codex/sessions` altındaki dosyaların yalnız son
  64 KB'ını okur, yalnız kota bloğunu ayrıştırır, hiçbirini saklamaz; bu açıkça yazılır.
  Bekleyen gizlilik belge yaması (`settings.json`, HKCU Run değeri) bu güncellemeye katılır.
- Birleştirmeden önce taze bağlamlı, salt-okunur doğrulayıcı dalı inceler.
- `v0.4.0` etiketinin push'u herkese açık yayındır; o anda ayrıca Özenç onayı istenir.

## Roller

Spec, plan, görsel katman (`Graphics`, `Ui`, `Popup`, `TrayIconPainter`) ve inceleme
Claude'da. Core değişiklikleri, `Host`, `Tray`, `Platform`, interop, ölçüm düzeneği ve CI
Codex'e (`codex exec`, geçici worktree) delege edilir. Codex'in "tamamlandı" beyanı kanıt
sayılmaz: derleme çıktısı, test sonucu ve çalışan uygulamanın gözlemi gerekir.

## Kapsam dışı

Yeni özellik yok. Başka sağlayıcı, otomatik güncelleme, imzalı binary, ARM64 yayını bu
sürümün konusu değil.
