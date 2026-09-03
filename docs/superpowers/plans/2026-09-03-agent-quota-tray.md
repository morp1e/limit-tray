# Agent Quota Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows görev çubuğunda, hiçbir oturum açmadan Claude Code ve Codex CLI'ın 5 saatlik ve 7 günlük kota yüzdelerini gerçek veriyle gösteren bir tray uygulaması.

**Architecture:** İki collector (Claude: HTTP poll, Codex: app-server JSON-RPC push) ortak `QuotaSnapshot` üretir; `QuotaStore` tazeliği yönetip olay yayar; WPF tray katmanı yalnız çizer. Tüm mantık WPF'e bağımlı olmayan `Core` kütüphanesindedir, böylece testler başsız koşar.

**Tech Stack:** .NET 9, C# 13, WPF (net9.0-windows), xUnit, System.Text.Json. Harici NuGet paketi yok — `NotifyIcon` için `System.Windows.Forms` referansı kullanılır.

**Spec:** `docs/specs/2026-09-03-agent-quota-tray-design.md`

## Global Constraints

- İki proje: `src/AgentQuotaTray.Core` (`net9.0`, WPF referansı **yasak**) ve `src/AgentQuotaTray.App` (`net9.0-windows`, `UseWPF` + `UseWindowsForms`). Testler yalnız Core'a bağlanır.
- `<Nullable>enable</Nullable>` ve `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` her projede.
- Harici NuGet bağımlılığı yok (test projesindeki xUnit hariç).
- Token loglanmaz, diske yazılmaz, `ToString()`'e girmez, istisna metnine sızmaz.
- Hiçbir hata durumu `%0` olarak gösterilmez — `%0` yalnız sağlayıcıdan gelen gerçek değerdir.
- Kullanıcıya görünen metinler Türkçe; kod, dosya adları, commit mesajları ASCII.
- Claude kanonik okuma: `five_hour` / `seven_day` nesneleri. `limits` dizisi yalnız çapraz kontrol; çelişkide nesneler kazanır.
- Pencere süresi (`windowDurationMins`) yanıttan okunur, sabit varsayılmaz.

---

### Task 1: Çözüm iskeleti, ortak model, Claude yanıt ayrıştırıcı

**Files:**
- Create: `AgentQuotaTray.sln`
- Create: `src/AgentQuotaTray.Core/AgentQuotaTray.Core.csproj`
- Create: `src/AgentQuotaTray.Core/Model/HealthState.cs`
- Create: `src/AgentQuotaTray.Core/Model/QuotaWindow.cs`
- Create: `src/AgentQuotaTray.Core/Model/QuotaSnapshot.cs`
- Create: `src/AgentQuotaTray.Core/Claude/ClaudeUsageParser.cs`
- Create: `tests/AgentQuotaTray.Tests/AgentQuotaTray.Tests.csproj`
- Test: `tests/AgentQuotaTray.Tests/Claude/ClaudeUsageParserTests.cs`

**Interfaces:**
- Consumes: yok (ilk görev)
- Produces: `HealthState` enum; `QuotaWindow(double Percent, DateTimeOffset? ResetsAt, TimeSpan WindowLength)`; `QuotaSnapshot(string Provider, QuotaWindow? Session, QuotaWindow? Weekly, HealthState Health, DateTimeOffset FetchedAt, string? Detail)`; `static QuotaSnapshot ClaudeUsageParser.Parse(string json, DateTimeOffset now)`

- [ ] **Step 1: Çözümü ve projeleri oluştur**

```bash
cd C:/Users/ozncd/Documents/Isler/agent-quota-tray
dotnet new sln -n AgentQuotaTray
dotnet new classlib -o src/AgentQuotaTray.Core -f net9.0
dotnet new xunit  -o tests/AgentQuotaTray.Tests -f net9.0
rm src/AgentQuotaTray.Core/Class1.cs tests/AgentQuotaTray.Tests/UnitTest1.cs
dotnet sln add src/AgentQuotaTray.Core tests/AgentQuotaTray.Tests
dotnet add tests/AgentQuotaTray.Tests reference src/AgentQuotaTray.Core
```

Her iki `.csproj` içindeki `<PropertyGroup>` bloğuna ekle:

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<LangVersion>13.0</LangVersion>
```

- [ ] **Step 2: Model tiplerini yaz**

`src/AgentQuotaTray.Core/Model/HealthState.cs`:

```csharp
namespace AgentQuotaTray.Core.Model;

public enum HealthState
{
    Fresh,
    Stale,
    RateLimited,
    AuthMissing,
    ProtocolBroken
}
```

`src/AgentQuotaTray.Core/Model/QuotaWindow.cs`:

```csharp
namespace AgentQuotaTray.Core.Model;

/// <summary>Tek bir kota penceresi. Percent 0-100 araligindadir.</summary>
public sealed record QuotaWindow(
    double Percent,
    DateTimeOffset? ResetsAt,
    TimeSpan WindowLength);
```

`src/AgentQuotaTray.Core/Model/QuotaSnapshot.cs`:

```csharp
namespace AgentQuotaTray.Core.Model;

public sealed record QuotaSnapshot(
    string Provider,
    QuotaWindow? Session,
    QuotaWindow? Weekly,
    HealthState Health,
    DateTimeOffset FetchedAt,
    string? Detail)
{
    public static QuotaSnapshot Unhealthy(
        string provider, HealthState health, DateTimeOffset now, string detail) =>
        new(provider, null, null, health, now, detail);
}
```

- [ ] **Step 3: Ayrıştırıcı testlerini yaz (başarısız olacak)**

`tests/AgentQuotaTray.Tests/Claude/ClaudeUsageParserTests.cs`:

```csharp
using AgentQuotaTray.Core.Claude;
using AgentQuotaTray.Core.Model;
using Xunit;

namespace AgentQuotaTray.Tests.Claude;

public class ClaudeUsageParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private const string RealResponse = """
    {"five_hour":{"utilization":14.0,"resets_at":"2026-09-03T23:09:59.727061+00:00"},
     "seven_day":{"utilization":12.0,"resets_at":"2026-09-05T08:59:59.727081+00:00"},
     "limits":[{"kind":"session","percent":14,"resets_at":"2026-09-03T23:09:59.727061+00:00"},
               {"kind":"weekly_all","percent":12,"resets_at":"2026-09-05T08:59:59.727081+00:00"}]}
    """;

    [Fact]
    public void Parse_RealResponse_ReadsBothWindows()
    {
        var snap = ClaudeUsageParser.Parse(RealResponse, Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal("claude", snap.Provider);
        Assert.Equal(14.0, snap.Session!.Percent);
        Assert.Equal(12.0, snap.Weekly!.Percent);
        Assert.Equal(TimeSpan.FromHours(5), snap.Session.WindowLength);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly.WindowLength);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 23, 9, 59, TimeSpan.Zero),
            snap.Session.ResetsAt!.Value.TruncateToSeconds());
    }

    [Fact]
    public void Parse_ZeroPercent_IsFreshNotError()
    {
        var snap = ClaudeUsageParser.Parse(
            """{"five_hour":{"utilization":0.0,"resets_at":null},"seven_day":null}""", Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(0.0, snap.Session!.Percent);
        Assert.Null(snap.Weekly);
    }

    [Fact]
    public void Parse_MissingFiveHour_IsProtocolBroken()
    {
        var snap = ClaudeUsageParser.Parse("""{"seven_day":{"utilization":12.0}}""", Now);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Null(snap.Session);
    }

    [Fact]
    public void Parse_Garbage_IsProtocolBroken()
    {
        var snap = ClaudeUsageParser.Parse("not json at all", Now);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.NotNull(snap.Detail);
    }
}

internal static class DateTimeOffsetTestExtensions
{
    public static DateTimeOffset TruncateToSeconds(this DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second,
            value.Offset);
}
```

- [ ] **Step 4: Testleri çalıştır, başarısız olduklarını gör**

Run: `dotnet test tests/AgentQuotaTray.Tests`
Expected: FAIL — `ClaudeUsageParser` tipi bulunamıyor (CS0246).

- [ ] **Step 5: Ayrıştırıcıyı yaz**

`src/AgentQuotaTray.Core/Claude/ClaudeUsageParser.cs`:

```csharp
using System.Text.Json;
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Claude;

public static class ClaudeUsageParser
{
    public const string Provider = "claude";

    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    private static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);

    public static QuotaSnapshot Parse(string json, DateTimeOffset now)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now, "Yanit JSON degil: " + ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var session = ReadWindow(root, "five_hour", FiveHours);
            var weekly = ReadWindow(root, "seven_day", SevenDays);

            // five_hour hicbir zaman opsiyonel degildir; yoksa sema degismistir.
            if (session is null && !HasExplicitNull(root, "five_hour"))
            {
                return QuotaSnapshot.Unhealthy(
                    Provider, HealthState.ProtocolBroken, now,
                    "Yanitta five_hour alani yok");
            }

            return new QuotaSnapshot(Provider, session, weekly, HealthState.Fresh, now, null);
        }
    }

    private static bool HasExplicitNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Null;

    private static QuotaWindow? ReadWindow(JsonElement root, string name, TimeSpan length)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;

        if (!el.TryGetProperty("utilization", out var util) ||
            util.ValueKind != JsonValueKind.Number)
            return null;

        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty("resets_at", out var reset) &&
            reset.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(reset.GetString(), out var parsed))
        {
            resetsAt = parsed;
        }

        return new QuotaWindow(util.GetDouble(), resetsAt, length);
    }
}
```

- [ ] **Step 6: Testleri çalıştır, geçtiklerini gör**

Run: `dotnet test tests/AgentQuotaTray.Tests`
Expected: PASS — 4 test.

- [ ] **Step 7: Commit**

```bash
git add AgentQuotaTray.sln src tests
git commit -m "feat(core): quota model and claude usage parser"
```

---

### Task 2: Claude collector — token okuma, HTTP, hata durumları

**Files:**
- Create: `src/AgentQuotaTray.Core/Claude/ClaudeCredentialReader.cs`
- Create: `src/AgentQuotaTray.Core/Http/IHttpTransport.cs`
- Create: `src/AgentQuotaTray.Core/Http/HttpTransportResult.cs`
- Create: `src/AgentQuotaTray.Core/Http/SystemHttpTransport.cs`
- Create: `src/AgentQuotaTray.Core/Collectors/IQuotaCollector.cs`
- Create: `src/AgentQuotaTray.Core/Claude/ClaudeCollector.cs`
- Test: `tests/AgentQuotaTray.Tests/Claude/ClaudeCollectorTests.cs`

**Interfaces:**
- Consumes: `ClaudeUsageParser.Parse`, `QuotaSnapshot`, `HealthState` (Task 1)
- Produces: `IQuotaCollector { string Provider { get; } IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct); }`; `IHttpTransport { Task<HttpTransportResult> GetAsync(string url, IReadOnlyDictionary<string,string> headers, CancellationToken ct); }`; `HttpTransportResult(int StatusCode, string Body)`; `ClaudeCollector(IHttpTransport, ClaudeCredentialReader, Func<DateTimeOffset> clock, Func<TimeSpan,CancellationToken,Task> delay)`

- [ ] **Step 1: Testleri yaz (başarısız olacak)**

`tests/AgentQuotaTray.Tests/Claude/ClaudeCollectorTests.cs`:

```csharp
using AgentQuotaTray.Core.Claude;
using AgentQuotaTray.Core.Http;
using AgentQuotaTray.Core.Model;
using Xunit;

