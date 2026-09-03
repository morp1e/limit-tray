---
title: "Agent Quota Tray — Tasarım"
created: 2026-09-03
modified: 2026-09-03
type: spec
status: approved
tags: [proje, csharp, wpf, kota, claude, codex]
---

# Agent Quota Tray — Tasarım

## Sorun

Terminalde hem Claude Code hem Codex CLI kullanılıyor. "Ne kadar hakkım kaldı?"
sorusunun cevabı iki ayrı yerde: Claude'da `/usage`, Codex'te `/status`. İkisi de
yalnız o harness'ın interaktif oturumu açıkken görülebiliyor. Sonuç: iş ortasında
limit sürprizi, ya da limiti kontrol etmek için oturum açma zorunluluğu.

## Başarı ölçütü

Görev çubuğundaki simgeye bakan biri, hiçbir oturum açmadan, iki ajanın da 5 saatlik
ve 7 günlük penceresindeki gerçek kullanım yüzdesini ve sıfırlanma zamanını görür.
Gösterilen sayı ya doğrudur ya da açıkça "güvenilmez" işaretlidir — arada bir şey yok.

## Kapsam dışı (bilinçli)

Kullanım geçmişi grafiği, eşik bildirimi, Windows açılışında başlatma, Gemini ve
diğer sağlayıcılar, maliyet takibi. Hiçbiri temel çalışmadan anlam taşımaz.

## Veri kaynakları

İkisi de 2026-09-03'te bu makinede ölçülerek doğrulandı; varsayım değil.

### Claude — OAuth usage endpoint

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <token>
anthropic-beta: oauth-2025-04-20
```

Token: `%USERPROFILE%\.claude\.credentials.json` → `claudeAiOauth.accessToken`.

Yanıt (ölçülen, kısaltılmış):

```json
{"five_hour":{"utilization":14.0,"resets_at":"2026-09-03T23:09:59Z"},
 "seven_day":{"utilization":12.0,"resets_at":"2026-09-05T08:59:59Z"},
 "limits":[{"kind":"session","percent":14,"severity":"normal","resets_at":"..."},
           {"kind":"weekly_all","percent":12,"severity":"normal","resets_at":"..."}]}
```

`utilization` 0-100 aralığında ondalık gelir. `limits` dizisi aynı bilgiyi ikinci kez
taşır; **kanonik okuma `five_hour` / `seven_day` nesneleridir**, `limits` yalnız
çapraz doğrulama için okunur ve çelişkide nesneler kazanır.

Bu bir model çağrısı değildir; kullanıcının kotasını tüketmez.

### Codex — app-server JSON-RPC

`codex app-server` süreci stdio üzerinden JSON-RPC konuşur. Protokol şeması
`codex app-server generate-json-schema --out <dir>` ile üretilebilir ve
`account/rateLimits/read` ile `account/rateLimits/updated` orada tanımlıdır.

Akış: `initialize` → `initialized` → `account/rateLimits/read` (bir kez) → süreç açık
tutulur ve `account/rateLimits/updated` bildirimleri dinlenir.

Yanıt (ölçülen):

```json
{"rateLimits":{"primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1788478826},
               "secondary":{"usedPercent":36,"windowDurationMins":10080,"resetsAt":1788817184},
               "planType":"plus"}}
```

`resetsAt` Unix saniyesidir. `primary` = 5 saatlik pencere (300 dk),
`secondary` = 7 günlük (10080 dk). Pencere süresi yanıttan okunur, sabit varsayılmaz.

Windows'ta `codex` bir npm shim'idir ve doğrudan süreç olarak başlatılamaz; gerçek
ikili `@openai/codex/node_modules/@openai/codex-win32-x64/vendor/.../bin/codex.exe`
altındadır. Uygulama ikiliyi çözerken önce `codex.cmd` üzerinden `cmd /c` kullanır,
bulunamazsa npm yolunu tarar.

### Codex yedek kaynağı

app-server başlatılamazsa `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl` içindeki
en son `"rate_limits"` bloğu okunur. Aynı alanlar snake_case olarak orada da yazılıdır.
Bu yol her zaman `Stale` işaretlidir — dosya son API çağrısı kadar eskidir.

## Mimari

```
ClaudeCollector ─┐
                 ├─→ QuotaStore ──→ TrayApp (NotifyIcon + popup)
CodexCollector  ─┘
```

Katmanlar birbirini tanımaz. UI hiçbir HTTP veya süreç detayı bilmez; yalnız
`QuotaStore`'un yaydığı olayları dinler ve `QuotaSnapshot` çizer.

### Ortak model

```csharp
record QuotaWindow(double Percent, DateTimeOffset? ResetsAt, TimeSpan WindowLength);
record QuotaSnapshot(
    string Provider,          // "claude" | "codex"
    QuotaWindow? Session,     // 5 saatlik
    QuotaWindow? Weekly,      // 7 günlük
    HealthState Health,
    DateTimeOffset FetchedAt,
    string? Detail);          // kullanıcıya gösterilecek hata açıklaması
