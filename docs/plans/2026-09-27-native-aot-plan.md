---
title: "Lim'it v0.4 - Native AOT uygulama planı"
created: 2026-09-27
modified: 2026-09-27
type: plan
status: active
tags: [proje, csharp, nativeaot, win32, plan]
---

# Lim'it v0.4 Native AOT Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking. In this repository Codex tasks run in a temporary
> git worktree (`codex exec -s workspace-write -C <worktree>`); the main agent reads the
> diff, runs the checks itself and applies it. A worker's "done" is not evidence.

**Goal:** Replace the WPF/WinForms UI with a NativeAOT + Win32 + software Direct2D app that
looks and behaves exactly like v0.3.4 while holding at most 20 MB Private Bytes idle.

**Architecture:** `LimitTray.Core` keeps all logic (now .NET 10, AOT-compatible) and gains a
sparse Codex data path (rollout tail + one-shot app-server), per-provider staleness,
dirty-only history writes and a fixture mode. A new `LimitTray.Native` exe hosts a Win32
message loop, a `Shell_NotifyIcon` tray icon and a layered popup drawn with Direct2D in
software mode; the popup and every drawing resource exist only while it is open.

**Tech Stack:** .NET 10 SDK 10.0.401, C# 13, NativeAOT (MSVC 14.44 linker), CsWin32
(build-time only), Direct2D + DirectWrite (software render target), xUnit.

**Spec:** `docs/specs/2026-09-27-native-aot-design.md`

## Global Constraints

- TFM `net10.0` for Core and `tests/LimitTray.Tests`; `net10.0-windows` for Native,
  `tests/LimitTray.Native.Tests` and (until Task 15) `LimitTray.App`.
- `TreatWarningsAsErrors=true` in every project. AOT/trim warnings (IL2xxx, IL3xxx) count.
- No runtime NuGet dependency. Only exception: `Microsoft.Windows.CsWin32` with
  `PrivateAssets="all"` in `LimitTray.Native`. Test projects keep xUnit + test SDK.
- Source files, identifiers, file names and commit messages are ASCII. Display strings live
  only in `src/LimitTray.Core/Presentation/Strings.cs` (Turkish accents allowed there).
- No failure state is ever drawn as `0%`; failures travel as `HealthState` and show in words.
- The Claude token is never logged, displayed, written to disk or put in an exception message.
- `history.json` holds only percentages, window lengths and timestamps.
- Colour comes only from `Theme.ColourFor`; the tray icon draws `TrayIconModel` and decides nothing.
- No U+2014 anywhere in the repo (`.github/scripts/Test-NoEmDash.ps1`, run in CI).
- Memory is judged by Private Bytes (`Process.PrivateMemorySize64`), never by working set.
- Codex app-server exists only during a read; rollout files are read only from the tail (64 KB).

## Review Focus

1. **Explorer restarts while the app runs.** The icon must come back (`TaskbarCreated`).
   Pinned in Task 2 (message routing test + manual kill/restart step).
2. **A rollout file is being written, locked, truncated or deleted during a poll.** No
   exception, no bogus reading. Pinned in Task 3 tests.
3. **Codex is not installed (`~/.codex` missing, `codex.exe` not found).** Codex card shows
   the failure in words after three failed reads, no process spawn more often than every
   10 minutes, Claude unaffected. Pinned in Task 5 tests.
4. **A quota window resets between two sparse Codex reads.** The old percentage is never
   presented as fresh past `resets_at + 30 s` and is never replaced by `0%`. Pinned in Task 5.
5. **Popup opened on a monitor with a different DPI or with the taskbar on another edge.**
   Correct size, position and crisp text. Pinned in Task 12 (layout test per edge) and the
   manual matrix in Task 14.

---

## Execution order

| Wave | Tasks | Owner |
|---|---|---|
| A | 1 (.NET 10), 3 (rollout tail), 4 (server reader) | Codex, parallel |
| B | 2 (native skeleton), 5 (collector), 6 (store/history/fixture) | Codex |
| C | 7 (graphics + popup skeleton + floor gate) | Claude; **stop if floor > 20 MB** |
| D | 8 (host wiring), 9 (measurement harness) | Codex |
| E | 10 (tray painter), 11 (layout/animator), 12 (panel), 13 (settings) | Claude |
| F | 14 (parity + measurement gates), 15 (switch-over), 16 (docs), 17 (live run + review) | mixed |

At most three Codex lanes per ten minutes (guard default). Every lane is `gpt-6-luna`,
effort `high`, in a worktree created from `native-aot`.

---

### Task 1: Target .NET 10 and mark Core AOT-compatible

**Files:**
- Create: `global.json`
- Modify: `src/LimitTray.Core/LimitTray.Core.csproj`, `tests/LimitTray.Tests/LimitTray.Tests.csproj`,
  `src/LimitTray.App/LimitTray.App.csproj`, `tools/IconGen/IconGen.csproj`,
  `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: every project builds on .NET 10; `LimitTray.Core` has `IsAotCompatible=true`.

- [ ] **Step 1: Record the current test count**

Run: `dotnet test -c Release 2>&1 | Select-String "Passed!|Failed!"`
Expected: `Passed! - Failed: 0, Passed: 234` (write the exact number into the lane report).

- [ ] **Step 2: Add `global.json`**

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 3: Retarget**

- Core and Tests: `<TargetFramework>net10.0</TargetFramework>`.
- Core also: `<IsAotCompatible>true</IsAotCompatible>`.
- App: `<TargetFramework>net10.0-windows</TargetFramework>`.
- IconGen: same TFM family it has today, moved to 10.
- `ci.yml` and `release.yml`: `dotnet-version: 10.0.x`.
- If `Microsoft.NET.Test.Sdk` / xunit fail to restore on net10, bump to the latest stable
  versions and say which in the report.

- [ ] **Step 4: Build and test**

Run: `dotnet build -c Release` then `dotnet test -c Release`
Expected: build `0 Warning(s) 0 Error(s)`; tests pass with the same count as Step 1.
If the AOT analyzer reports a warning in Core, fix the code (do not suppress) and name the
warning in the report.

- [ ] **Step 5: Commit**

```bash
git add global.json src/LimitTray.Core/LimitTray.Core.csproj tests/LimitTray.Tests/LimitTray.Tests.csproj src/LimitTray.App/LimitTray.App.csproj tools/IconGen/IconGen.csproj .github/workflows/ci.yml .github/workflows/release.yml
git commit -m "build: target .NET 10 and mark Core AOT compatible"
```

---

### Task 2: Native project skeleton, message loop, tray icon, single instance

**Files:**
- Create: `src/LimitTray.Native/LimitTray.Native.csproj`, `src/LimitTray.Native/app.manifest`,
  `src/LimitTray.Native/NativeMethods.txt`, `src/LimitTray.Native/NativeMethods.json`,
  `src/LimitTray.Native/Program.cs`, `src/LimitTray.Native/Host/MessageWindow.cs`,
  `src/LimitTray.Native/Host/UiThread.cs`, `src/LimitTray.Native/Host/SingleInstance.cs`,
  `src/LimitTray.Native/Tray/TrayIcon.cs`
- Create: `tests/LimitTray.Native.Tests/LimitTray.Native.Tests.csproj`,
  `tests/LimitTray.Native.Tests/SingleInstanceTests.cs`
- Modify: `LimitTray.sln` (add both projects)

**Interfaces:**
- Consumes: `LimitTray.Core.Presentation.Strings` (`Exit`).
- Produces (all `internal`, namespace `LimitTray.Native.*`, `InternalsVisibleTo` the test project):

```csharp
namespace LimitTray.Native.Host;
internal sealed class MessageWindow : IDisposable
{
    public const string ClassName = "LimitTray.Host";
    public MessageWindow();                       // hidden top-level WS_POPUP tool window (see Step 3)
    public HWND Handle { get; }
    public uint TaskbarCreatedMessage { get; }    // RegisterWindowMessage("TaskbarCreated")
    public void Register(uint message, Func<WPARAM, LPARAM, LRESULT?> handler);
    public static int RunLoop();                  // GetMessage loop until WM_QUIT
}
internal static class UiThread
{
    public static void Initialize(MessageWindow window);
    public static void Post(Action action);       // thread-safe; runs on the UI thread in FIFO order
    public static bool IsCurrent { get; }
}
internal static class SingleInstance
{
    public static string MutexName(string dataDirectory); // "LimitTray-" + first 16 hex of SHA-256(upper-case full path)
    public static IDisposable? TryAcquire(string dataDirectory); // null when another instance holds it
}
namespace LimitTray.Native.Tray;
internal sealed class TrayIcon : IDisposable
{
    public TrayIcon(MessageWindow window, HICON icon, string tooltip);
    public event Action? LeftClick;
    public event Action<int, int>? RightClick;    // screen coordinates
    public void SetIcon(HICON icon);              // does not take ownership
    public void SetTooltip(string text);          // truncated to 127 chars
    public void ShowBalloon(string title, string body); // NIIF_WARNING
    public RECT? GetIconRect();                   // Shell_NotifyIconGetRect
}
```

- [ ] **Step 1: Project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>13.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PublishAot>true</PublishAot>
    <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
    <ApplicationIcon>..\..\assets\limit.ico</ApplicationIcon>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>LimitTray</AssemblyName>
    <AssemblyTitle>Lim'it</AssemblyTitle>
    <Product>Lim'it</Product>
    <Company>Morp1e</Company>
    <Authors>Morp1e</Authors>
    <Version>0.4.0</Version>
    <AssemblyVersion>0.4.0.0</AssemblyVersion>
    <FileVersion>0.4.0.0</FileVersion>
    <Copyright>Copyright (c) Morp1e</Copyright>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWin32" Version="PIN-LATEST-STABLE" PrivateAssets="all" />
    <ProjectReference Include="..\LimitTray.Core\LimitTray.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="LimitTray.Native.Tests" />
  </ItemGroup>
</Project>
```