namespace AgentQuotaTray.Tests.Claude;

public class ClaudeCollectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeTransport : IHttpTransport
    {
        private readonly Queue<HttpTransportResult> _results;
        public List<IReadOnlyDictionary<string, string>> SeenHeaders { get; } = new();
        public FakeTransport(params HttpTransportResult[] results) =>
            _results = new Queue<HttpTransportResult>(results);

        public Task<HttpTransportResult> GetAsync(
            string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            SeenHeaders.Add(headers);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private static ClaudeCollector Build(
        IHttpTransport transport, string? token, List<TimeSpan>? delays = null) =>
        new(transport,
            new ClaudeCredentialReader(() => token),
            () => Now,
            (d, _) => { delays?.Add(d); return Task.CompletedTask; });

    private static async Task<List<QuotaSnapshot>> Take(
        ClaudeCollector collector, int count)
    {
        var result = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource();
        await foreach (var snap in collector.Watch(cts.Token))
        {
            result.Add(snap);
            if (result.Count >= count) { cts.Cancel(); break; }
        }
        return result;
    }

    [Fact]
    public async Task Watch_Success_YieldsFreshSnapshot()
    {
        var body = """{"five_hour":{"utilization":14.0},"seven_day":{"utilization":12.0}}""";
        var transport = new FakeTransport(new HttpTransportResult(200, body));

        var snaps = await Take(Build(transport, "tok-abc"), 1);

        Assert.Equal(HealthState.Fresh, snaps[0].Health);
        Assert.Equal(14.0, snaps[0].Session!.Percent);
    }

    [Fact]
    public async Task Watch_SendsBearerAndBetaHeaders()
    {
        var transport = new FakeTransport(
            new HttpTransportResult(200, """{"five_hour":{"utilization":1.0}}"""));

        await Take(Build(transport, "tok-abc"), 1);

        var headers = transport.SeenHeaders[0];
        Assert.Equal("Bearer tok-abc", headers["Authorization"]);
        Assert.Equal("oauth-2025-04-20", headers["anthropic-beta"]);
    }

    [Fact]
    public async Task Watch_NoToken_YieldsAuthMissing()
    {
        var snaps = await Take(Build(new FakeTransport(), token: null), 1);

        Assert.Equal(HealthState.AuthMissing, snaps[0].Health);
        Assert.Null(snaps[0].Session);
    }

    [Fact]
    public async Task Watch_401_YieldsAuthMissing()
    {
        var transport = new FakeTransport(new HttpTransportResult(401, "{}"));

        var snaps = await Take(Build(transport, "tok-abc"), 1);

        Assert.Equal(HealthState.AuthMissing, snaps[0].Health);
    }

    [Fact]
    public async Task Watch_429_YieldsRateLimitedAndBacksOff()
    {
        // Snapshot ONCE yayilir, bekleme SONRA gelir. Bu yuzden N gecikme gozlemek
        // icin N+1 snapshot alinir; aksi halde son bekleme hic calismaz.
        var delays = new List<TimeSpan>();
        var transport = new FakeTransport(
            new HttpTransportResult(429, "{}"),
            new HttpTransportResult(429, "{}"),
            new HttpTransportResult(429, "{}"));

        var snaps = await Take(Build(transport, "tok-abc", delays), 3);

        Assert.All(snaps, s => Assert.Equal(HealthState.RateLimited, s.Health));
        Assert.Equal(TimeSpan.FromMinutes(2), delays[0]);
        Assert.Equal(TimeSpan.FromMinutes(4), delays[1]);
    }

    [Fact]
    public async Task Watch_BackoffIsCappedAt15Minutes()
    {
        var delays = new List<TimeSpan>();
        var results = Enumerable.Range(0, 8)
            .Select(_ => new HttpTransportResult(429, "{}")).ToArray();

        await Take(Build(new FakeTransport(results), "tok-abc", delays), 8);

        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromMinutes(15)));
        Assert.Equal(TimeSpan.FromMinutes(15), delays[^1]);
    }

    [Fact]
    public async Task Watch_500_YieldsProtocolBroken()
    {
        var transport = new FakeTransport(new HttpTransportResult(500, "oops"));

        var snaps = await Take(Build(transport, "tok-abc"), 1);

        Assert.Equal(HealthState.ProtocolBroken, snaps[0].Health);
    }

    [Fact]
    public async Task Watch_SuccessAfter429_ResetsBackoffToNormalInterval()
    {
        var delays = new List<TimeSpan>();
        var transport = new FakeTransport(
            new HttpTransportResult(429, "{}"),
            new HttpTransportResult(200, """{"five_hour":{"utilization":5.0}}"""),
            new HttpTransportResult(200, """{"five_hour":{"utilization":5.0}}"""));

        await Take(Build(transport, "tok-abc", delays), 3);

        Assert.Equal(TimeSpan.FromMinutes(2), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(60), delays[1]);
    }

    [Fact]
    public async Task Watch_ErrorDetail_NeverContainsToken()
    {
        var transport = new FakeTransport(new HttpTransportResult(500, "oops"));

        var snaps = await Take(Build(transport, "super-secret-token"), 1);

        Assert.DoesNotContain("super-secret-token", snaps[0].Detail ?? "");
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduklarını gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter ClaudeCollectorTests`
Expected: FAIL — `IHttpTransport`, `ClaudeCollector`, `ClaudeCredentialReader` bulunamıyor.

- [ ] **Step 3: Taşıma ve credential tiplerini yaz**

`src/AgentQuotaTray.Core/Http/HttpTransportResult.cs`:

```csharp
namespace AgentQuotaTray.Core.Http;

public sealed record HttpTransportResult(int StatusCode, string Body);
```

`src/AgentQuotaTray.Core/Http/IHttpTransport.cs`:

```csharp
namespace AgentQuotaTray.Core.Http;

public interface IHttpTransport
{
    Task<HttpTransportResult> GetAsync(
        string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}
```

`src/AgentQuotaTray.Core/Http/SystemHttpTransport.cs`:

```csharp
namespace AgentQuotaTray.Core.Http;

public sealed class SystemHttpTransport : IHttpTransport, IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<HttpTransportResult> GetAsync(
        string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new HttpTransportResult((int)response.StatusCode, body);
    }

    public void Dispose() => _client.Dispose();
}
```

`src/AgentQuotaTray.Core/Claude/ClaudeCredentialReader.cs`:

```csharp
using System.Text.Json;

namespace AgentQuotaTray.Core.Claude;

/// <summary>
/// Token'i her cagrida diskten taze okur; Claude Code onu yenilemis olabilir.
/// Token asla alanda saklanmaz ve asla loglanmaz.
/// </summary>
public sealed class ClaudeCredentialReader
{
    private readonly Func<string?> _read;

    public ClaudeCredentialReader(Func<string?> read) => _read = read;

    public static ClaudeCredentialReader FromDefaultPath() =>
        new(() => ReadFromFile(DefaultPath));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    public string? ReadToken() => _read();

    private static string? ReadFromFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            if (!oauth.TryGetProperty("accessToken", out var token)) return null;
            var value = token.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Collector arayüzünü ve Claude collector'ı yaz**

`src/AgentQuotaTray.Core/Collectors/IQuotaCollector.cs`:

```csharp
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Collectors;

public interface IQuotaCollector
{
    string Provider { get; }
    IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);
}
```

`src/AgentQuotaTray.Core/Claude/ClaudeCollector.cs`:

```csharp
using System.Runtime.CompilerServices;
using AgentQuotaTray.Core.Collectors;
using AgentQuotaTray.Core.Http;
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Claude;

public sealed class ClaudeCollector : IQuotaCollector
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    public const string BetaHeader = "oauth-2025-04-20";

    private static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private readonly IHttpTransport _transport;
    private readonly ClaudeCredentialReader _credentials;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ClaudeCollector(
        IHttpTransport transport,
        ClaudeCredentialReader credentials,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _transport = transport;
        _credentials = credentials;
        _clock = clock;
        _delay = delay;
    }

    public string Provider => ClaudeUsageParser.Provider;

    public async IAsyncEnumerable<QuotaSnapshot> Watch(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var backoff = FirstBackoff;

        while (!ct.IsCancellationRequested)
        {
            var (snapshot, rateLimited) = await FetchOnce(ct).ConfigureAwait(false);

            // SIRA KRITIK: once yayin, sonra bekleme. Tersi olursa uygulama
            // acilista bir tam aralik boyunca bos kalir ve her deger bir cevrim
            // geç gorunur. Bir testi gecirmek icin bu sira degistirilmez.
            yield return snapshot;

            if (ct.IsCancellationRequested) yield break;

            TimeSpan wait;
            if (rateLimited)
            {
                wait = backoff;
                backoff = backoff >= MaxBackoff
                    ? MaxBackoff
                    : Min(backoff + backoff, MaxBackoff);
            }
            else
            {
                wait = NormalInterval;
                backoff = FirstBackoff;
            }

            await _delay(wait, ct).ConfigureAwait(false);
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private async Task<(QuotaSnapshot Snapshot, bool RateLimited)> FetchOnce(
        CancellationToken ct)
    {
        var now = _clock();
        var token = _credentials.ReadToken();

        if (token is null)
        {
            return (QuotaSnapshot.Unhealthy(
                Provider, HealthState.AuthMissing, now,
                "Claude oturumu bulunamadi, giris gerekli"), false);
        }

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + token,
            ["anthropic-beta"] = BetaHeader,
            ["Content-Type"] = "application/json",
        };

        HttpTransportResult result;
        try
        {
            result = await _transport.GetAsync(UsageUrl, headers, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now,
                "Baglanti kurulamadi: " + ex.GetType().Name), false);
        }

        return result.StatusCode switch
        {
            200 => (ClaudeUsageParser.Parse(result.Body, now), false),
            401 or 403 => (QuotaSnapshot.Unhealthy(
                Provider, HealthState.AuthMissing, now,
                "Token gecersiz, giris gerekli"), false),
            429 => (QuotaSnapshot.Unhealthy(
                Provider, HealthState.RateLimited, now,
                "Gecici olarak sinirli"), true),
            _ => (QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now,
                $"Beklenmeyen yanit: HTTP {result.StatusCode}"), false),
        };
    }
}
```

Hata metinlerinde yalnız durum kodu ve istisna tipi yer alır; gövde ve token asla yazılmaz.

- [ ] **Step 5: Testleri çalıştır, geçtiklerini gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter ClaudeCollectorTests`
Expected: PASS — 9 test (Task 1'in 4 testi ayrica gecmeye devam eder).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(claude): usage collector with backoff and health states"
```

---

### Task 3: Codex yanıt ayrıştırıcıları — app-server ve rollout yedeği

**Files:**
- Create: `src/AgentQuotaTray.Core/Codex/CodexRateLimitsParser.cs`
- Create: `src/AgentQuotaTray.Core/Codex/CodexRolloutReader.cs`
- Test: `tests/AgentQuotaTray.Tests/Codex/CodexRateLimitsParserTests.cs`
- Test: `tests/AgentQuotaTray.Tests/Codex/CodexRolloutReaderTests.cs`

**Interfaces:**
- Consumes: `QuotaSnapshot`, `QuotaWindow`, `HealthState` (Task 1)
- Produces: `static QuotaSnapshot CodexRateLimitsParser.ParseAppServer(string json, DateTimeOffset now)`; `static QuotaSnapshot? CodexRolloutReader.ReadLatest(string sessionsRoot, DateTimeOffset now)`

- [ ] **Step 1: Testleri yaz (başarısız olacak)**

`tests/AgentQuotaTray.Tests/Codex/CodexRateLimitsParserTests.cs`:

```csharp
using AgentQuotaTray.Core.Codex;
using AgentQuotaTray.Core.Model;
using Xunit;

namespace AgentQuotaTray.Tests.Codex;

public class CodexRateLimitsParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    // app-server'dan olculen gercek yanit
    private const string RealResult = """
    {"id":2,"result":{"rateLimits":{"limitId":"codex",
      "primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1788478826},
      "secondary":{"usedPercent":36,"windowDurationMins":10080,"resetsAt":1788817184},
      "planType":"plus"}}}
    """;

    [Fact]
    public void ParseAppServer_RealResult_ReadsBothWindows()
    {
        var snap = CodexRateLimitsParser.ParseAppServer(RealResult, Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal("codex", snap.Provider);
        Assert.Equal(0.0, snap.Session!.Percent);
        Assert.Equal(36.0, snap.Weekly!.Percent);
        Assert.Equal(TimeSpan.FromMinutes(300), snap.Session.WindowLength);
        Assert.Equal(TimeSpan.FromMinutes(10080), snap.Weekly.WindowLength);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1788478826), snap.Session.ResetsAt);
    }

    [Fact]
    public void ParseAppServer_NotificationShape_IsAlsoAccepted()
    {
        const string notification = """
        {"method":"account/rateLimits/updated","params":{"rateLimits":{
          "primary":{"usedPercent":7,"windowDurationMins":300,"resetsAt":1788478826}}}}
        """;

        var snap = CodexRateLimitsParser.ParseAppServer(notification, Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(7.0, snap.Session!.Percent);
        Assert.Null(snap.Weekly);
    }

    [Fact]
    public void ParseAppServer_MissingRateLimits_IsProtocolBroken()
    {
        var snap = CodexRateLimitsParser.ParseAppServer("""{"id":2,"result":{}}""", Now);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
    }

    [Fact]
    public void ParseAppServer_WindowDurationMissing_FallsBackToKnownWindows()
    {
        const string json = """
        {"result":{"rateLimits":{"primary":{"usedPercent":5},
                                 "secondary":{"usedPercent":9}}}}
        """;

        var snap = CodexRateLimitsParser.ParseAppServer(json, Now);

        Assert.Equal(TimeSpan.FromHours(5), snap.Session!.WindowLength);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly!.WindowLength);
    }
}
```

`tests/AgentQuotaTray.Tests/Codex/CodexRolloutReaderTests.cs`:

```csharp
using AgentQuotaTray.Core.Codex;
using AgentQuotaTray.Core.Model;
using Xunit;

namespace AgentQuotaTray.Tests.Codex;

public class CodexRolloutReaderTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "aqt-tests-" + Guid.NewGuid().ToString("N"));

    public CodexRolloutReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteRollout(string name, string content, DateTime lastWrite)
    {
        var dir = Path.Combine(_root, "2026", "09", "03");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }

    [Fact]
    public void ReadLatest_ReadsSnakeCaseRateLimits_AsStale()
    {
        WriteRollout("rollout-a.jsonl", """
        {"type":"event"}
        {"rate_limits":{"limit_id":"codex","primary":{"used_percent":1.0,"window_minutes":300,"resets_at":1787753045},"secondary":{"used_percent":42.0,"window_minutes":10080,"resets_at":1788817184}}}
        """, new DateTime(2026, 9, 3, 19, 0, 0, DateTimeKind.Utc));

        var snap = CodexRolloutReader.ReadLatest(_root, Now);

        Assert.NotNull(snap);
        Assert.Equal(HealthState.Stale, snap!.Health);
        Assert.Equal(1.0, snap.Session!.Percent);
        Assert.Equal(42.0, snap.Weekly!.Percent);
    }

    [Fact]
    public void ReadLatest_PrefersMostRecentlyWrittenFile()
    {
        WriteRollout("rollout-old.jsonl",
            """{"rate_limits":{"primary":{"used_percent":11.0}}}""",
            new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc));
        WriteRollout("rollout-new.jsonl",
            """{"rate_limits":{"primary":{"used_percent":22.0}}}""",
            new DateTime(2026, 9, 3, 19, 0, 0, DateTimeKind.Utc));

        var snap = CodexRolloutReader.ReadLatest(_root, Now);

        Assert.Equal(22.0, snap!.Session!.Percent);
    }

    [Fact]
    public void ReadLatest_UsesLastRateLimitsLineInFile()
    {
        WriteRollout("rollout-a.jsonl", """
        {"rate_limits":{"primary":{"used_percent":5.0}}}
        {"rate_limits":{"primary":{"used_percent":9.0}}}
        """, new DateTime(2026, 9, 3, 19, 0, 0, DateTimeKind.Utc));

        var snap = CodexRolloutReader.ReadLatest(_root, Now);

        Assert.Equal(9.0, snap!.Session!.Percent);
    }

    [Fact]
    public void ReadLatest_NoFiles_ReturnsNull() =>
        Assert.Null(CodexRolloutReader.ReadLatest(_root, Now));

    [Fact]
    public void ReadLatest_MissingDirectory_ReturnsNull() =>
        Assert.Null(CodexRolloutReader.ReadLatest(
            Path.Combine(_root, "yok"), Now));
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduklarını gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter Codex`
Expected: FAIL — `CodexRateLimitsParser` ve `CodexRolloutReader` bulunamıyor.

- [ ] **Step 3: app-server ayrıştırıcısını yaz**

`src/AgentQuotaTray.Core/Codex/CodexRateLimitsParser.cs`:

```csharp
using System.Text.Json;
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Codex;

public static class CodexRateLimitsParser
{
    public const string Provider = "codex";

    private static readonly TimeSpan DefaultSessionWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan DefaultWeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>result.rateLimits veya params.rateLimits tasiyan mesaji ayristirir.</summary>
    public static QuotaSnapshot ParseAppServer(string json, DateTimeOffset now)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now, "Yanit JSON degil: " + ex.Message);
        }

        using (doc)
        {
            if (!TryFindRateLimits(doc.RootElement, out var limits))
            {
                return QuotaSnapshot.Unhealthy(
                    Provider, HealthState.ProtocolBroken, now,
                    "Yanitta rateLimits alani yok");
            }

            var session = ReadWindow(limits, "primary", "usedPercent",
                "windowDurationMins", "resetsAt", DefaultSessionWindow);
            var weekly = ReadWindow(limits, "secondary", "usedPercent",
                "windowDurationMins", "resetsAt", DefaultWeeklyWindow);

            if (session is null && weekly is null)
            {
                return QuotaSnapshot.Unhealthy(
                    Provider, HealthState.ProtocolBroken, now,
                    "rateLimits icinde pencere yok");
            }

            return new QuotaSnapshot(Provider, session, weekly, HealthState.Fresh, now, null);
        }
    }

    internal static QuotaWindow? ReadWindow(
        JsonElement parent, string name, string percentField, string windowField,
        string resetField, TimeSpan fallbackWindow)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;
        if (!el.TryGetProperty(percentField, out var pct) ||
            pct.ValueKind != JsonValueKind.Number)
            return null;

        var window = fallbackWindow;
        if (el.TryGetProperty(windowField, out var mins) &&
            mins.ValueKind == JsonValueKind.Number)
        {
            window = TimeSpan.FromMinutes(mins.GetDouble());
        }

        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty(resetField, out var reset) &&
            reset.ValueKind == JsonValueKind.Number)
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(reset.GetInt64());
        }

        return new QuotaWindow(pct.GetDouble(), resetsAt, window);
    }

    private static bool TryFindRateLimits(JsonElement root, out JsonElement limits)
    {
        foreach (var container in new[] { "result", "params" })
        {
            if (root.TryGetProperty(container, out var el) &&
                el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("rateLimits", out limits) &&
                limits.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        if (root.TryGetProperty("rateLimits", out limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        limits = default;
        return false;
    }
}
```

- [ ] **Step 4: Rollout yedek okuyucusunu yaz**

`src/AgentQuotaTray.Core/Codex/CodexRolloutReader.cs`:

```csharp
using System.Text.Json;
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Codex;

/// <summary>
/// app-server calismadiginda son bilinen kotayi rollout dosyalarindan okur.
/// Bu kaynak her zaman Stale'dir: dosya son API cagrisi kadar eskidir.
/// </summary>
public static class CodexRolloutReader
{
    private static readonly TimeSpan DefaultSessionWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan DefaultWeeklyWindow = TimeSpan.FromDays(7);

    public static string DefaultSessionsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "sessions");

    public static QuotaSnapshot? ReadLatest(string sessionsRoot, DateTimeOffset now)
    {
        if (!Directory.Exists(sessionsRoot)) return null;

        string[] files;
        try
        {
            files = Directory.GetFiles(sessionsRoot, "rollout-*.jsonl",
                SearchOption.AllDirectories);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        foreach (var path in files.OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var snapshot = ReadFile(path, now);
            if (snapshot is not null) return snapshot;
        }

        return null;
    }

    private static QuotaSnapshot? ReadFile(string path, DateTimeOffset now)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!TryFind(doc.RootElement, out var limits)) continue;

                var session = CodexRateLimitsParser.ReadWindow(limits, "primary",
                    "used_percent", "window_minutes", "resets_at", DefaultSessionWindow);
                var weekly = CodexRateLimitsParser.ReadWindow(limits, "secondary",
                    "used_percent", "window_minutes", "resets_at", DefaultWeeklyWindow);

                if (session is null && weekly is null) continue;

                return new QuotaSnapshot(
                    CodexRateLimitsParser.Provider, session, weekly,
                    HealthState.Stale, now,
                    "app-server yok, son bilinen deger dosyadan okundu");
            }
        }

        return null;
    }

    private static bool TryFind(JsonElement root, out JsonElement limits)
    {
        if (root.TryGetProperty("rate_limits", out limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("rate_limits", out limits) &&
                limits.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        limits = default;
        return false;
    }
}
```

- [ ] **Step 5: Testleri çalıştır, geçtiklerini gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter Codex`
Expected: PASS — 9 test (Task 1'in 4 testi ayrica gecmeye devam eder).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(codex): app-server and rollout rate limit parsers"
```

---

### Task 4: Codex collector — ikili bulucu ve app-server oturumu

**Files:**
- Create: `src/AgentQuotaTray.Core/Codex/CodexBinaryLocator.cs`
- Create: `src/AgentQuotaTray.Core/Process/IJsonRpcProcess.cs`
- Create: `src/AgentQuotaTray.Core/Process/StdioJsonRpcProcess.cs`
- Create: `src/AgentQuotaTray.Core/Codex/CodexCollector.cs`
- Test: `tests/AgentQuotaTray.Tests/Codex/CodexBinaryLocatorTests.cs`
- Test: `tests/AgentQuotaTray.Tests/Codex/CodexCollectorTests.cs`

**Interfaces:**
- Consumes: `CodexRateLimitsParser.ParseAppServer`, `CodexRolloutReader.ReadLatest` (Task 3); `IQuotaCollector` (Task 2)
- Produces: `IJsonRpcProcess { Task StartAsync(CancellationToken ct); Task SendAsync(string jsonLine, CancellationToken ct); IAsyncEnumerable<string> ReadLines(CancellationToken ct); }`; `static string? CodexBinaryLocator.Locate(IEnumerable<string> candidatePaths, Func<string,bool> fileExists)`; `CodexCollector(Func<IJsonRpcProcess> processFactory, Func<DateTimeOffset> clock, Func<TimeSpan,CancellationToken,Task> delay, Func<QuotaSnapshot?> readFallback)`

- [ ] **Step 1: İkili bulucu testini yaz**

`tests/AgentQuotaTray.Tests/Codex/CodexBinaryLocatorTests.cs`:

```csharp
using AgentQuotaTray.Core.Codex;
using Xunit;

namespace AgentQuotaTray.Tests.Codex;

public class CodexBinaryLocatorTests
{
    [Fact]
    public void Locate_ReturnsFirstExistingCandidate()
    {
        var candidates = new[] { @"C:\yok\codex.exe", @"C:\var\codex.exe" };

        var found = CodexBinaryLocator.Locate(
            candidates, p => p == @"C:\var\codex.exe");

        Assert.Equal(@"C:\var\codex.exe", found);
    }

    [Fact]
    public void Locate_NoneExist_ReturnsNull() =>
        Assert.Null(CodexBinaryLocator.Locate(new[] { @"C:\yok\codex.exe" }, _ => false));

    [Fact]
    public void DefaultCandidates_IncludeNpmVendorPath()
    {
        var candidates = CodexBinaryLocator.DefaultCandidates().ToList();

        Assert.Contains(candidates, c =>
            c.Contains("codex-win32-x64", StringComparison.OrdinalIgnoreCase) &&
            c.EndsWith("codex.exe", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Collector testlerini yaz**

`tests/AgentQuotaTray.Tests/Codex/CodexCollectorTests.cs`:

```csharp
using System.Text.Json;
using AgentQuotaTray.Core.Codex;
using AgentQuotaTray.Core.Model;
using AgentQuotaTray.Core.Process;
using Xunit;

namespace AgentQuotaTray.Tests.Codex;

public class CodexCollectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeProcess : IJsonRpcProcess
    {
        private readonly string[] _lines;
        private readonly bool _failStart;
        public List<string> Sent { get; } = new();

        public FakeProcess(string[] lines, bool failStart = false)
        {
            _lines = lines;
            _failStart = failStart;
        }

        public Task StartAsync(CancellationToken ct) =>
            _failStart
                ? Task.FromException(new InvalidOperationException("baslatilamadi"))
                : Task.CompletedTask;

        public Task SendAsync(string jsonLine, CancellationToken ct)
        {
            Sent.Add(jsonLine);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ReadLines(CancellationToken ct)
        {
            foreach (var line in _lines)
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }

        public void Dispose() { }
    }

    private const string InitLine = """{"id":1,"result":{"userAgent":"x"}}""";
    private const string ReadLine = """
    {"id":2,"result":{"rateLimits":{"primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1788478826},"secondary":{"usedPercent":36,"windowDurationMins":10080,"resetsAt":1788817184}}}}
    """;
    private const string UpdatedLine = """
    {"method":"account/rateLimits/updated","params":{"rateLimits":{"primary":{"usedPercent":8,"windowDurationMins":300,"resetsAt":1788478826}}}}
    """;

    // Watch sonsuz akistir; sayaç dolunca iptal edilir ve iptal istisnasi
    // beklenen sonlanma bicimidir, hata degildir.
    private static async Task<List<QuotaSnapshot>> Take(
        CodexCollector collector, int count)
    {
        var result = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource();
        try
        {
            await foreach (var snap in collector.Watch(cts.Token))
            {
                result.Add(snap);
                if (result.Count >= count) { await cts.CancelAsync(); break; }
            }
        }
        catch (OperationCanceledException) { }
        return result;
    }

    [Fact]
    public async Task Watch_ReadResponse_YieldsFreshSnapshot()
    {
        var process = new FakeProcess(new[] { InitLine, ReadLine });
        var collector = new CodexCollector(
            () => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.Fresh, snaps[0].Health);
        Assert.Equal(0.0, snaps[0].Session!.Percent);
        Assert.Equal(36.0, snaps[0].Weekly!.Percent);
    }

    [Fact]
    public async Task Watch_SendsInitializeThenRead()
    {
        var process = new FakeProcess(new[] { InitLine, ReadLine });
        var collector = new CodexCollector(
            () => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        await Take(collector, 1);

        Assert.Contains(process.Sent, s => s.Contains("\"initialize\""));
        Assert.Contains(process.Sent, s => s.Contains("account/rateLimits/read"));
        var initIndex = process.Sent.FindIndex(s => s.Contains("\"initialize\""));
        var readIndex = process.Sent.FindIndex(s => s.Contains("rateLimits/read"));
        Assert.True(initIndex < readIndex);
    }

    [Fact]
    public async Task Watch_UpdatedNotification_YieldsNewSnapshot()
    {
        var process = new FakeProcess(new[] { InitLine, ReadLine, UpdatedLine });
        var collector = new CodexCollector(
            () => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 2);

        Assert.Equal(0.0, snaps[0].Session!.Percent);
        Assert.Equal(8.0, snaps[1].Session!.Percent);
    }

    [Fact]
    public async Task Watch_UnrelatedLines_AreIgnored()
    {
        var noise = """{"method":"remoteControl/status/changed","params":{"status":"disabled"}}""";
        var process = new FakeProcess(new[] { InitLine, noise, ReadLine });
        var collector = new CodexCollector(
            () => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.Fresh, snaps[0].Health);
        Assert.Equal(0.0, snaps[0].Session!.Percent);
    }

    [Fact]
    public async Task Watch_StartFailsThreeTimes_FallsBackToRolloutFile()
    {
        var fallback = new QuotaSnapshot(
            "codex", new QuotaWindow(11.0, null, TimeSpan.FromHours(5)), null,
            HealthState.Stale, Now, "dosyadan");
        var attempts = 0;
        var collector = new CodexCollector(
            () => { attempts++; return new FakeProcess(Array.Empty<string>(), failStart: true); },
            () => Now, (_, _) => Task.CompletedTask, () => fallback);

        var snaps = await Take(collector, 1);

        Assert.Equal(3, attempts);
        Assert.Equal(HealthState.Stale, snaps[0].Health);
        Assert.Equal(11.0, snaps[0].Session!.Percent);
    }

    [Fact]
    public async Task Watch_StartFailsAndNoFallback_YieldsProtocolBroken()
    {
        var collector = new CodexCollector(
            () => new FakeProcess(Array.Empty<string>(), failStart: true),
            () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.ProtocolBroken, snaps[0].Health);
        Assert.Null(snaps[0].Session);
    }

    [Fact]
    public async Task Watch_RestartBackoffIsCappedAtFiveMinutes()
    {
        var delays = new List<TimeSpan>();
        var collector = new CodexCollector(
            () => new FakeProcess(Array.Empty<string>(), failStart: true),
            () => Now, (d, _) => { delays.Add(d); return Task.CompletedTask; }, () => null);

        await Take(collector, 1);

        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromMinutes(5)));
    }
}
```

- [ ] **Step 3: Testleri çalıştır, başarısız olduklarını gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter Codex`
Expected: FAIL — `CodexBinaryLocator`, `IJsonRpcProcess`, `CodexCollector` bulunamıyor.

- [ ] **Step 4: İkili bulucuyu yaz**

`src/AgentQuotaTray.Core/Codex/CodexBinaryLocator.cs`:

```csharp
namespace AgentQuotaTray.Core.Codex;

/// <summary>
/// Windows'ta PATH'teki `codex` bir npm shim'idir ve dogrudan surec olarak
/// baslatilamaz (WinError 2). Gercek ikili npm vendor dizinindedir.
/// </summary>
public static class CodexBinaryLocator
{
    public static string? Locate(
        IEnumerable<string> candidatePaths, Func<string, bool> fileExists) =>
        candidatePaths.FirstOrDefault(fileExists);

    public static string? LocateDefault() => Locate(DefaultCandidates(), File.Exists);

    public static IEnumerable<string> DefaultCandidates()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmRoot = Path.Combine(appData, "npm", "node_modules", "@openai", "codex");

        yield return Path.Combine(npmRoot, "node_modules", "@openai",
            "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Programs", "codex", "codex.exe");
    }
}
```

- [ ] **Step 5: Süreç arayüzünü ve gerçek uygulamasını yaz**

`src/AgentQuotaTray.Core/Process/IJsonRpcProcess.cs`:

```csharp
namespace AgentQuotaTray.Core.Process;

public interface IJsonRpcProcess : IDisposable
{
    Task StartAsync(CancellationToken ct);
    Task SendAsync(string jsonLine, CancellationToken ct);
    IAsyncEnumerable<string> ReadLines(CancellationToken ct);
}
```

`src/AgentQuotaTray.Core/Process/StdioJsonRpcProcess.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentQuotaTray.Core.Process;

public sealed class StdioJsonRpcProcess : IJsonRpcProcess
{
    private readonly string _fileName;
    private readonly string _arguments;
    private System.Diagnostics.Process? _process;

    public StdioJsonRpcProcess(string fileName, string arguments)
    {
        _fileName = fileName;
        _arguments = arguments;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var info = new ProcessStartInfo(_fileName, _arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };

        _process = System.Diagnostics.Process.Start(info)
            ?? throw new InvalidOperationException("codex app-server baslatilamadi");

        return Task.CompletedTask;
    }

    public async Task SendAsync(string jsonLine, CancellationToken ct)
    {
        var process = _process
            ?? throw new InvalidOperationException("Surec baslatilmadi");
        await process.StandardInput.WriteLineAsync(jsonLine.AsMemory(), ct)
            .ConfigureAwait(false);
        await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ReadLines(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var process = _process
            ?? throw new InvalidOperationException("Surec baslatilmadi");

        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;
            yield return line;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        _process?.Dispose();
    }
}
```

- [ ] **Step 6: Codex collector'ı yaz**

`src/AgentQuotaTray.Core/Codex/CodexCollector.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AgentQuotaTray.Core.Collectors;
using AgentQuotaTray.Core.Model;
using AgentQuotaTray.Core.Process;

namespace AgentQuotaTray.Core.Codex;

public sealed class CodexCollector : IQuotaCollector
{
    private const int MaxStartAttempts = 3;
    private static readonly TimeSpan FirstRestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(5);

    private readonly Func<IJsonRpcProcess> _processFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<QuotaSnapshot?> _readFallback;

    public CodexCollector(
        Func<IJsonRpcProcess> processFactory,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<QuotaSnapshot?> readFallback)
    {
        _processFactory = processFactory;
        _clock = clock;
        _delay = delay;
        _readFallback = readFallback;
    }

    public string Provider => CodexRateLimitsParser.Provider;

    /// <summary>
    /// Oturum kasten uzun yasar: app-server bagli kaldigi surece
    /// account/rateLimits/updated bildirimleri akar. Bu yuzden snapshot'lar
    /// oturum bitiminde toplu degil, geldikleri anda yayilir — arada bir
    /// Channel vardir, cunku try/catch icinden yield return yapilamaz.
    /// </summary>
    public async IAsyncEnumerable<QuotaSnapshot> Watch(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<QuotaSnapshot>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        var pump = Task.Run(() => RunLoop(channel.Writer, ct), CancellationToken.None);

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var snapshot))
                    yield return snapshot;
            }
        }
        finally
        {
            await pump.ConfigureAwait(false);
        }
    }

    private async Task RunLoop(ChannelWriter<QuotaSnapshot> writer, CancellationToken ct)
    {
        var failures = 0;
        var restartDelay = FirstRestartDelay;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var started = await RunSession(writer, ct).ConfigureAwait(false);

                if (started)
                {
                    failures = 0;
                    restartDelay = FirstRestartDelay;
                }
                else
                {
                    failures++;
                    if (failures >= MaxStartAttempts)
                    {
                        var fallback = _readFallback()
                            ?? QuotaSnapshot.Unhealthy(
                                Provider, HealthState.ProtocolBroken, _clock(),
                                "codex app-server baslatilamadi");
                        await writer.WriteAsync(fallback, ct).ConfigureAwait(false);
                        failures = 0;
                    }
                }

                await _delay(restartDelay, ct).ConfigureAwait(false);
                restartDelay = restartDelay + restartDelay > MaxRestartDelay
                    ? MaxRestartDelay
                    : restartDelay + restartDelay;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>Surec basladiysa true doner; snapshot'lari writer'a yazar.</summary>
    private async Task<bool> RunSession(
        ChannelWriter<QuotaSnapshot> writer, CancellationToken ct)
    {
        using var process = _processFactory();

        try
        {
            await process.StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return false;
        }

        try
        {
            await process.SendAsync(InitializeMessage, ct).ConfigureAwait(false);

            var initialized = false;
            await foreach (var line in process.ReadLines(ct).ConfigureAwait(false))
            {
                if (!initialized && IsInitializeResponse(line))
                {
                    initialized = true;
                    await process.SendAsync(InitializedNotification, ct).ConfigureAwait(false);
                    await process.SendAsync(ReadMessage, ct).ConfigureAwait(false);
                    continue;
                }

                if (!CarriesRateLimits(line)) continue;

                await writer.WriteAsync(
                    CodexRateLimitsParser.ParseAppServer(line, _clock()), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* surec olduse yeniden baslatilir */ }

        return true;
    }

    private const string InitializeMessage = """
    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"agent-quota-tray","title":"Agent Quota Tray","version":"0.1.0"}}}
    """;

    private const string InitializedNotification = """
    {"jsonrpc":"2.0","method":"initialized","params":{}}
    """;

    private const string ReadMessage = """
    {"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{}}
    """;

    private static bool IsInitializeResponse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1;
        }
        catch (JsonException) { return false; }
    }

    private static bool CarriesRateLimits(string line) =>
        line.Contains("\"rateLimits\"", StringComparison.Ordinal);
}
```

`InitializeMessage` içindeki tek satır biçimi korunmalıdır — JSON-RPC stdio protokolü satır başına bir mesaj bekler.

- [ ] **Step 7: Testleri çalıştır, geçtiklerini gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter Codex`
Expected: PASS — 19 test (Task 3'ün 9'u + bu görevin 10'u).

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(codex): app-server collector with restart and file fallback"
```

---

### Task 5: QuotaStore — tazelik yönetimi ve olay yayını

**Files:**
- Create: `src/AgentQuotaTray.Core/Store/QuotaStore.cs`
- Test: `tests/AgentQuotaTray.Tests/Store/QuotaStoreTests.cs`

**Interfaces:**
- Consumes: `QuotaSnapshot`, `HealthState` (Task 1); `IQuotaCollector` (Task 2)
- Produces: `QuotaStore(Func<DateTimeOffset> clock)` with `void Apply(QuotaSnapshot snapshot)`, `QuotaSnapshot? Get(string provider)`, `IReadOnlyList<QuotaSnapshot> All()`, `event Action<QuotaSnapshot>? Changed`, `void RefreshStaleness()`

- [ ] **Step 1: Testleri yaz (başarısız olacak)**

`tests/AgentQuotaTray.Tests/Store/QuotaStoreTests.cs`:

```csharp
using AgentQuotaTray.Core.Model;
using AgentQuotaTray.Core.Store;
using Xunit;

namespace AgentQuotaTray.Tests.Store;

public class QuotaStoreTests
{
    private DateTimeOffset _now = new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private QuotaStore Build() => new(() => _now);

    private QuotaSnapshot Fresh(string provider, double percent) =>
        new(provider, new QuotaWindow(percent, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, _now, null);

    [Fact]
    public void Apply_StoresAndReturnsSnapshot()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        Assert.Equal(14.0, store.Get("claude")!.Session!.Percent);
        Assert.Null(store.Get("codex"));
    }

    [Fact]
    public void Apply_RaisesChangedEvent()
    {
        var store = Build();
        var seen = new List<string>();
        store.Changed += s => seen.Add(s.Provider);

        store.Apply(Fresh("claude", 14.0));

        Assert.Equal(new[] { "claude" }, seen);
    }

    [Fact]
    public void RefreshStaleness_AfterTwoMinutes_MarksStale()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        _now = _now.AddMinutes(3);
        store.RefreshStaleness();

        Assert.Equal(HealthState.Stale, store.Get("claude")!.Health);
        Assert.Equal(14.0, store.Get("claude")!.Session!.Percent);
    }

    [Fact]
    public void RefreshStaleness_WithinTwoMinutes_StaysFresh()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        _now = _now.AddSeconds(90);
        store.RefreshStaleness();

        Assert.Equal(HealthState.Fresh, store.Get("claude")!.Health);
    }

    [Fact]
    public void RefreshStaleness_DoesNotDowngradeErrorStates()
    {
        var store = Build();
        store.Apply(QuotaSnapshot.Unhealthy(
            "claude", HealthState.RateLimited, _now, "sinirli"));

        _now = _now.AddMinutes(10);
        store.RefreshStaleness();

        Assert.Equal(HealthState.RateLimited, store.Get("claude")!.Health);
    }

    [Fact]
    public void RefreshStaleness_RaisesChangedOnlyOnTransition()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));
        var count = 0;
        store.Changed += _ => count++;

        _now = _now.AddMinutes(3);
        store.RefreshStaleness();
        store.RefreshStaleness();

        Assert.Equal(1, count);
    }

    [Fact]
    public void All_ReturnsSnapshotsInStableProviderOrder()
    {
        var store = Build();
        store.Apply(Fresh("codex", 36.0));
        store.Apply(Fresh("claude", 14.0));

        Assert.Equal(new[] { "claude", "codex" }, store.All().Select(s => s.Provider));
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduklarını gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter QuotaStoreTests`
Expected: FAIL — `QuotaStore` bulunamıyor.

- [ ] **Step 3: QuotaStore'u yaz**

`src/AgentQuotaTray.Core/Store/QuotaStore.cs`:

```csharp
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Store;

/// <summary>
/// Son snapshot'lari tutar ve yasa gore Fresh -> Stale gecisini yonetir.
/// Hata durumlari (RateLimited, AuthMissing, ProtocolBroken) yas gectikce
/// Stale'e cevrilmez; hata bilgisi daha spesifiktir ve korunur.
/// </summary>
public sealed class QuotaStore
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, QuotaSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    public QuotaStore(Func<DateTimeOffset> clock) => _clock = clock;

    public event Action<QuotaSnapshot>? Changed;

    public void Apply(QuotaSnapshot snapshot)
    {
        lock (_gate) _snapshots[snapshot.Provider] = snapshot;
        Changed?.Invoke(snapshot);
    }

    public QuotaSnapshot? Get(string provider)
    {
        lock (_gate) return _snapshots.GetValueOrDefault(provider);
    }

    public IReadOnlyList<QuotaSnapshot> All()
    {
        lock (_gate)
            return _snapshots.Values.OrderBy(s => s.Provider, StringComparer.Ordinal).ToList();
    }

    public void RefreshStaleness()
    {
        var now = _clock();
        var transitioned = new List<QuotaSnapshot>();

        lock (_gate)
        {
            foreach (var (provider, snapshot) in _snapshots.ToList())
            {
                if (snapshot.Health != HealthState.Fresh) continue;
                if (now - snapshot.FetchedAt < StaleAfter) continue;

                var stale = snapshot with { Health = HealthState.Stale };
                _snapshots[provider] = stale;
                transitioned.Add(stale);
            }
        }

        foreach (var snapshot in transitioned) Changed?.Invoke(snapshot);
    }
}
```

- [ ] **Step 4: Testleri çalıştır, geçtiklerini gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter QuotaStoreTests`
Expected: PASS — 7 test.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(store): quota store with staleness transitions"
```

---

### Task 6: Sunum mantığı — biçimlendirme ve renk eşikleri

**Files:**
- Create: `src/AgentQuotaTray.Core/Presentation/QuotaSeverity.cs`
- Create: `src/AgentQuotaTray.Core/Presentation/QuotaFormatter.cs`
- Test: `tests/AgentQuotaTray.Tests/Presentation/QuotaFormatterTests.cs`

**Interfaces:**
- Consumes: `QuotaSnapshot`, `QuotaWindow`, `HealthState` (Task 1)
- Produces: `enum QuotaSeverity { Normal, Caution, Warning }`; `static QuotaSeverity QuotaFormatter.SeverityFor(double percent)`; `static string QuotaFormatter.Percent(double)`; `static string QuotaFormatter.ResetsIn(DateTimeOffset? resetsAt, DateTimeOffset now)`; `static string QuotaFormatter.Age(DateTimeOffset fetchedAt, DateTimeOffset now)`; `static string QuotaFormatter.HealthText(QuotaSnapshot)`; `static string QuotaFormatter.Tooltip(IReadOnlyList<QuotaSnapshot>, DateTimeOffset now)`; `static double? QuotaFormatter.HighestPercent(IReadOnlyList<QuotaSnapshot>)`; `static bool QuotaFormatter.HasUnhealthy(IReadOnlyList<QuotaSnapshot>)`

- [ ] **Step 1: Testleri yaz (başarısız olacak)**

`tests/AgentQuotaTray.Tests/Presentation/QuotaFormatterTests.cs`:

```csharp
using AgentQuotaTray.Core.Model;
using AgentQuotaTray.Core.Presentation;
using Xunit;

namespace AgentQuotaTray.Tests.Presentation;

public class QuotaFormatterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.0, QuotaSeverity.Normal)]
    [InlineData(59.9, QuotaSeverity.Normal)]
    [InlineData(60.0, QuotaSeverity.Caution)]
    [InlineData(85.0, QuotaSeverity.Caution)]
    [InlineData(85.1, QuotaSeverity.Warning)]
    [InlineData(100.0, QuotaSeverity.Warning)]
    public void SeverityFor_UsesFixedThresholds(double percent, QuotaSeverity expected) =>
        Assert.Equal(expected, QuotaFormatter.SeverityFor(percent));

    [Theory]
    [InlineData(14.0, "%14")]
    [InlineData(0.0, "%0")]
    [InlineData(36.4, "%36")]
    [InlineData(99.5, "%100")]
    public void Percent_RoundsToWholeNumber(double value, string expected) =>
        Assert.Equal(expected, QuotaFormatter.Percent(value));

    [Fact]
    public void ResetsIn_HoursAndMinutes() =>
        Assert.Equal("4s 38d sonra sifirlanir",
            QuotaFormatter.ResetsIn(Now.AddMinutes(278), Now));

    [Fact]
    public void ResetsIn_DaysAndHours() =>
        Assert.Equal("1g 14s sonra sifirlanir",
            QuotaFormatter.ResetsIn(Now.AddHours(38), Now));

    [Fact]
    public void ResetsIn_UnderOneMinute() =>
        Assert.Equal("birazdan sifirlanir",
            QuotaFormatter.ResetsIn(Now.AddSeconds(30), Now));

    [Fact]
    public void ResetsIn_PastTime_SaysResetting() =>
        Assert.Equal("sifirlaniyor", QuotaFormatter.ResetsIn(Now.AddMinutes(-5), Now));

    [Fact]
    public void ResetsIn_Null_SaysUnknown() =>
        Assert.Equal("sifirlanma zamani bilinmiyor", QuotaFormatter.ResetsIn(null, Now));

    [Fact]
    public void Age_JustNow() =>
        Assert.Equal("simdi guncellendi", QuotaFormatter.Age(Now.AddSeconds(-10), Now));

    [Fact]
    public void Age_Minutes() =>
        Assert.Equal("3 dk once", QuotaFormatter.Age(Now.AddMinutes(-3), Now));

    [Theory]
    [InlineData(HealthState.RateLimited, "Gecici olarak sinirli")]
    [InlineData(HealthState.AuthMissing, "Giris gerekli")]
    [InlineData(HealthState.ProtocolBroken, "API degismis")]
    public void HealthText_DescribesFailure(HealthState health, string expected)
    {
        var snapshot = QuotaSnapshot.Unhealthy("claude", health, Now, "detay");
        Assert.Equal(expected, QuotaFormatter.HealthText(snapshot));
    }

    [Fact]
    public void Tooltip_ShowsBothProviders()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude",
                new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                new QuotaWindow(12, null, TimeSpan.FromDays(7)),
                HealthState.Fresh, Now, null),
            new QuotaSnapshot("codex",
                new QuotaWindow(0, null, TimeSpan.FromHours(5)),
                new QuotaWindow(36, null, TimeSpan.FromDays(7)),
                HealthState.Fresh, Now, null),
        };

        Assert.Equal("CC %14 / %12  ·  CX %0 / %36",
            QuotaFormatter.Tooltip(snapshots, Now));
    }

    [Fact]
    public void Tooltip_UnhealthyProviderShowsReasonNotZero()
    {
        var snapshots = new[]
        {
            QuotaSnapshot.Unhealthy("claude", HealthState.AuthMissing, Now, "detay"),
        };

        var tooltip = QuotaFormatter.Tooltip(snapshots, Now);

        Assert.Contains("Giris gerekli", tooltip);
        Assert.DoesNotContain("%0", tooltip);
    }

    [Fact]
    public void HighestPercent_IgnoresUnhealthySnapshots()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude", new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                null, HealthState.Fresh, Now, null),
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "detay"),
        };

        Assert.Equal(14.0, QuotaFormatter.HighestPercent(snapshots));
    }

    [Fact]
    public void HighestPercent_AllUnhealthy_ReturnsNull()
    {
        var snapshots = new[]
        {
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "detay"),
        };

        Assert.Null(QuotaFormatter.HighestPercent(snapshots));
    }

    [Fact]
    public void HasUnhealthy_OneBrokenProviderAmongHealthy_IsTrue()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude", new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                null, HealthState.Fresh, Now, null),
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "detay"),
        };

        Assert.True(QuotaFormatter.HasUnhealthy(snapshots));
    }

    [Fact]
    public void HasUnhealthy_StaleIsNotUnhealthy()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude", new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                null, HealthState.Stale, Now, null),
        };

        Assert.False(QuotaFormatter.HasUnhealthy(snapshots));
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduklarını gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter QuotaFormatterTests`
Expected: FAIL — `QuotaFormatter` bulunamıyor.

- [ ] **Step 3: Sunum tiplerini yaz**

`src/AgentQuotaTray.Core/Presentation/QuotaSeverity.cs`:

```csharp
namespace AgentQuotaTray.Core.Presentation;

public enum QuotaSeverity
{
    Normal,
    Caution,
    Warning
}
```

`src/AgentQuotaTray.Core/Presentation/QuotaFormatter.cs`:

```csharp
using System.Globalization;
using System.Text;
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Presentation;

public static class QuotaFormatter
{
    public const double CautionThreshold = 60.0;
    public const double WarningThreshold = 85.0;

    public static QuotaSeverity SeverityFor(double percent) => percent switch
    {
        > WarningThreshold => QuotaSeverity.Warning,
        >= CautionThreshold => QuotaSeverity.Caution,
        _ => QuotaSeverity.Normal,
    };

    public static string Percent(double value) =>
        "%" + Math.Round(value, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

    public static string ResetsIn(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "sifirlanma zamani bilinmiyor";

        var remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero) return "sifirlaniyor";
        if (remaining < TimeSpan.FromMinutes(1)) return "birazdan sifirlanir";

        if (remaining >= TimeSpan.FromDays(1))
            return $"{remaining.Days}g {remaining.Hours}s sonra sifirlanir";

        if (remaining >= TimeSpan.FromHours(1))
            return $"{remaining.Hours}s {remaining.Minutes}d sonra sifirlanir";

        return $"{remaining.Minutes}d sonra sifirlanir";
    }

    public static string Age(DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        var age = now - fetchedAt;
        if (age < TimeSpan.FromMinutes(1)) return "simdi guncellendi";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} dk once";
        return $"{(int)age.TotalHours} sa once";
    }

    public static string HealthText(QuotaSnapshot snapshot) => snapshot.Health switch
    {
        HealthState.RateLimited => "Gecici olarak sinirli",
        HealthState.AuthMissing => "Giris gerekli",
        HealthState.ProtocolBroken => "API degismis",
        HealthState.Stale => "Veri eski",
        _ => "",
    };

    public static string ShortName(string provider) => provider switch
    {
        "claude" => "CC",
        "codex" => "CX",
        _ => provider.ToUpperInvariant(),
    };

    public static string Tooltip(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        var parts = new List<string>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            var name = ShortName(snapshot.Provider);

            if (snapshot.Session is null && snapshot.Weekly is null)
            {
                parts.Add($"{name} {HealthText(snapshot)}");
                continue;
            }

            var session = snapshot.Session is null ? "?" : Percent(snapshot.Session.Percent);
            var weekly = snapshot.Weekly is null ? "?" : Percent(snapshot.Weekly.Percent);
            parts.Add($"{name} {session} / {weekly}");
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Herhangi bir saglayicinin verisi alinamiyorsa true. Stale sayilmaz:
    /// eski veri hala gercek veridir, alinamayan veri degildir.
    /// </summary>
    public static bool HasUnhealthy(IReadOnlyList<QuotaSnapshot> snapshots) =>
        snapshots.Any(s => s.Health is HealthState.RateLimited
            or HealthState.AuthMissing or HealthState.ProtocolBroken);

    /// <summary>
    /// Simgede gosterilecek deger: saglikli snapshot'lardaki en yuksek yuzde.
    /// Saglıksiz snapshot hic sayilmaz — hata sifir gibi gorunemez.
    /// </summary>
    public static double? HighestPercent(IReadOnlyList<QuotaSnapshot> snapshots)
    {
        double? highest = null;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Health is HealthState.RateLimited or HealthState.AuthMissing
                or HealthState.ProtocolBroken)
                continue;

            foreach (var window in new[] { snapshot.Session, snapshot.Weekly })
            {
                if (window is null) continue;
                if (highest is null || window.Percent > highest) highest = window.Percent;
            }
        }

        return highest;
    }
}
```

- [ ] **Step 4: Testleri çalıştır, geçtiklerini gör**

Run: `dotnet test tests/AgentQuotaTray.Tests --filter QuotaFormatterTests`
Expected: PASS — 26 test (Theory satırları dahil).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(presentation): quota formatting and severity thresholds"
```

---

### Task 7: WPF uygulaması — tray simgesi ve popup

**Files:**
- Create: `src/AgentQuotaTray.App/AgentQuotaTray.App.csproj`
- Create: `src/AgentQuotaTray.App/App.xaml`
- Create: `src/AgentQuotaTray.App/App.xaml.cs`
- Create: `src/AgentQuotaTray.App/TrayIconRenderer.cs`
- Create: `src/AgentQuotaTray.App/QuotaPopup.xaml`
- Create: `src/AgentQuotaTray.App/QuotaPopup.xaml.cs`
- Modify: `AgentQuotaTray.sln`

**Interfaces:**
- Consumes: `QuotaStore` (Task 5); `QuotaFormatter`, `QuotaSeverity` (Task 6); `ClaudeCollector` (Task 2); `CodexCollector`, `CodexBinaryLocator`, `CodexRolloutReader` (Tasks 3-4)
- Produces: çalıştırılabilir uygulama; başka görev buna bağlanmaz

Bu görev UI'dir; mantık Core'da test edilmiştir, burada birim testi yoktur. Doğrulama Task 8'de gerçek çalıştırmayla yapılır.

- [ ] **Step 1: WPF projesini oluştur**

```bash
cd C:/Users/ozncd/Documents/Isler/agent-quota-tray
dotnet new wpf -o src/AgentQuotaTray.App -f net9.0
dotnet sln add src/AgentQuotaTray.App
dotnet add src/AgentQuotaTray.App reference src/AgentQuotaTray.Core
rm src/AgentQuotaTray.App/MainWindow.xaml src/AgentQuotaTray.App/MainWindow.xaml.cs
```

`src/AgentQuotaTray.App/AgentQuotaTray.App.csproj` içindeki `<PropertyGroup>`:

```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net9.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<ApplicationIcon></ApplicationIcon>
```

- [ ] **Step 2: Simge çizicisini yaz**

`src/AgentQuotaTray.App/TrayIconRenderer.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using AgentQuotaTray.Core.Presentation;

namespace AgentQuotaTray.App;

/// <summary>En yuksek yuzdeyi halka olarak cizer. Deger yoksa soru isareti cizer.</summary>
public static class TrayIconRenderer
{
    private const int Size = 32;

    public static Icon Render(double? percent, bool hasUnhealthy = false)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var rect = new Rectangle(3, 3, Size - 7, Size - 7);

            // Bir saglayicinin verisi hic alinamiyorsa bunu halkanin kendisi soyler;
            // yoksa saglikli olan digerinin yuzdesi sorunu gizlerdi.
            using var track = new Pen(
                hasUnhealthy
                    ? Color.FromArgb(150, 235, 87, 87)
                    : Color.FromArgb(70, 255, 255, 255),
                4f);
            g.DrawEllipse(track, rect);

            if (percent is null)
            {
                using var font = new Font("Segoe UI", 14f, FontStyle.Bold,
                    GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(230, 200, 200, 200));
                g.DrawString("?", font, brush, new PointF(11f, 8f));
            }
            else
            {
                using var arc = new Pen(ColorFor(QuotaFormatter.SeverityFor(percent.Value)), 4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                var sweep = (float)(Math.Clamp(percent.Value, 0, 100) / 100.0 * 360.0);
                if (sweep > 0) g.DrawArc(arc, rect, -90f, sweep);
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static Color ColorFor(QuotaSeverity severity) => severity switch
    {
        QuotaSeverity.Warning => Color.FromArgb(255, 235, 87, 87),
        QuotaSeverity.Caution => Color.FromArgb(255, 240, 180, 41),
        _ => Color.FromArgb(255, 80, 200, 120),
    };
}
```

- [ ] **Step 3: Popup penceresini yaz**

`src/AgentQuotaTray.App/QuotaPopup.xaml`:

```xml
<Window x:Class="AgentQuotaTray.App.QuotaPopup"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Agent Quota" Width="320" SizeToContent="Height"
        WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False"
        Topmost="True" Background="#1E1E1E" BorderBrush="#333" BorderThickness="1"
        Deactivated="OnDeactivated">
    <StackPanel Margin="16">
        <StackPanel x:Name="ProvidersPanel" />
        <TextBlock x:Name="FooterText" Foreground="#777" FontSize="11" Margin="0,12,0,0" />
    </StackPanel>
</Window>
```

`src/AgentQuotaTray.App/QuotaPopup.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentQuotaTray.Core.Model;
using AgentQuotaTray.Core.Presentation;

namespace AgentQuotaTray.App;

public partial class QuotaPopup : Window
{
    public QuotaPopup() => InitializeComponent();

    public void Show(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        ProvidersPanel.Children.Clear();

        foreach (var snapshot in snapshots)
            ProvidersPanel.Children.Add(BuildProviderBlock(snapshot, now));

        FooterText.Text = snapshots.Count == 0
            ? "Henuz veri yok"
            : QuotaFormatter.Age(snapshots.Max(s => s.FetchedAt), now);

        PositionNearTray();
        Show();
        Activate();
    }

    private static UIElement BuildProviderBlock(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        panel.Children.Add(new TextBlock
        {
            Text = TitleFor(snapshot.Provider),
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (snapshot.Session is null && snapshot.Weekly is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = QuotaFormatter.HealthText(snapshot),
                Foreground = new SolidColorBrush(Color.FromRgb(235, 87, 87)),
                FontSize = 12,
            });
            return panel;
        }

        AddWindowRow(panel, "Session", "5 saatlik pencere", snapshot.Session, snapshot, now);
        AddWindowRow(panel, "Weekly", "7 gunluk pencere", snapshot.Weekly, snapshot, now);
        return panel;
    }

    private static void AddWindowRow(
        Panel parent, string title, string subtitle, QuotaWindow? window,
        QuotaSnapshot snapshot, DateTimeOffset now)
    {
        if (window is null) return;

        var stale = snapshot.Health == HealthState.Stale;
        var color = TrayIconRenderer.ColorFor(QuotaFormatter.SeverityFor(window.Percent));
        var brush = new SolidColorBrush(Color.FromArgb(
            stale ? (byte)120 : (byte)255, color.R, color.G, color.B));

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var percent = new TextBlock
        {
            Text = QuotaFormatter.Percent(window.Percent),
            Foreground = brush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };
        DockPanel.SetDock(percent, Dock.Right);
        header.Children.Add(percent);
        header.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = stale ? Brushes.Gray : Brushes.White,
            FontSize = 13,
        });

        parent.Children.Add(header);
        parent.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var track = new Border
        {
            Height = 6,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var fill = new Border
        {
            Height = 6,
            Background = brush,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(100, window.Percent)) / 100.0 * 288,
        };
        track.Child = fill;
        parent.Children.Add(track);

        parent.Children.Add(new TextBlock
        {
            Text = QuotaFormatter.ResetsIn(window.ResetsAt, now)
                   + (stale ? " · " + QuotaFormatter.HealthText(snapshot) : ""),
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10),
        });
    }

    private static string TitleFor(string provider) => provider switch
    {
        "claude" => "Claude Usage",
        "codex" => "Codex Usage",
        _ => provider,
    };

    private void PositionNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();
}
```

- [ ] **Step 4: Uygulama giriş noktasını (composition root) yaz**

`src/AgentQuotaTray.App/App.xaml`:

```xml
<Application x:Class="AgentQuotaTray.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Startup="OnStartup" ShutdownMode="OnExplicitShutdown" />
```

`src/AgentQuotaTray.App/App.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Forms;
using AgentQuotaTray.Core.Claude;
using AgentQuotaTray.Core.Codex;
using AgentQuotaTray.Core.Collectors;
using AgentQuotaTray.Core.Http;
using AgentQuotaTray.Core.Presentation;
using AgentQuotaTray.Core.Process;
using AgentQuotaTray.Core.Store;