```

`Session` ve `Weekly` null olabilir — sağlayıcı o pencereyi bildirmediğinde uydurulmaz.

### Collector sözleşmesi

```csharp
interface IQuotaCollector {
    string Provider { get; }
    IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);
}
```

Tek arayüz, iki taban tabana zıt uygulama — bu kasıtlıdır:

- **ClaudeCollector** çeker. 60 saniyede bir istek. Token her istekte dosyadan taze
  okunur, çünkü Claude Code onu yenilemiş olabilir; bellekte tutulmaz.
- **CodexCollector** dinler. Süreç açık kalır, veri kendi gelir. Süreç ölürse yeniden
  başlatılır (üstel geri çekilme, max 5 dk); üç kez üst üste başlatılamazsa yedek
  dosya kaynağına düşer.

## Sağlık durumları

Bu projenin asıl zorluğu ekran çizmek değil, yanlış sayı göstermemektir. İki veri yolu
da resmî değildir: `anthropic-beta` başlığı belgelenmemiş ve tarihlidir, app-server
protokolü "experimental" işaretlidir. Bu yüzden sağlık birinci sınıf alandır.

| Durum | Tetikleyici | Panelde |
|---|---|---|
| `Fresh` | başarılı okuma, < 2 dk | sayı + "şimdi güncellendi" |
| `Stale` | son başarılı okuma > 2 dk | sayı **gri** + yaş |
| `RateLimited` | HTTP 429 | "geçici sınırlı, N dk sonra" |
| `AuthMissing` | credentials yok / 401 | "giriş gerekli" |
| `ProtocolBroken` | 4xx, beklenmeyen şema, eksik alan | "API değişmiş" |

**Hiçbir hata durumu `%0` olarak gösterilmez.** `%0` gerçek bir değerdir — ölçüm
gününde Codex'in 5 saatlik penceresi gerçekten `%0` idi. Hatayı sıfırla karıştırmak
bu aracın tek ölümcül kusuru olur.

429'da üstel geri çekilme: 2, 4, 8 dk, tavan 15 dk. Endpoint'in kendi rate limit'i
vardır ve agresif çağrı 429 üretir (anthropics/claude-code#31021, "closed as not
planned"). Geri çekilme sırasında son bilinen değer `Stale` olarak gösterilir.

## Arayüz

- **Simge:** dört yüzdeden en yükseği, halka olarak. Tek bakışta "durum iyi mi".
  Herhangi bir sağlayıcı sağlıksızsa simge uyarı rengine döner.
- **Tooltip:** `CC 14% / 12%  ·  CX 0% / 36%`
- **Popup:** sağlayıcı başına bir blok; Session ve Weekly satırları, yüzde, ilerleme
  çubuğu, "4s 38d sonra sıfırlanır". Altta elle yenileme ve son güncelleme zamanı.
- Popup tray simgesine tıklayınca açılır, odak kaybında kapanır.

Yüzde renkleri: < %60 normal, %60-85 dikkat, > %85 uyarı. Eşikler tek yerde sabit.

## Test

Collector'lar ağ ve süreç bağımlılığını arayüz arkasına alır (`IHttpClient`,
`IProcessRunner`), böylece testler sahte yanıtla çalışır. Zorunlu kapsam:

- Claude: 200 normal, 401, 429, bozuk JSON, `five_hour` eksik, `resets_at` geçmişte
- Codex: normal yanıt, `initialize` yanıtı gelmeden `read`, süreç erken ölümü,
  `updated` bildiriminin snapshot'ı güncellemesi, yedek dosya kaynağına düşüş
- QuotaStore: `Fresh` → `Stale` geçişinin yaşa göre doğru anda olması
- Yüzde biçimlendirme: ondalık `14.0` → `%14`

Gerçek ağ çağrısı yapan testler ayrı kategoridedir ve varsayılan koşuda çalışmaz.

## Güvenlik

Token loglanmaz, diske yazılmaz, pencerede veya tooltip'te görünmez, istisna
metinlerine sızmaz. Yalnız `Authorization` başlığında Anthropic'e gider. Uygulama
credentials dosyasına yalnız okuma için erişir.

## Repo yapısı

```
agent-quota-tray/
  AGENTS.md            # kanonik proje kuralları
  CLAUDE.md            # @AGENTS.md köprüsü
  docs/specs/          # bu dosya
  src/AgentQuotaTray/  # WPF uygulaması
  tests/AgentQuotaTray.Tests/
```

Hedef: .NET 9 / WPF (makinede `9.0.310` SDK ve `WindowsDesktop 9.0.19` runtime
kurulu, ek kurulum gerekmiyor).

## Bilinen riskler

1. `anthropic-beta: oauth-2025-04-20` belgelenmemiştir; Anthropic değiştirirse Claude
   tarafı sessizce kırılır. Karşılığı `ProtocolBroken` durumudur — araç yanlış sayı
   göstermek yerine bozulduğunu söyler.
2. `codex app-server` "experimental" işaretlidir; metot adları sürümle değişebilir.
   Karşılığı yedek dosya kaynağıdır.
3. Windows'ta npm shim'i nedeniyle ikili yolu kırılgandır; çözüm sırası koda gömülür
   ve bulunamazsa `ProtocolBroken` verir.
4. Endpoint'in kendi rate limit'i belgelenmemiştir; 60 sn aralık ölçülmüş bir eşik
   değil, muhafazakâr bir tahmindir. 429 görülürse geri çekilme devreye girer.

## Rol dağılımı

Kanonik metin ve inceleme Claude'da; C# üretimi Codex'e delege edilir. Codex'in
"tamamlandı" beyanı kanıt değildir — derleme çıktısı, test sonucu ve çalışan
uygulamanın gözlenmesi gerekir.