Replace `PIN-LATEST-STABLE` with the newest stable CsWin32 version on nuget.org and name it
in the report. `NativeMethods.json`: `{ "allowMarshaling": false, "public": false }`.
`app.manifest`: `dpiAwareness` `PerMonitorV2`, `dpiAware` `true/pm`, supportedOS Windows 10.

- [ ] **Step 2: Failing test for the mutex name**

```csharp
namespace LimitTray.Native.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void MutexName_IsStableAndCaseInsensitive()
    {
        var a = Host.SingleInstance.MutexName(@"C:\Users\x\AppData\Local\limit-tray");
        var b = Host.SingleInstance.MutexName(@"c:\users\X\appdata\local\LIMIT-TRAY");
        Assert.Equal(a, b);
        Assert.StartsWith("LimitTray-", a);
        Assert.Equal("LimitTray-".Length + 16, a.Length);
    }

    [Fact]
    public void MutexName_DiffersPerDataDirectory() =>
        Assert.NotEqual(
            Host.SingleInstance.MutexName(@"C:\a"),
            Host.SingleInstance.MutexName(@"C:\b"));
}
```

Run: `dotnet test tests/LimitTray.Native.Tests` - Expected: FAIL (type missing).

- [ ] **Step 3: Implement the skeleton**

- `Program.Main` (`[STAThread]`): acquire single instance for the default data directory
  (`%LOCALAPPDATA%\limit-tray`) or exit 0; create `MessageWindow`; `UiThread.Initialize`;
  load the app icon from the exe's own Win32 icon resource (`LoadImage` on
  `GetModuleHandle(null)`, the resource the SDK embeds from `ApplicationIcon`); create
  `TrayIcon` with tooltip `Lim'it`; `RightClick` shows a `TrackPopupMenu` with one item
  `Strings.ForCulture(CultureInfo.CurrentUICulture).Exit` that posts `WM_QUIT`; `RunLoop`.
- `MessageWindow`: `RegisterClassEx` + `CreateWindowEx` of a hidden top-level `WS_POPUP`
  window with `WS_EX_TOOLWINDOW`, never shown. Not `HWND_MESSAGE`: the `TaskbarCreated`
  broadcast only reaches top-level windows, and a message-only window would lose the tray
  icon for good after an Explorer restart (say so in a comment). The window procedure is an
  `[UnmanagedCallersOnly]` static that looks the handler up in a dictionary.
- `TrayIcon`: `NOTIFYICONDATAW` with `NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP`,
  `NOTIFYICON_VERSION_4`; callback `WM_APP + 1`; `WM_LBUTTONUP` → `LeftClick`,
  `WM_CONTEXTMENU` → `RightClick` (call `SetForegroundWindow` before `TrackPopupMenu` so the
  menu closes on outside click); on `TaskbarCreated` re-add with the current icon and tip.
  `Dispose` deletes the icon.
- `UiThread.Post`: enqueue to a `ConcurrentQueue<Action>` and `PostMessage(WM_APP + 2)`;
  the handler drains the queue.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/LimitTray.Native.Tests` - Expected: PASS.
Run: `dotnet publish src/LimitTray.Native -c Release -r win-x64` - Expected: no warning,
`bin/Release/net10.0-windows/win-x64/publish/LimitTray.exe` exists; report its size.
Manual (report each): exe starts, icon visible with tooltip; right click shows Exit and Exit
removes the icon; a second copy started while the first runs exits within 1 s;
`Stop-Process -Name explorer; Start-Process explorer` brings the icon back.

- [ ] **Step 5: Commit**

```bash
git add src/LimitTray.Native tests/LimitTray.Native.Tests LimitTray.sln
git commit -m "feat(native): AOT skeleton with message loop, tray icon and single instance"
```

---

### Task 3: `CodexRolloutTail` reads only the tail of the newest rollout files

**Files:**
- Create: `src/LimitTray.Core/Codex/CodexRolloutTail.cs`
- Test: `tests/LimitTray.Tests/Codex/CodexRolloutTailTests.cs`

**Interfaces:**
- Consumes: `CodexRateLimitsParser.ReadWindow(JsonElement, string, string, string, string, TimeSpan)`
  (internal, same assembly), `CodexRateLimitsParser.Provider`, `QuotaWindowRange.IsValid`.
- Produces:

```csharp
namespace LimitTray.Core.Codex;
public sealed class CodexRolloutTail
{
    public const int TailBytes = 64 * 1024;
    public const int TrackedFiles = 4;
    public static string DefaultSessionsRoot { get; }   // %USERPROFILE%\.codex\sessions
    public CodexRolloutTail(string sessionsRoot, Func<DateTimeOffset> clock);
    /// Never throws. Returns null when no tracked file changed length since the previous
    /// call or no changed tail holds a parseable reading.
    public QuotaSnapshot? Poll();
}
```

Behaviour (the tests below pin each line):
- Day folders are found from directory **names** only: year (4 digits) desc, month desc,
  day desc; the newest **two** day folders overall (they may span a month or year).
- In those folders, `rollout-*.jsonl` ordered by file name descending (the name starts with
  the creation timestamp); the first `TrackedFiles` are tracked.
- Change detection uses the real length from an open handle
  (`FileShare.ReadWrite | FileShare.Delete`), never the directory entry's write time:
  measured 2026-09-27, a file being written kept its creation time as write time.
- On the first call every tracked file counts as changed.
- For a changed file: read the last `min(length, TailBytes)` bytes; if the read did not
  start at offset 0, drop everything up to and including the first `\n`; drop a final
  segment that does not end with `\n`. Scan the remaining lines from the end; the first line
  that contains `"rate_limits"`, parses with `JsonDocument`, has a string `timestamp`
  (ISO 8601) at the root, and yields at least one valid window wins for that file.
  `rate_limits` is found at the root or one object level down (`payload.rate_limits`).
- Windows: `ReadWindow(limits, "primary", "used_percent", "window_minutes", "resets_at", 5 h)`
  and `("secondary", ..., 7 d)`; a window failing `QuotaWindowRange.IsValid` makes the line
  unusable (keep scanning earlier lines).
- Across changed files the reading with the latest line timestamp wins. `FetchedAt` is that
  timestamp, clamped to `clock()` if it is in the future. `Health = Fresh`, `Detail = null`.
- Buffers come from `ArrayPool<byte>.Shared` and are returned.
- Remembered lengths are pruned to the currently tracked set.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using LimitTray.Core.Codex;
using LimitTray.Core.Model;

namespace LimitTray.Tests.Codex;

public class CodexRolloutTailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rt-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    public CodexRolloutTailTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CodexRolloutTail Tail() => new(_root, () => Now);

    private string Day(string y, string m, string d)
    {
        var dir = Path.Combine(_root, y, m, d);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Line(string ts, double primary, double secondary) =>
        "{\"timestamp\":\"" + ts + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"," +
        "\"rate_limits\":{\"primary\":{\"used_percent\":" + primary.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"window_minutes\":300,\"resets_at\":1790479554},\"secondary\":{\"used_percent\":" +
        secondary.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"window_minutes\":10080,\"resets_at\":1791047422}}}}\n";

    private static string Filler(int bytes)
    {
        var sb = new StringBuilder();
        while (sb.Length < bytes) sb.Append("{\"timestamp\":\"2026-09-27T00:00:00Z\",\"type\":\"response_item\",\"payload\":{\"text\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}}\n");
        return sb.ToString();
    }

    [Fact]
    public void Poll_FirstCall_ReadsNewestFile_AsFreshWithLineTimestamp()
    {
        var dir = Day("2026", "09", "27");
        File.WriteAllText(Path.Combine(dir, "rollout-2026-09-27T01-40-28-a.jsonl"),
            Line("2026-09-27T11:59:00Z", 12.5, 40));

        var snap = Tail().Poll();

        Assert.NotNull(snap);
        Assert.Equal("codex", snap!.Provider);
        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(12.5, snap.Session!.Percent);
        Assert.Equal(40, snap.Weekly!.Percent);
        Assert.Equal(TimeSpan.FromMinutes(300), snap.Session.WindowLength);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 11, 59, 0, TimeSpan.Zero), snap.FetchedAt);
    }

    [Fact]
    public void Poll_WithoutGrowth_ReturnsNull_AfterAppend_ReturnsNewValue()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-40-28-a.jsonl");
        File.WriteAllText(path, Line("2026-09-27T11:00:00Z", 10, 20));
        var tail = Tail();

        Assert.NotNull(tail.Poll());
        Assert.Null(tail.Poll());

        File.AppendAllText(path, Line("2026-09-27T11:30:00Z", 11, 21));
        var next = tail.Poll();
        Assert.Equal(11, next!.Session!.Percent);
    }

    [Fact]
    public void Poll_IncompleteLastLine_IsIgnored()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-40-28-a.jsonl");
        var partial = Line("2026-09-27T11:40:00Z", 99, 99).TrimEnd('\n');
        File.WriteAllText(path, Line("2026-09-27T11:00:00Z", 10, 20) + partial[..(partial.Length - 5)]);

        Assert.Equal(10, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_ReadsOnlyTheTail_OfALargeFile()
    {
        // The only reading sits before the last 64 KB; reading it would prove a full read.
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-40-28-a.jsonl");
        File.WriteAllText(path, Line("2026-09-27T10:00:00Z", 5, 5) + Filler(200 * 1024));

        Assert.Null(Tail().Poll());
    }

    [Fact]
    public void Poll_ConcurrentSessions_LatestLineTimestampWins()
    {
        var dir = Day("2026", "09", "27");
        File.WriteAllText(Path.Combine(dir, "rollout-2026-09-27T09-00-00-a.jsonl"), Line("2026-09-27T11:50:00Z", 30, 30));
        File.WriteAllText(Path.Combine(dir, "rollout-2026-09-27T10-00-00-b.jsonl"), Line("2026-09-27T11:10:00Z", 20, 20));

        Assert.Equal(30, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_AcrossMidnight_UsesThePreviousDayFolder()
    {
        File.WriteAllText(Path.Combine(Day("2026", "09", "26"), "rollout-2026-09-26T23-50-00-a.jsonl"), Line("2026-09-26T23:59:00Z", 7, 8));
        File.WriteAllText(Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T00-01-00-b.jsonl"), Filler(1024));

        Assert.Equal(7, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_AcrossYearBoundary_FindsBothDays()
    {
        File.WriteAllText(Path.Combine(Day("2026", "12", "31"), "rollout-2026-12-31T23-00-00-a.jsonl"), Line("2026-12-31T23:30:00Z", 9, 9));
        File.WriteAllText(Path.Combine(Day("2027", "01", "01"), "rollout-2027-01-01T00-10-00-b.jsonl"), Filler(1024));

        Assert.Equal(9, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_FutureTimestamp_IsClampedToNow()
    {
        File.WriteAllText(Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl"), Line("2026-09-28T00:00:00Z", 1, 1));

        Assert.Equal(Now, Tail().Poll()!.FetchedAt);
    }

    [Fact]
    public void Poll_MalformedOrOutOfRangeLines_AreSkipped()
    {
        var bad = "{\"timestamp\":\"2026-09-27T11:59:00Z\",\"rate_limits\":{\"primary\":{\"used_percent\":150}}}\n";
        var broken = "{\"timestamp\":\"2026-09-27T11:59:30Z\",\"rate_limits\":{\n";
        File.WriteAllText(Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl"),
            Line("2026-09-27T11:00:00Z", 15, 25) + bad + broken);

        Assert.Equal(15, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_FileHeldOpenForWriting_IsStillRead()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl");
        using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var bytes = Encoding.UTF8.GetBytes(Line("2026-09-27T11:00:00Z", 33, 44));
        writer.Write(bytes); writer.Flush();

        Assert.Equal(33, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_ExclusivelyLockedFile_DoesNotThrow()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl");
        File.WriteAllText(path, Line("2026-09-27T11:00:00Z", 1, 1));
        using var locker = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Record.Exception(() => Tail().Poll());
        Assert.Null(ex);
    }

    [Fact]
    public void Poll_MissingRoot_ReturnsNull() =>
        Assert.Null(new CodexRolloutTail(Path.Combine(_root, "nope"), () => Now).Poll());
}
```