namespace AgentQuotaTray.App;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _cts = new();
    private readonly QuotaStore _store = new(() => DateTimeOffset.Now);
    private NotifyIcon? _trayIcon;
    private QuotaPopup? _popup;
    private SystemHttpTransport? _transport;
    private System.Windows.Threading.DispatcherTimer? _stalenessTimer;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _popup = new QuotaPopup();

        _trayIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.Render(null, hasUnhealthy: false),
            Visible = true,
            Text = "Agent Quota Tray",
        };
        _trayIcon.Click += (_, _) => TogglePopup();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Cikis", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        _store.Changed += _ => Dispatcher.Invoke(UpdateTray);

        _stalenessTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _stalenessTimer.Tick += (_, _) => _store.RefreshStaleness();
        _stalenessTimer.Start();

        _transport = new SystemHttpTransport();
        StartCollector(BuildClaudeCollector(_transport));
        StartCollector(BuildCodexCollector());
    }

    private static IQuotaCollector BuildClaudeCollector(IHttpTransport transport) =>
        new ClaudeCollector(
            transport,
            ClaudeCredentialReader.FromDefaultPath(),
            () => DateTimeOffset.Now,
            Task.Delay);

    private static IQuotaCollector BuildCodexCollector()
    {
        var binary = CodexBinaryLocator.LocateDefault();

        return new CodexCollector(
            () => binary is null
                ? throw new InvalidOperationException("codex.exe bulunamadi")
                : new StdioJsonRpcProcess(binary, "app-server"),
            () => DateTimeOffset.Now,
            Task.Delay,
            () => CodexRolloutReader.ReadLatest(
                CodexRolloutReader.DefaultSessionsRoot, DateTimeOffset.Now));
    }

    private void StartCollector(IQuotaCollector collector) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var snapshot in collector.Watch(_cts.Token))
                    _store.Apply(snapshot);
            }
            catch (OperationCanceledException) { }
        });

    private void TogglePopup()
    {
        if (_popup is null) return;
        if (_popup.IsVisible) _popup.Hide();
        else _popup.Show(_store.All(), DateTimeOffset.Now);
    }

    private void UpdateTray()
    {
        if (_trayIcon is null) return;

        var snapshots = _store.All();
        var old = _trayIcon.Icon;
        _trayIcon.Icon = TrayIconRenderer.Render(
            QuotaFormatter.HighestPercent(snapshots),
            QuotaFormatter.HasUnhealthy(snapshots));
        old?.Dispose();

        var tooltip = QuotaFormatter.Tooltip(snapshots, DateTimeOffset.Now);
        _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (_popup is { IsVisible: true })
            _popup.Show(snapshots, DateTimeOffset.Now);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _stalenessTimer?.Stop();
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose();
        _transport?.Dispose();
        base.OnExit(e);
    }
}
```

`NotifyIcon.Text` 63 karakterle sınırlıdır; kırpma bilinçlidir.

- [ ] **Step 5: Derle**

Run: `dotnet build`
Expected: 0 hata, 0 uyarı (uyarılar hata sayılır).

- [ ] **Step 6: Commit**

```bash
git add src AgentQuotaTray.sln
git commit -m "feat(app): wpf tray icon and quota popup"
```

---

### Task 8: Gerçek çalıştırma doğrulaması ve repo dosyaları

**Files:**
- Create: `AGENTS.md`
- Create: `CLAUDE.md`
- Create: `README.md`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: tüm önceki görevler
- Produces: çalışan uygulama, doğrulanmış gerçek çıktı

- [ ] **Step 1: Tüm testleri çalıştır**

Run: `dotnet test`
Expected: PASS — tüm testler, 0 başarısız.

- [ ] **Step 2: Uygulamayı çalıştır ve gerçek veriyi gözle**

Run: `dotnet run --project src/AgentQuotaTray.App`

Doğrulanacaklar — hepsi gözlenmeli, varsayılmamalı:
1. Görev çubuğunda halka simgesi belirir.
2. Simgeye tıklayınca popup açılır, **iki** sağlayıcı bloğu görünür.
3. Claude blokinde Session ve Weekly yüzdeleri var ve `claude` oturumunda `/usage` ile karşılaştırıldığında aynı.
4. Codex bloğundaki yüzdeler `codex` oturumunda `/status` ile aynı.
5. Popup dışına tıklayınca kapanır.
6. Sağ tık → Çıkış uygulamayı kapatır ve simge kaybolur.

Bir madde tutmuyorsa düzelt ve tekrar çalıştır. "Derlendi" doğrulama değildir.

- [ ] **Step 3: Hata yolunu bir kez gerçekten gözle**

`~/.claude/.credentials.json` dosyasını geçici olarak yeniden adlandır:

```bash
mv ~/.claude/.credentials.json ~/.claude/.credentials.json.bak
```

Uygulamayı yeniden çalıştır. Claude bloğu **"Giris gerekli"** göstermeli, `%0` **göstermemeli**. Sonra geri al:

```bash
mv ~/.claude/.credentials.json.bak ~/.claude/.credentials.json
```

Bu adım spec'in en önemli kuralını canlı doğrular.

- [ ] **Step 4: Repo dosyalarını yaz**

`AGENTS.md`:

```markdown
# agent-quota-tray