Run: `dotnet test tests/LimitTray.Tests --filter CodexRolloutTailTests` - Expected: FAIL (type missing).

- [ ] **Step 2: Implement `CodexRolloutTail` as specified above.**

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/LimitTray.Tests --filter CodexRolloutTailTests` - Expected: all PASS.
Run: `dotnet test -c Release` - Expected: whole suite green.

- [ ] **Step 4: Commit**

```bash
git add src/LimitTray.Core/Codex/CodexRolloutTail.cs tests/LimitTray.Tests/Codex/CodexRolloutTailTests.cs
git commit -m "feat(core): read Codex quota from the tail of the newest rollout files"
```

---

### Task 4: `CodexServerReader` performs one short app-server read

**Files:**
- Create: `src/LimitTray.Core/Codex/CodexProtocol.cs`, `src/LimitTray.Core/Codex/CodexServerReader.cs`
- Test: `tests/LimitTray.Tests/Codex/CodexServerReaderTests.cs`

**Interfaces:**
- Consumes: `IJsonRpcProcess`, `CodexRateLimitsParser.ParseAppServer(string, DateTimeOffset)`.
- Produces:

```csharp
namespace LimitTray.Core.Codex;
internal static class CodexProtocol
{
    public const string InitializeMessage = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"limit-tray","title":"Lim'it","version":"0.4.0"}}}""";
    public const string InitializedNotification = """{"jsonrpc":"2.0","method":"initialized","params":{}}""";
    public const string ReadMessage = """{"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{}}""";
    public static bool IsResponse(string line, int id); // JsonDocument; false on bad JSON
}
public sealed class CodexServerReader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    public CodexServerReader(Func<IJsonRpcProcess> processFactory, Func<DateTimeOffset> clock, TimeSpan? timeout = null);
    /// Start, initialize, initialized, read, parse, Dispose (kills the process tree).
    /// Returns Fresh, or Unhealthy(ProtocolBroken) with a detail that is a fixed ASCII
    /// phrase or an exception type name. Throws only OperationCanceledException for ct.
    public Task<QuotaSnapshot> ReadOnceAsync(CancellationToken ct);
}
```

Details: `"codex.exe bulunamadi"` is not special; any factory exception →
`"app-server baslatilamadi: " + ex.GetType().Name`. Stream ends before the id 2 response →
`"app-server erken kapandi"`. Timeout (covers the whole exchange) → `"app-server zaman asimi"`.
Lines that are notifications (have `method`) are ignored. The process is disposed exactly
once on every path.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LimitTray.Core.Codex;
using LimitTray.Core.Model;
using LimitTray.Core.Process;

namespace LimitTray.Tests.Codex;

public class CodexServerReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private const string InitOk = """{"jsonrpc":"2.0","id":1,"result":{}}""";
    private const string ReadOk = """{"jsonrpc":"2.0","id":2,"result":{"rateLimits":{"primary":{"usedPercent":18,"windowDurationMins":300,"resetsAt":1790479554},"secondary":{"usedPercent":22,"windowDurationMins":10080,"resetsAt":1791047422}}}}""";
    private const string Notice = """{"jsonrpc":"2.0","method":"account/rateLimits/updated","params":{"rateLimits":{"primary":{"usedPercent":99}}}}""";

    private sealed class ScriptedProcess : IJsonRpcProcess
    {
        private readonly Channel<string> _out = Channel.CreateUnbounded<string>();
        private readonly Func<string, IEnumerable<string>> _reply;
        public List<string> Sent { get; } = new();
        public int Disposed { get; private set; }
        public bool ThrowOnStart { get; init; }
        public ScriptedProcess(Func<string, IEnumerable<string>> reply) => _reply = reply;
        public Task StartAsync(CancellationToken ct) =>
            ThrowOnStart ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        public Task SendAsync(string line, CancellationToken ct)
        {
            Sent.Add(line);
            foreach (var r in _reply(line)) _out.Writer.TryWrite(r);
            return Task.CompletedTask;
        }
        public void End() => _out.Writer.TryComplete();
        public async IAsyncEnumerable<string> ReadLines([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var l in _out.Reader.ReadAllAsync(ct)) yield return l;
        }
        public void Dispose() => Disposed++;
    }

    private static IEnumerable<string> Happy(string sent) =>
        sent.Contains("\"initialize\"") ? new[] { InitOk } :
        sent.Contains("rateLimits/read") ? new[] { Notice, ReadOk } :
        Array.Empty<string>();

    [Fact]
    public async Task ReadOnce_HappyPath_ReturnsFreshAndDisposesOnce()
    {
        var p = new ScriptedProcess(Happy);
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(18, snap.Session!.Percent);
        Assert.Equal(22, snap.Weekly!.Percent);
        Assert.Equal(1, p.Disposed);
        Assert.Contains(p.Sent, s => s.Contains("\"initialized\""));
    }

    [Fact]
    public async Task ReadOnce_NotificationBeforeResponse_IsIgnored()
    {
        var p = new ScriptedProcess(Happy);
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);
        Assert.NotEqual(99, snap.Session!.Percent);
    }

    [Fact]
    public async Task ReadOnce_InitializeNeverAnswered_TimesOutAsBroken()
    {
        var p = new ScriptedProcess(_ => Array.Empty<string>());
        var snap = await new CodexServerReader(() => p, () => Now, TimeSpan.FromMilliseconds(100))
            .ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Equal("app-server zaman asimi", snap.Detail);
        Assert.Equal(1, p.Disposed);
    }

    [Fact]
    public async Task ReadOnce_ProcessEndsEarly_IsBroken()
    {
        ScriptedProcess? p = null;
        p = new ScriptedProcess(sent => { if (sent.Contains("\"initialize\"")) p!.End(); return Array.Empty<string>(); });
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);

        Assert.Equal("app-server erken kapandi", snap.Detail);
        Assert.Equal(1, p.Disposed);
    }

    [Fact]
    public async Task ReadOnce_FactoryThrows_IsBrokenWithTypeNameOnly()
    {
        var snap = await new CodexServerReader(() => throw new FileNotFoundException("C:\\secret\\path"), () => Now)
            .ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Equal("app-server baslatilamadi: FileNotFoundException", snap.Detail);
    }

    [Fact]
    public async Task ReadOnce_StartThrows_IsBrokenAndDisposed()
    {
        var p = new ScriptedProcess(Happy) { ThrowOnStart = true };
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Equal(1, p.Disposed);
    }

    [Fact]
    public async Task ReadOnce_Cancelled_Throws()
    {
        var p = new ScriptedProcess(_ => Array.Empty<string>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CodexServerReader(() => p, () => Now).ReadOnceAsync(cts.Token));
    }
}
```

Run: `dotnet test tests/LimitTray.Tests --filter CodexServerReaderTests` - Expected: FAIL.

- [ ] **Step 2: Implement `CodexProtocol` and `CodexServerReader`.**

Leave `CodexCollector` untouched in this task (it keeps its private copies of the messages
until Task 5 deletes them).

- [ ] **Step 3: Run the tests** - filter, then the whole suite. Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add src/LimitTray.Core/Codex/CodexProtocol.cs src/LimitTray.Core/Codex/CodexServerReader.cs tests/LimitTray.Tests/Codex/CodexServerReaderTests.cs
git commit -m "feat(core): one-shot codex app-server rate limit read"
```

---

### Task 5: Sparse `CodexCollector` (rollout 30 s, server 10 min, resets, failures)

**Files:**
- Modify: `src/LimitTray.Core/Codex/CodexCollector.cs` (rewrite)
- Delete: `src/LimitTray.Core/Codex/CodexRolloutReader.cs`, `tests/LimitTray.Tests/Codex/CodexRolloutReaderTests.cs`
- Rewrite: `tests/LimitTray.Tests/Codex/CodexCollectorTests.cs`
- Modify: `src/LimitTray.App/App.xaml.cs` (`BuildCodexCollector` only, so the WPF app still builds until Task 15)

**Interfaces:**
- Consumes: `CodexServerReader.ReadOnceAsync`, `CodexRolloutTail.Poll` (as delegates).
- Produces:

```csharp
namespace LimitTray.Core.Codex;
public sealed class CodexCollector : IQuotaCollector
{
    public static readonly TimeSpan RolloutPollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ServerReadInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AfterResetDelay = TimeSpan.FromSeconds(30);
    public const int FailuresBeforeBroken = 3;
    public CodexCollector(
        Func<CancellationToken, Task<QuotaSnapshot>> readServer,
        Func<QuotaSnapshot?> pollRollout,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay);
    public string Provider { get; }           // "codex"
    public void RequestRefresh();             // next loop turn reads the server; cuts the current wait short
    public IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);
}
```

Algorithm (one loop, no background tasks):

```text
latest = null; failures = 0; staledResets = {}
nextPoll = clock(); nextServer = clock()
loop until ct:
  now = clock()
  if refreshRequested: nextServer = now; refreshRequested = false
  if now >= nextPoll:
      r = pollRollout(); if r is not null: Offer(r)
      nextPoll = now + RolloutPollInterval
  due = earliest (w.ResetsAt + AfterResetDelay) over latest's windows where
        latest.FetchedAt < w.ResetsAt and w.ResetsAt not in staledResets   (null if none)
  if now >= nextServer or (due is not null and now >= due):
      s = await readServer(ct)
      if s.Health == Fresh: failures = 0; Offer(s)
      else: failures++; if failures == FailuresBeforeBroken: emit s
      for each window w of latest with latest.FetchedAt < w.ResetsAt <= now - AfterResetDelay
          and w.ResetsAt not in staledResets:
              add w.ResetsAt to staledResets; emit latest with Health = Stale
      nextServer = now + ServerReadInterval
  wake = min(nextPoll, nextServer, due ?? max)
  await delay(max(0, wake - clock()), token cancelled by RequestRefresh or ct)
    (a cancellation caused by RequestRefresh is swallowed; one caused by ct ends the loop)

Offer(r): if latest is null or r.FetchedAt > latest.FetchedAt:
    latest = r with { Session = r.Session ?? latest?.Session, Weekly = r.Weekly ?? latest?.Weekly }
    emit latest
```

Emissions are collected per turn and yielded outside any `try/catch` (C# cannot `yield`
inside a `catch`-bearing `try`). `RequestRefresh` can be called while the consumer is
handling an emission, before the loop reaches its wait: check the flag immediately before
waiting and skip the wait when it is set. `readServer` is trusted not to throw except for
cancellation; if it does throw anything else, treat it as a failed read.

- [ ] **Step 1: Write the failing tests**

Use a fake clock that the fake delay advances, so every test runs instantly:

```csharp
using LimitTray.Core.Codex;
using LimitTray.Core.Model;

namespace LimitTray.Tests.Codex;