Windows tray uygulamasi: Claude Code ve Codex CLI kota yuzdelerini tek panelde gosterir.

## Bu repoda gecerli kurallar

- Tum mantik `src/AgentQuotaTray.Core` icindedir ve WPF'e bagimli degildir.
  `src/AgentQuotaTray.App` yalnizca cizim yapar; oraya is mantigi yazilmaz.
- Hicbir hata durumu `%0` olarak gosterilmez. `%0` yalnizca saglayicidan gelen
  gercek degerdir. Hata durumlari `HealthState` ile tasinir.
- Token loglanmaz, diske yazilmaz, istisna metnine sizmaz.
- Harici NuGet bagimliligi eklenmez (test projesindeki xUnit haric).
- Kullaniciya gorunen metin Turkce; kod, dosya adi ve commit mesaji ASCII.
- Veri kaynaklarinin ikisi de resmi degildir. Kaynak degisirse dogru davranis
  `ProtocolBroken` gostermektir, tahmin uretmek degildir.

Tasarim: `docs/specs/2026-09-03-agent-quota-tray-design.md`
Plan: `docs/superpowers/plans/2026-09-03-agent-quota-tray.md`
```

`CLAUDE.md`:

```markdown
@AGENTS.md
```

`README.md`:

```markdown
# Agent Quota Tray

Claude Code ve Codex CLI'in 5 saatlik ve 7 gunluk kota yuzdelerini Windows gorev
cubugunda gosterir. Oturum acmaya gerek yoktur.

## Calistirma

    dotnet run --project src/AgentQuotaTray.App

## Test

    dotnet test

## Veri kaynaklari

- Claude: `GET https://api.anthropic.com/api/oauth/usage`
  (token `~/.claude/.credentials.json`, header `anthropic-beta: oauth-2025-04-20`)
- Codex: `codex app-server` uzerinden JSON-RPC `account/rateLimits/read`
  ve `account/rateLimits/updated`; yedek olarak `~/.codex/sessions/**/rollout-*.jsonl`

Ikisi de belgelenmemis arayuzlerdir. Kirilirlarsa uygulama tahmin uretmez,
"API degismis" gosterir.
```

`.gitignore` sonuna ekle:

```
*.suo
.vs/
```

- [ ] **Step 5: Commit**

```bash
git add AGENTS.md CLAUDE.md README.md .gitignore
git commit -m "docs: project rules, readme and agent bridge"
```