public class CodexCollectorTests
{
    private sealed class Harness
    {
        public DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        public readonly List<TimeSpan> Delays = new();
        public readonly Queue<QuotaSnapshot?> Rollout = new();
        public readonly Queue<QuotaSnapshot> Server = new();
        public int ServerReads, RolloutPolls;
        public CodexCollector Build() => new(
            _ => { ServerReads++; return Task.FromResult(Server.Count > 0 ? Server.Dequeue() : Broken()); },
            () => { RolloutPolls++; return Rollout.Count > 0 ? Rollout.Dequeue() : null; },
            () => Now,
            (d, ct) => { ct.ThrowIfCancellationRequested(); Delays.Add(d); Now += d; return Task.CompletedTask; });
        public QuotaSnapshot Fresh(double s, double w, DateTimeOffset at, DateTimeOffset? sessionReset = null) =>
            new("codex", new QuotaWindow(s, sessionReset ?? at.AddHours(5), TimeSpan.FromHours(5)),
                new QuotaWindow(w, at.AddDays(7), TimeSpan.FromDays(7)), HealthState.Fresh, at, null);
        public QuotaSnapshot Broken() =>
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "app-server zaman asimi");
    }

    private static async Task<List<QuotaSnapshot>> Run(CodexCollector c, int take)
    {
        var got = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var s in c.Watch(cts.Token))
        {
            got.Add(s);
            if (got.Count == take) break;
        }
        return got;
    }

    [Fact]
    public async Task Start_PollsRolloutAndReadsServer_BeforeFirstWait()
    {
        var h = new Harness();
        h.Server.Enqueue(h.Fresh(10, 20, h.Now));
        var got = await Run(h.Build(), 1);

        Assert.Equal(1, h.RolloutPolls);
        Assert.Equal(1, h.ServerReads);
        Assert.Empty(h.Delays);
        Assert.Equal(10, got[0].Session!.Percent);
    }

    [Fact]
    public async Task Server_IsReadEveryTenMinutes_RolloutEveryThirtySeconds()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) h.Server.Enqueue(h.Fresh(10 + i, 20, h.Now.AddMinutes(10 * i)));
        await Run(h.Build(), 3);

        Assert.Equal(3, h.ServerReads);
        Assert.Equal(41, h.RolloutPolls);                       // t=0, then every 30 s for 20 min
        Assert.All(h.Delays, d => Assert.True(d <= TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task OlderReading_IsNotEmitted_NewerIs()
    {
        var h = new Harness();
        h.Rollout.Enqueue(h.Fresh(30, 30, h.Now));              // newest
        h.Server.Enqueue(h.Fresh(10, 10, h.Now.AddMinutes(-5)));// older, must be dropped
        h.Rollout.Enqueue(h.Fresh(31, 31, h.Now.AddSeconds(30)));
        var got = await Run(h.Build(), 2);

        Assert.Equal(new[] { 30.0, 31.0 }, got.Select(s => s.Session!.Percent));
    }

    [Fact]
    public async Task PartialReading_KeepsTheOtherWindow()
    {
        var h = new Harness();
        h.Server.Enqueue(h.Fresh(10, 20, h.Now));
        h.Rollout.Enqueue(null);
        h.Rollout.Enqueue(new QuotaSnapshot("codex", new QuotaWindow(11, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, h.Now.AddSeconds(30), null));
        var got = await Run(h.Build(), 2);

        Assert.Equal(11, got[1].Session!.Percent);
        Assert.Equal(20, got[1].Weekly!.Percent);
    }

    [Fact]
    public async Task ResetPassed_AndReadFails_EmitsStaleWithTheOldPercent_NeverZero()
    {
        var h = new Harness();
        var start = h.Now;
        h.Server.Enqueue(h.Fresh(70, 20, start, sessionReset: start.AddMinutes(3)));
        // every later server read fails
        var got = await Run(h.Build(), 2);

        Assert.Equal(HealthState.Stale, got[1].Health);
        Assert.Equal(70, got[1].Session!.Percent);
        Assert.True(h.Now >= start.AddMinutes(3).AddSeconds(30));
        Assert.True(h.Now < start.AddMinutes(10));              // not waiting for the 10-minute read
    }

    [Fact]
    public async Task ResetPassed_AndReadSucceeds_EmitsFreshOnly()
    {
        var h = new Harness();
        var start = h.Now;
        h.Server.Enqueue(h.Fresh(70, 20, start, sessionReset: start.AddMinutes(3)));
        h.Server.Enqueue(h.Fresh(2, 21, start.AddMinutes(3).AddSeconds(31)));
        var got = await Run(h.Build(), 2);

        Assert.Equal(HealthState.Fresh, got[1].Health);
        Assert.Equal(2, got[1].Session!.Percent);
    }

    [Fact]
    public async Task ThreeFailedReads_EmitProtocolBrokenOnce_ThenRolloutRecovers()
    {
        var h = new Harness();                                 // server queue empty: every read fails
        for (var i = 0; i < 50; i++) h.Rollout.Enqueue(null);  // polls at 0 s .. 24 min 30 s are quiet
        h.Rollout.Enqueue(h.Fresh(12, 13, h.Now.AddMinutes(25)));
        var got = await Run(h.Build(), 2);

        Assert.Equal(HealthState.ProtocolBroken, got[0].Health);
        Assert.Equal(HealthState.Fresh, got[1].Health);
        Assert.Equal(3, h.ServerReads);                        // t=0, 10, 20 min; no fast retry
    }

    [Fact]
    public async Task RequestRefresh_CutsTheWaitShortAndReads()
    {
        var h = new Harness();
        h.Server.Enqueue(h.Fresh(10, 20, h.Now));
        h.Server.Enqueue(h.Fresh(15, 20, h.Now.AddSeconds(1)));
        var collector = new CodexCollector(
            _ => { h.ServerReads++; return Task.FromResult(h.Server.Count > 0 ? h.Server.Dequeue() : h.Broken()); },
            () => null,
            () => h.Now,
            async (d, ct) => { await Task.Delay(Timeout.Infinite, ct); });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var got = new List<QuotaSnapshot>();
        await foreach (var s in collector.Watch(cts.Token))
        {
            got.Add(s);
            if (got.Count == 1) { h.Now = h.Now.AddSeconds(1); collector.RequestRefresh(); }
            if (got.Count == 2) break;
        }

        Assert.Equal(15, got[1].Session!.Percent);
        Assert.Equal(2, h.ServerReads);
    }

    [Fact]
    public async Task Cancellation_EndsTheStreamWithoutThrowing()
    {
        var h = new Harness();
        var collector = new CodexCollector(_ => Task.FromResult(h.Broken()), () => null, () => h.Now,
            async (d, ct) => await Task.Delay(Timeout.Infinite, ct));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var ex = await Record.ExceptionAsync(async () => { await foreach (var _ in collector.Watch(cts.Token)) { } });
        Assert.Null(ex);
    }
}
```

Run: `dotnet test tests/LimitTray.Tests --filter CodexCollectorTests` - Expected: FAIL.

- [ ] **Step 2: Rewrite `CodexCollector` per the algorithm; delete `CodexRolloutReader` and its tests.**

- [ ] **Step 3: Keep the WPF app compiling**

In `src/LimitTray.App/App.xaml.cs` replace the body of `BuildCodexCollector`:

```csharp
private IQuotaCollector BuildCodexCollector()
{
    var binary = CodexBinaryLocator.LocateDefault();
    var reader = new CodexServerReader(
        () => binary is null
            ? throw new InvalidOperationException("codex.exe bulunamadi")
            : new StdioJsonRpcProcess(binary, "app-server"),
        () => DateTimeOffset.Now);
    var tail = new CodexRolloutTail(CodexRolloutTail.DefaultSessionsRoot, () => DateTimeOffset.Now);
    return new CodexCollector(reader.ReadOnceAsync, tail.Poll, () => DateTimeOffset.Now, Task.Delay);
}
```

- [ ] **Step 4: Run the whole suite** - `dotnet build -c Release; dotnet test -c Release`. Expected: green.

- [ ] **Step 5: Commit**

```bash
git add -A src/LimitTray.Core/Codex tests/LimitTray.Tests/Codex src/LimitTray.App/App.xaml.cs
git commit -m "feat(core): sparse Codex collector; app-server only lives during a read"
```

(`git add -A <paths>` with explicit paths: the repo guard forbids a bare `git add -A`.)

---

### Task 6: Per-provider staleness, dirty-only history writes, fixture file

**Files:**
- Modify: `src/LimitTray.Core/Store/QuotaStore.cs`, `src/LimitTray.Core/History/UsageHistory.cs`,
  `src/LimitTray.Core/History/HistoryStore.cs`
- Create: `src/LimitTray.Core/Fixtures/FixtureFile.cs`, `src/LimitTray.Core/Fixtures/FixtureCollector.cs`
- Create: `tools/Footprint/fixtures/normal.json`, `caution.json`, `warning.json`, `stale.json`, `ratelimited.json`
- Test: `tests/LimitTray.Tests/Store/QuotaStoreStalenessTests.cs`,
  `tests/LimitTray.Tests/History/HistoryStoreDirtyTests.cs`, `tests/LimitTray.Tests/Fixtures/FixtureFileTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace LimitTray.Core.Store;
public sealed class QuotaStore
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);   // unchanged default
    public QuotaStore(Func<DateTimeOffset> clock, Func<string, TimeSpan>? staleAfter = null);
    public static TimeSpan StaleAfterFor(string provider, int refreshSeconds);
    // "codex" -> 25 min; otherwise max(5 min, refreshSeconds * 2.5 s)
}
namespace LimitTray.Core.History;
public sealed class UsageHistory { public long Version { get; } }  // +1 on every mutation (Observe that changes state, Import)
public sealed class HistoryStore
{
    public HistoryStore(Func<string?> read, Action<string> write, Func<UsageHistory, string>? serialize = null);
    // Save returns immediately, without serializing, when the same instance is saved at the same Version
}
namespace LimitTray.Core.Fixtures;
public sealed record FixtureData(IReadOnlyList<QuotaSnapshot> Snapshots, IReadOnlyList<UsageSeries> History);
public static class FixtureFile
{
    public const int Version = 1;
    public static FixtureData Read(string json, DateTimeOffset now); // FormatException naming the bad field
}
public sealed class FixtureCollector : IQuotaCollector
{
    public FixtureCollector(string provider, IReadOnlyList<QuotaSnapshot> sequence);
    // Watch yields the sequence in order, then waits until ct; RequestRefresh re-yields the last item
}
```

Fixture format (times are relative so a fixture never expires):

```json
{
  "version": 1,
  "snapshots": [
    { "provider": "claude", "health": "Fresh", "ageSeconds": 0, "detail": null,
      "session": { "percent": 18, "resetsInMinutes": 287, "windowMinutes": 300 },
      "weekly":  { "percent": 30, "resetsInMinutes": 5680, "windowMinutes": 10080 } },
    { "provider": "codex", "health": "Fresh", "ageSeconds": 0, "detail": null,
      "session": { "percent": 18, "resetsInMinutes": 26, "windowMinutes": 300 },
      "weekly":  { "percent": 22, "resetsInMinutes": 7000, "windowMinutes": 10080 } }
  ],
  "history": [
    { "provider": "claude", "kind": "Session", "windowMinutes": 300, "resetsInMinutes": 287,
      "samples": [[60, 12], [45, 14], [30, 15], [15, 17], [0, 18]] }
  ]
}
```

`samples` are `[minutesAgo, percent]`. A provider may appear more than once in `snapshots`;
the entries are applied in order (this is how `ratelimited.json` shows old numbers under a
429: first a Fresh entry with `ageSeconds: 600`, then `{ "health": "RateLimited", "detail":
"HTTP 429" }` without windows). The five fixture files cover: normal (18/30, 18/22),
caution (Claude session 64), warning (Claude session 91, Codex weekly 88), stale (Claude
`Stale`, `ageSeconds: 21600`), ratelimited (as above). Each includes Claude session history
that satisfies the burn-rate thresholds (at least 3 samples over at least 10 minutes).

- [ ] **Step 1: Failing tests**

```csharp
// tests/LimitTray.Tests/Store/QuotaStoreStalenessTests.cs
using LimitTray.Core.Model;
using LimitTray.Core.Store;

namespace LimitTray.Tests.Store;

public class QuotaStoreStalenessTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static QuotaSnapshot Fresh(string p) =>
        new(p, new QuotaWindow(10, null, TimeSpan.FromHours(5)), null, HealthState.Fresh, T0, null);

    [Theory]
    [InlineData("codex", 120, 25 * 60)]
    [InlineData("claude", 60, 5 * 60)]
    [InlineData("claude", 120, 5 * 60)]
    [InlineData("claude", 300, 750)]
    public void StaleAfterFor_MatchesTheSpec(string provider, int refresh, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), QuotaStore.StaleAfterFor(provider, refresh));

    [Fact]
    public void Codex_StaysFreshBetweenSparseReads()
    {
        var now = T0.AddMinutes(20);
        var store = new QuotaStore(() => now, p => QuotaStore.StaleAfterFor(p, 120));
        store.Apply(Fresh("codex"));
        store.RefreshStaleness();
        Assert.Equal(HealthState.Fresh, store.Get("codex")!.Health);

        now = T0.AddMinutes(26);
        store.RefreshStaleness();
        Assert.Equal(HealthState.Stale, store.Get("codex")!.Health);
    }

    [Fact]
    public void DefaultConstructor_KeepsFiveMinutes()
    {
        var now = T0.AddMinutes(6);
        var store = new QuotaStore(() => now);
        store.Apply(Fresh("codex"));
        store.RefreshStaleness();
        Assert.Equal(HealthState.Stale, store.Get("codex")!.Health);
    }
}
```

```csharp
// tests/LimitTray.Tests/History/HistoryStoreDirtyTests.cs
using LimitTray.Core.History;
using LimitTray.Core.Model;

namespace LimitTray.Tests.History;

public class HistoryStoreDirtyTests
{
    [Fact]
    public void Save_Unchanged_DoesNotSerializeAgain()
    {
        var serialized = 0;
        var store = new HistoryStore(() => null, _ => { },
            h => { serialized++; return HistoryFile.Write(h); });
        var history = new UsageHistory();
        history.Observe(new QuotaSnapshot("claude", new QuotaWindow(10, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, DateTimeOffset.UtcNow, null));

        store.Save(history);
        store.Save(history);
        Assert.Equal(1, serialized);

        history.Observe(new QuotaSnapshot("claude", new QuotaWindow(11, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, DateTimeOffset.UtcNow.AddMinutes(2), null));
        store.Save(history);
        Assert.Equal(2, serialized);
    }
}
```

```csharp
// tests/LimitTray.Tests/Fixtures/FixtureFileTests.cs
using LimitTray.Core.Fixtures;
using LimitTray.Core.History;
using LimitTray.Core.Model;

namespace LimitTray.Tests.Fixtures;

public class FixtureFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static string Repo(string rel)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "LimitTray.sln"))) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, rel);
    }

    [Fact]
    public void Normal_ReadsBothProvidersWithRelativeTimes()
    {
        var data = FixtureFile.Read(File.ReadAllText(Repo("tools/Footprint/fixtures/normal.json")), Now);
        var claude = data.Snapshots.Single(s => s.Provider == "claude");
        Assert.Equal(18, claude.Session!.Percent);
        Assert.Equal(Now.AddMinutes(287), claude.Session.ResetsAt);
        Assert.Equal(Now, claude.FetchedAt);
    }

    [Fact]
    public void RateLimited_IsASequenceEndingUnhealthy()
    {
        var data = FixtureFile.Read(File.ReadAllText(Repo("tools/Footprint/fixtures/ratelimited.json")), Now);
        var claude = data.Snapshots.Where(s => s.Provider == "claude").ToList();
        Assert.Equal(HealthState.Fresh, claude[0].Health);
        Assert.Equal(HealthState.RateLimited, claude[^1].Health);
        Assert.Null(claude[^1].Session);
    }

    [Theory]
    [InlineData("normal.json")] [InlineData("caution.json")] [InlineData("warning.json")]
    [InlineData("stale.json")] [InlineData("ratelimited.json")]
    public void EveryFixture_SupportsABurnRateEstimate(string file)
    {
        var data = FixtureFile.Read(File.ReadAllText(Repo("tools/Footprint/fixtures/" + file)), Now);
        var history = new UsageHistory();
        foreach (var s in data.History) history.Import(s);
        Assert.NotNull(history.Estimate("claude", WindowKind.Session, Now));
    }

    [Theory]
    [InlineData("""{"snapshots":[]}""", "version")]
    [InlineData("""{"version":1,"snapshots":[{"provider":"claude","health":"Great"}]}""", "health")]
    [InlineData("""{"version":1,"snapshots":[{"provider":"claude","health":"Fresh","session":{"percent":150}}]}""", "percent")]
    public void BadInput_ThrowsFormatExceptionNamingTheField(string json, string field)
    {
        var ex = Assert.Throws<FormatException>(() => FixtureFile.Read(json, Now));
        Assert.Contains(field, ex.Message);
    }
}
```

Run the three filters - Expected: FAIL.

- [ ] **Step 2: Implement** the store overload, `UsageHistory.Version`, the `HistoryStore`
  skip, `FixtureFile` (`JsonDocument` only, no reflection serializer), `FixtureCollector`, and
  the five fixture files.

- [ ] **Step 3: Run the whole suite** - Expected: green, including the existing tests that
  assert `history.json` holds no Detail and no identifier.

- [ ] **Step 4: Commit**

```bash
git add src/LimitTray.Core/Store src/LimitTray.Core/History src/LimitTray.Core/Fixtures tools/Footprint/fixtures tests/LimitTray.Tests/Store tests/LimitTray.Tests/History tests/LimitTray.Tests/Fixtures
git commit -m "feat(core): per-provider staleness, dirty-only history writes, fixture file"
```

---

### Task 7: Graphics foundation, popup skeleton and the floor gate (Claude)

**Files:**
- Modify: `src/LimitTray.Native/NativeMethods.txt` (D2D, DirectWrite, GDI DIB, layered window APIs)
- Create: `src/LimitTray.Native/Graphics/Surface.cs`, `Graphics/GraphicsFactory.cs`,
  `Graphics/Canvas.cs`, `Popup/PopupWindow.cs`

**Interfaces:**
- Consumes: `MessageWindow`, `UiThread`, `TrayIcon.GetIconRect()` (Task 2).
- Produces:

```csharp
namespace LimitTray.Native.Graphics;
internal sealed class Surface : IDisposable           // 32bpp top-down premultiplied DIB section + memory DC
{ public Surface(int width, int height); public int Width { get; } public int Height { get; } public HDC Dc { get; } }
internal sealed class GraphicsFactory : IDisposable   // ID2D1Factory + IDWriteFactory; created on popup open, released on close
{ public static GraphicsFactory Create(); }
internal sealed class Canvas : IDisposable            // BeginDraw on a Surface; ends with EndDraw in Dispose
{
    public Canvas(GraphicsFactory f, Surface s, float dpiScale);
    public void Clear();
    public void FillRoundedRect(RectF r, float radius, Colour c);
    public void StrokeRoundedRect(RectF r, float radius, Colour c, float width);
    public void FillRoundedRectLinear(RectF r, float radius, Colour from, Colour to, bool vertical);
    public void FillRadial(RectF bounds, PointF centre, float radiusX, float radiusY, Colour inner, Colour outer);
    public void Arc(PointF centre, float radius, float startDeg, float sweepDeg, Colour c, float width, bool roundCaps);
    public void Polyline(ReadOnlySpan<PointF> points, Colour c, float width);
    public SizeF MeasureText(string text, TextStyle style, float maxWidth);
    public void Text(string text, TextStyle style, RectF r, Colour c, TextAlign align);
    public void PushClip(RectF r); public void PopClip();
    public void PushOpacity(float opacity); public void PopOpacity();
    public void Shadow(RectF r, float radius, float blur, Colour c);   // cheap: stacked translucent rounded rects
}
internal readonly record struct Colour(byte R, byte G, byte B, byte A);
internal readonly record struct RectF(float X, float Y, float W, float H);
internal readonly record struct PointF(float X, float Y);
internal readonly record struct SizeF(float W, float H);
internal sealed record TextStyle(string Family, float SizeDip, int Weight, bool Tabular);
internal enum TextAlign { Leading, Centre, Trailing }
namespace LimitTray.Native.Popup;
internal sealed class PopupWindow : IDisposable
{
    public PopupWindow(IPopupContent content);        // content draws and hit-tests; see Task 11
    public bool IsOpen { get; }
    public void Open(RECT? anchor);                   // create window + resources, first frame, show
    public void Close();                              // destroy window and every drawing resource
    public void Invalidate();                         // redraw now via UpdateLayeredWindow (size may change)
}
```

- [ ] **Step 1:** Implement `Surface`, `GraphicsFactory` (`D2D1CreateFactory` single-threaded,
  `DWriteCreateFactory` shared), `Canvas` (`ID2D1DCRenderTarget` with
  `D2D1_RENDER_TARGET_TYPE_SOFTWARE`, `DXGI_FORMAT_B8G8R8A8_UNORM` premultiplied,
  grayscale text antialias), `PopupWindow` (`WS_POPUP`, `WS_EX_LAYERED | WS_EX_TOOLWINDOW |
  WS_EX_TOPMOST`, `UpdateLayeredWindow` with `ULW_ALPHA`, closes on `WM_ACTIVATE(WA_INACTIVE)`
  and `Esc`). Temporary content: a 360x420 dark rounded card with shadow and the text
  "Lim'it" in Segoe UI Variable Text.
- [ ] **Step 2:** Wire tray left click to toggle the popup. If `D2D1CreateFactory`,
  `DWriteCreateFactory` or the render target fails, the popup does not open, the tray keeps
  working and one balloon says so (spec § Hata yönetimi); no exception escapes the loop.
- [ ] **Step 3: Floor gate.** `dotnet publish -c Release -r win-x64`, start the exe, run the
  scratchpad sampler (invariant culture) for 5 min idle, then open/close the popup 3 times
  and sample 2 more minutes. **Stop and report to Özenç if idle Private Bytes > 20 MB.** If
  idle > 15 MB or the post-close delta > 3 MB, build the same skeleton with GDI+ flat API
  and compare before continuing. Record the numbers in the plan's measurement log below.
- [ ] **Step 4:** Screenshot the skeleton popup with a DPI-aware capture: corners antialiased,
  shadow visible, no dark fringe, nothing drawn outside the card.
- [ ] **Step 5: Commit** `feat(native): software Direct2D surface and layered popup skeleton`

---

### Task 8: Host wiring: collectors, stores, settings, tray menu, platform services

**Files:**
- Create: `src/LimitTray.Native/Host/AppHost.cs`, `Host/CommandLine.cs`, `Host/TrayUpdater.cs`,
  `Host/Timers.cs`, `Tray/TrayMenu.cs`, `Platform/StartupRegistration.cs`,
  `Platform/TerminalLauncher.cs`, `Platform/SystemTheme.cs`, `Platform/Browser.cs`,
  `Popup/IPopupContent.cs`, `Graphics/ITrayIconPainter.cs`, `Graphics/PlaceholderTrayIconPainter.cs`
- Modify: `Program.cs` (delegate to `AppHost`)
- Test: `tests/LimitTray.Native.Tests/CommandLineTests.cs`, `TrayUpdaterTests.cs`

**Interfaces:**
- Consumes: Core (`QuotaStore`, `HistoryStore`, `SettingsStore`, `QuotaAlerts`, `UsageHistory`,
  `TrayIconModelBuilder`, `QuotaFormatter.Tooltip`, `Strings`, `LanguageArguments`,
  `ClaudeCollector`, `ClaudeCredentialReader`, `SystemHttpTransport`, `CodexCollector`,
  `CodexServerReader`, `CodexRolloutTail`, `CodexBinaryLocator`, `StdioJsonRpcProcess`,
  `FixtureFile`, `FixtureCollector`), Task 2 host types, Task 7 `PopupWindow`.
- Produces:

```csharp
namespace LimitTray.Native.Host;
internal sealed record CommandLine(string? FixturePath, string DataDirectory, IReadOnlyList<string> Raw, bool Show)
{ public static CommandLine Parse(IReadOnlyList<string> args, string defaultDataDirectory); }
internal sealed class TrayUpdater   // redraw only on change
{ public bool ShouldRedraw(TrayIconModel next, int iconSizePx, bool dark); }
internal interface IAppActions     // what the popup may ask the host to do
{
    AppSettings Settings { get; }
    Strings Strings { get; }
    UsageHistory History { get; }
    IReadOnlyList<QuotaSnapshot> Snapshots { get; }
    bool SettingsSaveFailed { get; }
    bool StartupEnabled { get; }
    void ApplySettings(AppSettings next);
    void RememberExpanded(IReadOnlySet<string> expanded);
    void RefreshNow();                                 // 5 s rate limit, both collectors
    void SetStartup(bool enabled);
    bool OpenTerminal(string provider);                // false when neither wt nor powershell starts
    void OpenRepository();
}
namespace LimitTray.Native.Popup;
internal interface IPopupContent
{
    SizeF Measure(float dpiScale);                      // content size in DIPs
    void Draw(Graphics.Canvas canvas, float dpiScale);
    bool OnMouse(MouseEvent e);                         // true when a redraw is needed
    bool OnKey(int virtualKey);
    bool IsAnimating { get; }
    void Tick(TimeSpan now);                            // animation step
}
internal readonly record struct MouseEvent(MouseKind Kind, float X, float Y);
internal enum MouseKind { Move, Leave, Down, Up, Wheel }
namespace LimitTray.Native.Graphics;
internal interface ITrayIconPainter { HICON Paint(TrayIconModel model, int sizePx, bool darkTaskbar); }
```

Behaviour: a straight port of `src/LimitTray.App/App.xaml.cs` (read it first) without WPF:
settings/strings/alerts/history load; seed from history; Claude collector with
`SystemHttpTransport`; Codex collector built from `CodexServerReader` + `CodexRolloutTail`;
fixture mode (`--fixture`) replaces both collectors with `FixtureCollector`s and imports the
fixture history; `QuotaStore` with `StaleAfterFor(provider, settings.RefreshSeconds)`;
snapshots marshalled with `UiThread.Post`; history saved 10 s after the last change
(one-shot `SetTimer`) and on exit; staleness every 60 s; tray icon redrawn only when
`TrayUpdater.ShouldRedraw`; tooltip from `QuotaFormatter.Tooltip` (127 chars); balloons from
`QuotaAlerts` when `settings.Notifications`; menu: Windows ile başlat (checked state read
back from the registry), Ayarlar, Çıkış; popup open asks Codex for `RequestRefresh` when its
snapshot is older than 2 minutes; `StartupRegistration` rewrites the Run value to the
current exe path when it is enabled but points elsewhere (upgrade from v0.3.x).
`PlaceholderTrayIconPainter` returns a copy of the app icon (Task 10 replaces it).

- [ ] **Step 1: Failing tests**

```csharp
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using LimitTray.Native.Host;

namespace LimitTray.Native.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_FixtureDataDirAndShow()
    {
        var cl = CommandLine.Parse(new[] { "--fixture", "f.json", "--data-dir", @"C:\t", "--show", "--lang=en" }, @"C:\d");
        Assert.Equal("f.json", cl.FixturePath);
        Assert.Equal(@"C:\t", cl.DataDirectory);
        Assert.True(cl.Show);
        Assert.Contains("--lang=en", cl.Raw);
    }

    [Fact]
    public void Parse_Defaults() =>
        Assert.Equal(@"C:\d", CommandLine.Parse(Array.Empty<string>(), @"C:\d").DataDirectory);
}

public class TrayUpdaterTests
{
    private static TrayIconModel Model(double p) =>
        new(TrayIconStyle.Ring, new TrayBar(p, new Rgb(1, 2, 3), 255), null, null, false);

    [Fact]
    public void SameModel_DoesNotRedraw_ChangedModelOrSizeDoes()
    {
        var u = new TrayUpdater();
        Assert.True(u.ShouldRedraw(Model(10), 16, true));
        Assert.False(u.ShouldRedraw(Model(10), 16, true));
        Assert.True(u.ShouldRedraw(Model(11), 16, true));
        Assert.True(u.ShouldRedraw(Model(11), 24, true));
        Assert.True(u.ShouldRedraw(Model(11), 24, false));
    }
}
```

- [ ] **Step 2: Implement.** Port behaviour, keep every comment that explains a past defect.
- [ ] **Step 3: Verify**

- `dotnet test` (both test projects) green; `dotnet publish` of Native with 0 warnings.
- Fixture run: `LimitTray.exe --fixture tools\Footprint\fixtures\warning.json --data-dir $env:TEMP\lt-fx`:
  tooltip shows both providers; one warning balloon; menu items work; Exit removes the icon.
- Real run (once, not repeatedly: `/api/oauth/usage` rate limits): both providers reach the
  tooltip within 60 s; `Get-Process codex -ErrorAction SilentlyContinue` returns nothing
  between Codex reads (sample for 3 minutes).
- [ ] **Step 4: Commit** `feat(native): host wiring, tray menu and platform services`

---

### Task 9: Measurement harness and popup probe

**Files:**
- Create: `tools/Footprint/Measure-Footprint.ps1`, `tools/Footprint/README.md`
- Modify: `tools/UiProbe/Probe-Popup.ps1` (native popup)
- Modify: `src/LimitTray.Native/Host/AppHost.cs` (probe hook)

**Interfaces:**
- Consumes: `MessageWindow.ClassName`, fixture files.
- Produces: `Measure-Footprint.ps1 -Exe <path> -Scenario Idle|Cycle|Open [-Fixture <json>]
  [-WarmupSeconds 60] [-Seconds 300] [-Out <csv>] [-MaxIdleMB 20] [-MaxCycleDeltaMB 3]`;
  exit code 1 when a threshold is violated. Probe hook: the host registers
  `RegisterWindowMessage("LimitTray.Probe")`; `wParam` 1 = toggle popup, 2 = open settings.
  Only the hidden host window receives it; it does nothing else.

- [ ] **Step 1:** Harness: starts the exe with `--fixture` and a fresh temp `--data-dir`, waits
  for warm-up, samples every 10 s `PrivateMemorySize64`, `WorkingSet64`, `HandleCount`,
  thread count and CPU delta; writes CSV with `[CultureInfo]::InvariantCulture`; for `Cycle`
  calls the probe (open, click first card, click it again, open settings, back, close) and
  keeps sampling 2 minutes; prints median/max per phase; kills the process at the end.
  A v0.3.4 run is supported without `-Fixture` (real data, run once).
- [ ] **Step 2:** Probe: finds `LimitTray.Host`, posts the probe message, locates the popup
  window by class, clicks at positions given in DIPs relative to the popup rectangle
  (constants at the top of the script, matching Task 11's layout), captures DPI-aware PNGs
  at 40 ms and settled, asserts the process is alive after each step.
- [ ] **Step 3:** Run both against the Task 8 build; attach the CSV summary to the report.
- [ ] **Step 4: Commit** `feat(tools): footprint harness and native popup probe`

---

### Task 10: Tray icon painter (Claude)

**Files:** Create `src/LimitTray.Native/Graphics/TrayIconPainter.cs`; modify `AppHost` to use it.
**Reference:** `src/LimitTray.App/TrayIconRenderer.cs`, `RenderedIcon.cs`, spec § Tray ikonu (v0.3).

- [ ] Paint Ring / Number / DualBar from `TrayIconModel` onto a `Surface` of
  `GetSystemMetricsForDpi(SM_CXSMICON, dpi)` pixels, convert with `CreateIconIndirect`
  (32bpp ARGB colour bitmap + empty mask), release every drawing resource after painting.
  Null bar draws the question mark; opacity from the model.
- [ ] Compare with v0.3.4 at 100% (16 px) and, when available, 150% (24 px): capture both
  tray areas; the three styles must match in geometry and colour.
- [ ] Commit `feat(native): tray icon painter`

---

### Task 11: Palette, layout, hit testing, animator (Claude)

**Files:** Create `src/LimitTray.Native/Ui/Palette.cs`, `Ui/Layout.cs`, `Ui/HitTest.cs`,
`Ui/Animator.cs`; tests `tests/LimitTray.Native.Tests/LayoutTests.cs`, `AnimatorTests.cs`.
**Reference:** `src/LimitTray.App/Themes/Dark.xaml`, `Light.xaml`, `Brushes.cs`,
`Views/PanelPage.xaml`, `Views/SettingsPage.xaml`, `QuotaPopup.xaml`.

- [ ] Palette: every brush and size token from the two themes, colours for quota states only
  via `Theme.ColourFor` / `OpacityFor`.
- [ ] Layout (pure): header, card (collapsed/expanded, healthy/unhealthy), rows, footer,
  settings groups; returns rectangles in DIPs; popup height follows content.
- [ ] Tests: collapsed two-card panel height equals the v0.3.4 value measured from
  `docs/screenshot.png` geometry; expanding a card grows the height by the expanded block;
  hit test returns the card for a point inside it and the gear for its rectangle; popup
  anchor for taskbar at bottom, top, left and right (Review Focus 5).
- [ ] Animator: ease-out cubic, 300 ms value tweens, 150 ms page slide, `IsAnimating` false
  after the last tween ends; tests with a fake time source.
- [ ] Commit `feat(native): layout, hit testing and animator`

---

### Task 12: Panel page (Claude)

**Files:** Create `src/LimitTray.Native/Ui/PanelPage.cs`, `Ui/PopupContent.cs`
(implements `IPopupContent`, owns page switching).
**Reference:** `Views/PanelPage.xaml(.cs)`, `ViewModels/PanelViewModel.cs`,
`ProviderCardViewModel.cs`, `WindowRowViewModel.cs`, `docs/screenshot.png`.

- [ ] Header (LIM'IT, refresh with one-turn spin and 5 s guard, gear), cards (ring with
  animated sweep, name, nearest reset, rows with animated bars and tabular percent,
  expanded block with sparkline, burn-rate line and "last updated"), hover border and
  terminal button, stale badge with real age, error text under old numbers, `?` for unknown
  side, footer (updated text, version). Radial brand glow background.
- [ ] Age texts refresh on minute boundaries only while open.
- [ ] Verify with the probe against each fixture; side-by-side with v0.3.4 captures.
- [ ] Commit `feat(native): panel page`

---

### Task 13: Settings page (Claude)

**Files:** Create `src/LimitTray.Native/Ui/SettingsPage.cs`, `Ui/Controls.cs` (segmented,
toggle, dropdown overlay drawn inside the popup surface).
**Reference:** `Views/SettingsPage.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`,
`docs/screenshot-settings.png`.

- [ ] All ten settings, instant apply through `IAppActions.ApplySettings`, save-failed line,
  back link, version + GitHub link, 150 ms slide both ways, keyboard: Esc closes the
  dropdown first, then the popup.
- [ ] Verify: each setting changes what it should (theme, language, tray style/source,
  thresholds recolour, interval takes effect), `settings.json` content equals what
  `SettingsFile.Write` produces.
- [ ] Commit `feat(native): settings page`

---

### Task 14: Parity check and measurement gates

- [ ] **Parity:** for each fixture capture the native popup collapsed and expanded, settings,
  light theme; capture v0.3.4 in the same states where reachable; review side by side.
  Özenç's eye is the final acceptance.
- [ ] **Matrix:** taskbar bottom (default); DPI 100%, and 150% if Özenç can switch it
  (otherwise ABSTAIN in the report); popup opened on each monitor.
- [ ] **Gates** (each one measured with `Measure-Footprint.ps1 -Scenario Idle` and `Cycle`,
  three runs, median): `System.GC.ConserveMemory=5`, `InvariantGlobalization=true`,
  `OptimizationPreference=Size`, `UseSystemResourceKeys=true`, WinHTTP transport. Keep a knob
  only if it lowers Private Bytes by at least 0.5 MB and tests + parity captures still pass.
- [ ] Record before/after in the measurement log below and in README.
- [ ] Commit `perf(native): adopt measured runtime settings`

---

### Task 15: Switch-over (Codex)

**Files:** Modify `.github/workflows/release.yml`, `ci.yml`, `LimitTray.sln`; delete
`src/LimitTray.App/`.

- [ ] `release.yml` Publish step:

```yaml
      - name: Publish
        shell: pwsh
        run: |
          dotnet publish src/LimitTray.Native `
            --configuration Release `
            --runtime win-x64 `
            -p:Version=${{ steps.version.outputs.version }} `
            --output publish/release
```

  and rename `publish/release/LimitTray.exe` in the next step; checksum and `gh` steps unchanged.
- [ ] `ci.yml`: build and test the solution (both test projects), keep `no-em-dash`, add a
  `dotnet publish src/LimitTray.Native -c Release -r win-x64` step so AOT warnings fail CI.
- [ ] Remove `src/LimitTray.App` and its sln entry; `git grep -n "LimitTray.App"` returns only
  historical docs.
- [ ] Verify locally: `dotnet build -c Release`, `dotnet test -c Release`, the publish command,
  `Test-NoEmDash.ps1`.
- [ ] Commit `build: ship the NativeAOT app; remove the WPF app`

---

### Task 16: Documentation (Claude)

- [ ] `README.md`: stack line, measured footprint table (v0.3.4 vs v0.4.0), install size,
  data sources including the rollout tail, privacy paragraph.
- [ ] `SECURITY.md`: files read (credentials, `~/.codex/sessions` tails: last 64 KB, only the
  rate-limit block parsed, nothing stored), files written (`history.json`, `settings.json`),
  registry (HKCU Run when chosen). Apply the pending privacy patch content.
- [ ] `AGENTS.md`: replace WPF rules (XAML binding, fixed 392x700 window) with the native
  rules (layered window sized only through `UpdateLayeredWindow`; popup resources die on
  close; tray icon redrawn only on model change; no DWM attributes on the layered window;
  NuGet exception for CsWin32; memory is Private Bytes; measure with the harness).
- [ ] Spec `status: implemented`; this plan `status: done`.
- [ ] `Test-NoEmDash.ps1` clean. Commit `docs: v0.4 native AOT`

---

### Task 17: Live run, independent review, merge

- [ ] Real providers, once: both deliver; manual refresh; terminal button; startup toggle;
  Explorer restart; Codex used in a terminal moves the Codex number within 30 s.
- [ ] Final harness run on the publish exe (Idle + Cycle), numbers into README.
- [ ] Fresh-context read-only Codex verifier over `main..native-aot` with the spec: verdict
  PASS/REVISE with evidence; fix and re-run until PASS.
- [ ] Merge `native-aot` into `main`, push. Ask Özenç before pushing tag `v0.4.0`.

---

## Measurement log

| Build | Scenario | Private Bytes (median / max) | CPU | Note |
|---|---|---|---|---|
| v0.3.4 Release (framework-dependent) | idle, popup never opened | 180 / 180 MB | ~0.05% | 2026-09-27 01:38-01:41 |
| v0.3.4 Release (framework-dependent) | after one popup event | 252 / 255 MB | - | 2026-09-27 01:42-01:46, did not return |
