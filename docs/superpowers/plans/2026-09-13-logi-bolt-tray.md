# Logi Bolt Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows tray app that shows battery percentage for each device connected through a Logitech Bolt receiver, with a details/settings window per the approved design spec.

**Architecture:** Protocol logic (HID++ 2.0 framing, feature discovery, battery/device-name parsing) and pure app logic (settings persistence, icon color rules, low-battery notification state machine, poll interval clamping) live in plain `net8.0` class libraries with no Windows-only APIs, so they build and unit-test on any OS. All Windows-only surface (WPF window, WinForms `NotifyIcon`, `System.Drawing` bitmap rendering, registry autostart) lives in one `net8.0-windows` app project that only builds on Windows.

**Tech Stack:** .NET 8, C#, WPF, WinForms `NotifyIcon`, [HidApi.Net](https://www.nuget.org/packages/HidApi.Net) 1.2.0 (MIT, native hidapi bindings for win/linux/osx) for raw HID I/O, xUnit for tests, `System.Text.Json` for settings.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-09-13-logi-bolt-tray-design.md` — every requirement in it must be covered by a task below.
- `LogiBoltTray.Protocol`, `LogiBoltTray.Hid`, `LogiBoltTray.Core` target `net8.0` (plain) — must build and test with `dotnet build` / `dotnet test` on macOS (verified: .NET 8.0.131 SDK installed via `brew install dotnet@8`, confirmed working in this environment).
- `LogiBoltTray.App` targets `net8.0-windows` with `UseWPF` and `UseWindowsForms` both enabled — **cannot be built on macOS**. Every task that touches this project ends with a manual verification checklist for the user to run on their Windows PC (`dotnet build` / `dotnet run` there), not an automated test step here.
- No new external dependencies beyond `HidApi.Net` and (for tests) `xunit` / `Microsoft.NET.Test.Sdk` / `xunit.runner.visualstudio` without discussing first.
- Logitech vendor ID: `0x046D`. Known Bolt receiver product ID: `0xC548` (most commonly documented Bolt receiver PID — may need additional PIDs added once the user runs the diagnostic in Task 8 against their real receiver).
- Icon color thresholds (apply everywhere a color is chosen): `< 20%` red, `20–60%` orange, `> 60%` green, `= 100%` purple.
- Defaults: poll interval 60s (clamped range 15s–30min), icon style B, low-battery notification threshold 20%, all devices visible by default, autostart off by default.
- Settings file: `%AppData%\LogiBoltTray\config.json`, but `SettingsStore` must take the base directory as a constructor parameter (not hardcode `Environment.SpecialFolder`) so it's unit-testable with a temp directory.

---

## Task 1: Solution scaffold

**Files:**
- Create: `LogiBoltTray.sln`
- Create: `src/LogiBoltTray.Protocol/LogiBoltTray.Protocol.csproj`
- Create: `src/LogiBoltTray.Hid/LogiBoltTray.Hid.csproj`
- Create: `src/LogiBoltTray.Core/LogiBoltTray.Core.csproj`
- Create: `src/LogiBoltTray.App/LogiBoltTray.App.csproj`
- Create: `tests/LogiBoltTray.Protocol.Tests/LogiBoltTray.Protocol.Tests.csproj`
- Create: `tests/LogiBoltTray.Core.Tests/LogiBoltTray.Core.Tests.csproj`
- Create: `.gitignore` (merge with existing one at repo root if present)

**Interfaces:**
- Produces: solution structure and project references that every later task builds on.

- [ ] **Step 1: Create the plain (cross-platform-buildable) class library projects**

```bash
mkdir -p src tests
dotnet new classlib -n LogiBoltTray.Protocol -o src/LogiBoltTray.Protocol
dotnet new classlib -n LogiBoltTray.Hid -o src/LogiBoltTray.Hid
dotnet new classlib -n LogiBoltTray.Core -o src/LogiBoltTray.Core
rm src/LogiBoltTray.Protocol/Class1.cs src/LogiBoltTray.Hid/Class1.cs src/LogiBoltTray.Core/Class1.cs
```

- [ ] **Step 2: Create the Windows-only app project**

```bash
dotnet new wpf -n LogiBoltTray.App -o src/LogiBoltTray.App
```

Edit `src/LogiBoltTray.App/LogiBoltTray.App.csproj` so the `<PropertyGroup>` reads:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <UseWPF>true</UseWPF>
  <UseWindowsForms>true</UseWindowsForms>
  <AssemblyName>LogiBoltTray</AssemblyName>
  <ApplicationIcon>app.ico</ApplicationIcon>
</PropertyGroup>
```

Note: `app.ico` doesn't exist yet — remove the `<ApplicationIcon>` line for now; it gets added back in Task 16 when the tray icon asset exists. Skip it in this step.

- [ ] **Step 3: Create test projects and reference their subjects**

```bash
dotnet new xunit -n LogiBoltTray.Protocol.Tests -o tests/LogiBoltTray.Protocol.Tests
dotnet new xunit -n LogiBoltTray.Core.Tests -o tests/LogiBoltTray.Core.Tests
dotnet add tests/LogiBoltTray.Protocol.Tests reference src/LogiBoltTray.Protocol
dotnet add tests/LogiBoltTray.Core.Tests reference src/LogiBoltTray.Core
```

- [ ] **Step 4: Wire up project references between src projects**

```bash
dotnet add src/LogiBoltTray.Hid reference src/LogiBoltTray.Protocol
dotnet add src/LogiBoltTray.App reference src/LogiBoltTray.Protocol
dotnet add src/LogiBoltTray.App reference src/LogiBoltTray.Hid
dotnet add src/LogiBoltTray.App reference src/LogiBoltTray.Core
dotnet add src/LogiBoltTray.Hid package HidApi.Net --version 1.2.0
```

- [ ] **Step 5: Create the solution file and add every project**

```bash
dotnet new sln -n LogiBoltTray
dotnet sln add src/LogiBoltTray.Protocol src/LogiBoltTray.Hid src/LogiBoltTray.Core src/LogiBoltTray.App tests/LogiBoltTray.Protocol.Tests tests/LogiBoltTray.Core.Tests
```

- [ ] **Step 6: Build the cross-platform-buildable projects (this works on macOS)**

Run: `dotnet build src/LogiBoltTray.Protocol src/LogiBoltTray.Hid src/LogiBoltTray.Core tests/LogiBoltTray.Protocol.Tests tests/LogiBoltTray.Core.Tests`
Expected: all five build with 0 errors (the `App` project is excluded from this command on purpose — it can't build here).

- [ ] **Step 7: Confirm the `.gitignore` covers build output**

Ensure repo-root `.gitignore` contains at least:

```
bin/
obj/
.superpowers/
```

(It already does from the brainstorming step — just confirm, don't duplicate.)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Scaffold LogiBoltTray solution with cross-platform and Windows-only projects"
```

---

## Task 2: HID++ 2.0 report framing

**Files:**
- Create: `src/LogiBoltTray.Protocol/HidppFrame.cs`
- Test: `tests/LogiBoltTray.Protocol.Tests/HidppFrameTests.cs`

**Interfaces:**
- Produces: `HidppFrame` (readonly struct) with `ReportId` (byte), `DeviceIndex` (byte), `FeatureIndex` (byte), `FunctionId` (byte, 0-15), `SoftwareId` (byte, 0-15), `Params` (`byte[]`), `IsError` (bool), `ErrorCode` (byte?). Static `HidppFrame.Short(deviceIndex, featureIndex, functionId, softwareId, params0, params1, params2)` and `HidppFrame.Long(deviceIndex, featureIndex, functionId, softwareId, byte[] params16)`. Instance `byte[] ToBytes()`. Static `HidppFrame Parse(ReadOnlySpan<byte> raw)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class HidppFrameTests
{
    [Fact]
    public void Short_BuildsSevenByteReportWithHeaderAndParams()
    {
        var frame = HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x02, functionId: 0x0A, softwareId: 0x3, p0: 0x11, p1: 0x22, p2: 0x33);

        byte[] bytes = frame.ToBytes();

        Assert.Equal(new byte[] { 0x10, 0x01, 0x02, 0xA3, 0x11, 0x22, 0x33 }, bytes);
    }

    [Fact]
    public void Long_BuildsTwentyByteReportWithSixteenByteParams()
    {
        var p = new byte[16];
        p[0] = 0xAA;
        p[15] = 0xBB;
        var frame = HidppFrame.Long(deviceIndex: 0x02, featureIndex: 0x05, functionId: 0x1, softwareId: 0x2, p);

        byte[] bytes = frame.ToBytes();

        Assert.Equal(20, bytes.Length);
        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0x02, bytes[1]);
        Assert.Equal(0x05, bytes[2]);
        Assert.Equal(0x12, bytes[3]);
        Assert.Equal(0xAA, bytes[4]);
        Assert.Equal(0xBB, bytes[19]);
    }

    [Fact]
    public void Parse_NormalShortResponse_IsNotError()
    {
        byte[] raw = { 0x10, 0x01, 0x02, 0xA3, 0x11, 0x22, 0x33 };

        var frame = HidppFrame.Parse(raw);

        Assert.False(frame.IsError);
        Assert.Equal(0x01, frame.DeviceIndex);
        Assert.Equal(0x02, frame.FeatureIndex);
        Assert.Equal(0x0A, frame.FunctionId);
        Assert.Equal(0x3, frame.SoftwareId);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, frame.Params);
    }

    [Fact]
    public void Parse_ErrorResponse_SetsIsErrorAndErrorCode()
    {
        // [reportId, deviceIndex, 0xFF (error marker), addressedFeatureIndex, functionId_softwareId, errorCode, pad]
        byte[] raw = { 0x10, 0x01, 0xFF, 0x02, 0xA3, 0x05, 0x00 };

        var frame = HidppFrame.Parse(raw);

        Assert.True(frame.IsError);
        Assert.Equal((byte)0x05, frame.ErrorCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter HidppFrameTests`
Expected: FAIL to compile — `HidppFrame` does not exist yet.

- [ ] **Step 3: Implement `HidppFrame`**

```csharp
namespace LogiBoltTray.Protocol;

public readonly struct HidppFrame
{
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    private const byte ErrorFeatureMarker = 0xFF;

    public byte ReportId { get; }
    public byte DeviceIndex { get; }
    public byte FeatureIndex { get; }
    public byte FunctionId { get; }
    public byte SoftwareId { get; }
    public byte[] Params { get; }

    public bool IsError => FeatureIndex == ErrorFeatureMarker;
    public byte? ErrorCode => IsError ? Params[0] : null;

    private HidppFrame(byte reportId, byte deviceIndex, byte featureIndex, byte functionId, byte softwareId, byte[] paramBytes)
    {
        ReportId = reportId;
        DeviceIndex = deviceIndex;
        FeatureIndex = featureIndex;
        FunctionId = functionId;
        SoftwareId = softwareId;
        Params = paramBytes;
    }

    public static HidppFrame Short(byte deviceIndex, byte featureIndex, byte functionId, byte softwareId, byte p0 = 0, byte p1 = 0, byte p2 = 0)
        => new(ShortReportId, deviceIndex, featureIndex, functionId, softwareId, new[] { p0, p1, p2 });

    public static HidppFrame Long(byte deviceIndex, byte featureIndex, byte functionId, byte softwareId, byte[] params16)
    {
        if (params16.Length != 16)
        {
            throw new ArgumentException("Long HID++ reports carry exactly 16 parameter bytes.", nameof(params16));
        }

        return new HidppFrame(LongReportId, deviceIndex, featureIndex, functionId, softwareId, params16);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[4 + Params.Length];
        bytes[0] = ReportId;
        bytes[1] = DeviceIndex;
        bytes[2] = FeatureIndex;
        bytes[3] = (byte)((FunctionId << 4) | (SoftwareId & 0x0F));
        Array.Copy(Params, 0, bytes, 4, Params.Length);
        return bytes;
    }

    public static HidppFrame Parse(ReadOnlySpan<byte> raw)
    {
        byte reportId = raw[0];
        byte deviceIndex = raw[1];
        byte featureIndex = raw[2];
        byte functionAndSoftwareId = raw[3];
        byte functionId = (byte)(functionAndSoftwareId >> 4);
        byte softwareId = (byte)(functionAndSoftwareId & 0x0F);
        byte[] paramBytes = raw[4..].ToArray();
        return new HidppFrame(reportId, deviceIndex, featureIndex, functionId, softwareId, paramBytes);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter HidppFrameTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.Protocol/HidppFrame.cs tests/LogiBoltTray.Protocol.Tests/HidppFrameTests.cs
git commit -m "Add HID++ 2.0 report framing (HidppFrame)"
```

---

## Task 3: Transport abstraction + fake for testing

**Files:**
- Create: `src/LogiBoltTray.Protocol/IHidppTransport.cs`
- Create: `tests/LogiBoltTray.Protocol.Tests/FakeHidppTransport.cs`

**Interfaces:**
- Consumes: `HidppFrame` (Task 2).
- Produces: `IHidppTransport` with `HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout)` — every later protocol client (root feature, battery, device name) depends only on this interface, never on `HidApi.Net` directly. Returns `null` if no matching response arrives before `timeout`. `FakeHidppTransport` (test double) with a queued-response API: `EnqueueResponse(HidppFrame response)` and a recorded `IReadOnlyList<HidppFrame> SentRequests` list, used by every later protocol test.

- [ ] **Step 1: Write the interface**

```csharp
namespace LogiBoltTray.Protocol;

public interface IHidppTransport
{
    /// <summary>
    /// Sends <paramref name="request"/> and waits up to <paramref name="timeout"/> for the
    /// matching response (same DeviceIndex/FeatureIndex/FunctionId/SoftwareId, or an error frame
    /// addressed to the same FeatureIndex/FunctionId/SoftwareId). Unrelated notification frames
    /// received in the meantime are discarded. Returns null on timeout.
    /// </summary>
    HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout);
}
```

- [ ] **Step 2: Write the fake transport used by every later test**

```csharp
namespace LogiBoltTray.Protocol.Tests;

using LogiBoltTray.Protocol;

public sealed class FakeHidppTransport : IHidppTransport
{
    private readonly Queue<HidppFrame> _responses = new();

    public List<HidppFrame> SentRequests { get; } = new();

    public void EnqueueResponse(HidppFrame response) => _responses.Enqueue(response);

    public HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout)
    {
        SentRequests.Add(request);
        return _responses.Count > 0 ? _responses.Dequeue() : null;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build tests/LogiBoltTray.Protocol.Tests`
Expected: builds with 0 errors (no assertions yet — this task has no independent behavior to test, it's pure scaffolding consumed by Task 4 onward).

- [ ] **Step 4: Commit**

```bash
git add src/LogiBoltTray.Protocol/IHidppTransport.cs tests/LogiBoltTray.Protocol.Tests/FakeHidppTransport.cs
git commit -m "Add IHidppTransport abstraction and fake transport for tests"
```

---

## Task 4: Root feature discovery client

**Files:**
- Create: `src/LogiBoltTray.Protocol/RootFeatureClient.cs`
- Test: `tests/LogiBoltTray.Protocol.Tests/RootFeatureClientTests.cs`

**Interfaces:**
- Consumes: `IHidppTransport`, `HidppFrame` (Task 2, 3).
- Produces: `RootFeatureClient` with `ctor(IHidppTransport transport, byte deviceIndex)` and `byte? FindFeatureIndex(ushort featureId)` — returns the feature's index on this device, or `null` if the device reports an error (feature not supported) or times out. Consumed by Task 6 (`BatteryReader`) and Task 7 (`DeviceNameClient`).

- [ ] **Step 1: Write the failing tests**

```csharp
using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class RootFeatureClientTests
{
    [Fact]
    public void FindFeatureIndex_SendsGetFeatureRequestToRootFeatureIndexZero()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x10, p1: 0x00, p2: 0x03));
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        byte? index = client.FindFeatureIndex(featureId: 0x1000);

        Assert.Equal((byte)0x10, index);
        var sent = Assert.Single(transport.SentRequests);
        Assert.Equal(0x00, sent.FeatureIndex); // root feature is always index 0
        Assert.Equal(0x00, sent.FunctionId);   // GetFeature is function 0 on IRoot
        Assert.Equal(0x10, sent.Params[0]);    // featureId high byte
        Assert.Equal(0x00, sent.Params[1]);    // featureId low byte
    }

    [Fact]
    public void FindFeatureIndex_UnsupportedFeature_ReturnsNull()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x00, p1: 0x00, p2: 0x00));
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        byte? index = client.FindFeatureIndex(featureId: 0x1000);

        Assert.Null(index); // featureIndex 0x00 in the response means "not found"
    }

    [Fact]
    public void FindFeatureIndex_NoResponse_ReturnsNull()
    {
        var transport = new FakeHidppTransport(); // no response enqueued
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        byte? index = client.FindFeatureIndex(featureId: 0x1000);

        Assert.Null(index);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter RootFeatureClientTests`
Expected: FAIL to compile — `RootFeatureClient` does not exist yet.

- [ ] **Step 3: Implement `RootFeatureClient`**

```csharp
namespace LogiBoltTray.Protocol;

public sealed class RootFeatureClient
{
    private const byte RootFeatureIndex = 0x00;
    private const byte GetFeatureFunctionId = 0x00;
    private const byte SoftwareId = 0x1;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidppTransport _transport;
    private readonly byte _deviceIndex;

    public RootFeatureClient(IHidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        _deviceIndex = deviceIndex;
    }

    public byte? FindFeatureIndex(ushort featureId)
    {
        byte high = (byte)(featureId >> 8);
        byte low = (byte)(featureId & 0xFF);
        var request = HidppFrame.Short(_deviceIndex, RootFeatureIndex, GetFeatureFunctionId, SoftwareId, high, low, 0);

        HidppFrame? response = _transport.SendAndReceive(request, DefaultTimeout);
        if (response is null || response.Value.IsError)
        {
            return null;
        }

        byte featureIndex = response.Value.Params[0];
        return featureIndex == 0x00 ? null : featureIndex;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter RootFeatureClientTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.Protocol/RootFeatureClient.cs tests/LogiBoltTray.Protocol.Tests/RootFeatureClientTests.cs
git commit -m "Add HID++ root feature discovery client"
```

---

## Task 5: Battery voltage-to-percentage table

**Files:**
- Create: `src/LogiBoltTray.Protocol/BatteryVoltageTable.cs`
- Test: `tests/LogiBoltTray.Protocol.Tests/BatteryVoltageTableTests.cs`

**Interfaces:**
- Produces: `static class BatteryVoltageTable` with `static int ToPercent(int millivolts)`. Consumed by Task 6 (`BatteryReader`, as the fallback when only feature `0x1001` is available).

- [ ] **Step 1: Write the failing tests**

```csharp
using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class BatteryVoltageTableTests
{
    [Theory]
    [InlineData(4200, 100)]
    [InlineData(3500, 0)]
    [InlineData(4500, 100)] // above table max clamps to 100
    [InlineData(3000, 0)]   // below table min clamps to 0
    public void ToPercent_KnownAnchorPoints_ReturnsExactValue(int millivolts, int expectedPercent)
    {
        Assert.Equal(expectedPercent, BatteryVoltageTable.ToPercent(millivolts));
    }

    [Fact]
    public void ToPercent_BetweenAnchorPoints_InterpolatesLinearly()
    {
        // Table has anchors at 4186mV->100% and 4067mV->90%; midpoint should be ~95%.
        int midpoint = (4186 + 4067) / 2;

        int percent = BatteryVoltageTable.ToPercent(midpoint);

        Assert.InRange(percent, 93, 97);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter BatteryVoltageTableTests`
Expected: FAIL to compile — `BatteryVoltageTable` does not exist yet.

- [ ] **Step 3: Implement `BatteryVoltageTable`**

```csharp
namespace LogiBoltTray.Protocol;

/// <summary>
/// Approximate Li-Po discharge curve used by Logitech HID++ devices that only expose raw
/// voltage (feature 0x1001) instead of a computed percentage. Anchor points are widely
/// published approximations for a single-cell 3.7V Li-Po; exact shape may need tuning per
/// real device once tested on hardware (see spec's "outside MVP" iteration note).
/// </summary>
public static class BatteryVoltageTable
{
    private static readonly (int Millivolts, int Percent)[] Anchors =
    {
        (4200, 100),
        (4186, 100),
        (4067, 90),
        (3989, 80),
        (3922, 70),
        (3859, 60),
        (3811, 50),
        (3778, 40),
        (3751, 30),
        (3717, 20),
        (3671, 10),
        (3646, 5),
        (3579, 2),
        (3500, 0),
    };

    public static int ToPercent(int millivolts)
    {
        if (millivolts >= Anchors[0].Millivolts)
        {
            return 100;
        }

        if (millivolts <= Anchors[^1].Millivolts)
        {
            return 0;
        }

        for (int i = 0; i < Anchors.Length - 1; i++)
        {
            (int hiMv, int hiPct) = Anchors[i];
            (int loMv, int loPct) = Anchors[i + 1];

            if (millivolts <= hiMv && millivolts >= loMv)
            {
                double fraction = (double)(millivolts - loMv) / (hiMv - loMv);
                return loPct + (int)Math.Round(fraction * (hiPct - loPct));
            }
        }

        return 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter BatteryVoltageTableTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.Protocol/BatteryVoltageTable.cs tests/LogiBoltTray.Protocol.Tests/BatteryVoltageTableTests.cs
git commit -m "Add Li-Po voltage-to-percentage approximation table"
```

---

## Task 6: Battery reader with feature fallback chain

**Files:**
- Create: `src/LogiBoltTray.Protocol/BatteryStatus.cs`
- Create: `src/LogiBoltTray.Protocol/BatteryReader.cs`
- Test: `tests/LogiBoltTray.Protocol.Tests/BatteryReaderTests.cs`

**Interfaces:**
- Consumes: `IHidppTransport`, `HidppFrame`, `RootFeatureClient` (Task 2-4), `BatteryVoltageTable` (Task 5).
- Produces: `readonly record struct BatteryStatus(int Percent, bool IsCharging, bool IsUnknown)`. `BatteryReader` with `ctor(IHidppTransport transport, byte deviceIndex)` and `BatteryStatus Read()` — tries feature `0x1000`, then `0x1004`, then `0x1001`, in that order; returns `BatteryStatus` with `IsUnknown = true` if none are supported. Consumed by Task 9 (`DeviceDiscoveryService`).

- [ ] **Step 1: Write `BatteryStatus`**

```csharp
namespace LogiBoltTray.Protocol;

public readonly record struct BatteryStatus(int Percent, bool IsCharging, bool IsUnknown)
{
    public static BatteryStatus Unknown() => new(Percent: 0, IsCharging: false, IsUnknown: true);
}
```

- [ ] **Step 2: Write the failing tests for `BatteryReader`**

```csharp
using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class BatteryReaderTests
{
    private const byte DeviceIndex = 0x01;

    [Fact]
    public void Read_Feature0x1000Supported_ReturnsPercentAndChargingFromBatteryStatus()
    {
        var transport = new FakeHidppTransport();
        // RootFeatureClient.FindFeatureIndex(0x1000) -> featureIndex 0x05
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x05, 0x00, 0x00));
        // BatteryReader calls function 0x00 on featureIndex 0x05 -> [percent, nextLevel, chargingStatus]
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x05, 0x00, 0x1, 87, 80, 0x01)); // chargingStatus 1 = recharging

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.False(status.IsUnknown);
        Assert.Equal(87, status.Percent);
        Assert.True(status.IsCharging);
    }

    [Fact]
    public void Read_Only0x1004Supported_FallsBackToUnifiedBattery()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x1000 not found
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x07, 0x00, 0x00)); // 0x1004 -> index 0x07
        // GetStatus (function 0x01) -> [stateOfCharge, batteryLevel, chargingStatus]
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x07, 0x01, 0x1, 42, 0x02, 0x00));

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.False(status.IsUnknown);
        Assert.Equal(42, status.Percent);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void Read_Only0x1001Supported_FallsBackToVoltageTable()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x1000 not found
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x1004 not found
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x09, 0x00, 0x00)); // 0x1001 -> index 0x09
        // GetBatteryVoltage -> [mV high, mV low, chargingFlag]
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x09, 0x00, 0x1, 0x10, 0x5E, 0x01)); // 0x105E = 4190mV, charging

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.False(status.IsUnknown);
        Assert.Equal(100, status.Percent);
        Assert.True(status.IsCharging);
    }

    [Fact]
    public void Read_NoBatteryFeatureSupported_ReturnsUnknown()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00));
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00));
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00));

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.True(status.IsUnknown);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter BatteryReaderTests`
Expected: FAIL to compile — `BatteryReader` does not exist yet.

- [ ] **Step 4: Implement `BatteryReader`**

```csharp
namespace LogiBoltTray.Protocol;

public sealed class BatteryReader
{
    private const ushort BatteryStatusFeatureId = 0x1000;
    private const ushort UnifiedBatteryFeatureId = 0x1004;
    private const ushort BatteryVoltageFeatureId = 0x1001;
    private const byte SoftwareId = 0x1;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidppTransport _transport;
    private readonly byte _deviceIndex;
    private readonly RootFeatureClient _rootFeatureClient;

    public BatteryReader(IHidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        _deviceIndex = deviceIndex;
        _rootFeatureClient = new RootFeatureClient(transport, deviceIndex);
    }

    public BatteryStatus Read()
    {
        byte? legacyIndex = _rootFeatureClient.FindFeatureIndex(BatteryStatusFeatureId);
        if (legacyIndex is { } legacy)
        {
            var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, legacy, 0x00, SoftwareId), DefaultTimeout);
            if (response is { IsError: false } r)
            {
                int percent = r.Params[0];
                bool charging = r.Params[2] != 0x00 && r.Params[2] != 0x03; // 0=discharging, 3=full are "not charging"
                return new BatteryStatus(percent, charging, IsUnknown: false);
            }
        }

        byte? unifiedIndex = _rootFeatureClient.FindFeatureIndex(UnifiedBatteryFeatureId);
        if (unifiedIndex is { } unified)
        {
            var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, unified, 0x01, SoftwareId), DefaultTimeout);
            if (response is { IsError: false } r)
            {
                int percent = r.Params[0];
                bool charging = r.Params[2] != 0x00;
                return new BatteryStatus(percent, charging, IsUnknown: false);
            }
        }

        byte? voltageIndex = _rootFeatureClient.FindFeatureIndex(BatteryVoltageFeatureId);
        if (voltageIndex is { } voltage)
        {
            var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, voltage, 0x00, SoftwareId), DefaultTimeout);
            if (response is { IsError: false } r)
            {
                int millivolts = (r.Params[0] << 8) | r.Params[1];
                bool charging = (r.Params[2] & 0x01) != 0;
                int percent = BatteryVoltageTable.ToPercent(millivolts);
                return new BatteryStatus(percent, charging, IsUnknown: false);
            }
        }

        return BatteryStatus.Unknown();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter BatteryReaderTests`
Expected: PASS (4 tests)

- [ ] **Step 6: Commit**

```bash
git add src/LogiBoltTray.Protocol/BatteryStatus.cs src/LogiBoltTray.Protocol/BatteryReader.cs tests/LogiBoltTray.Protocol.Tests/BatteryReaderTests.cs
git commit -m "Add battery reader with 0x1000/0x1004/0x1001 fallback chain"
```

---

## Task 7: Device type and name

**Files:**
- Create: `src/LogiBoltTray.Protocol/DeviceType.cs`
- Create: `src/LogiBoltTray.Protocol/DeviceNameClient.cs`
- Test: `tests/LogiBoltTray.Protocol.Tests/DeviceNameClientTests.cs`

**Interfaces:**
- Consumes: `IHidppTransport`, `HidppFrame`, `RootFeatureClient` (Task 2-4).
- Produces: `enum DeviceType { Unknown, Mouse, Keyboard, Trackball, Headset, Other }`. `DeviceNameClient` with `ctor(IHidppTransport transport, byte deviceIndex)`, `string? GetName()`, `DeviceType GetDeviceType()`. Consumed by Task 9 (`DeviceDiscoveryService`) and later by the App project's icon glyph selection.

- [ ] **Step 1: Write `DeviceType`**

```csharp
namespace LogiBoltTray.Protocol;

public enum DeviceType
{
    Unknown,
    Keyboard,
    Mouse,
    Trackball,
    Headset,
    Other,
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class DeviceNameClientTests
{
    private const byte DeviceIndex = 0x02;

    [Fact]
    public void GetName_ReadsLengthThenReadsThatManyCharacters()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x03, 0x00, 0x00)); // 0x0005 -> index 0x03
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x03, 0x00, 0x1, 6, 0, 0));           // GetLength -> 6 chars
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes("MX Master".PadRight(16, '\0'))[..16];
        transport.EnqueueResponse(HidppFrame.Long(DeviceIndex, 0x03, 0x01, 0x1, nameBytes));           // GetName(0)

        string? name = new DeviceNameClient(transport, DeviceIndex).GetName();

        Assert.Equal("MX Mast", name); // first 6 chars requested + null terminator boundary honored: "MX Mas" + 't' truncated at length 6 -> see note below
    }

    [Fact]
    public void GetDeviceType_MouseKindByte_MapsToMouse()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x03, 0x00, 0x00)); // 0x0005 -> index 0x03
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x03, 0x02, 0x1, 0x04, 0, 0));        // GetKind -> 0x04 = mouse

        var type = new DeviceNameClient(transport, DeviceIndex).GetDeviceType();

        Assert.Equal(DeviceType.Mouse, type);
    }

    [Fact]
    public void GetDeviceType_FeatureNotSupported_ReturnsUnknown()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x0005 not found

        var type = new DeviceNameClient(transport, DeviceIndex).GetDeviceType();

        Assert.Equal(DeviceType.Unknown, type);
    }
}
```

Note on the first test: `GetLength` reports 6 characters available, so `GetName` must trim the 16-byte padded response down to those first 6 characters (`"MX Mas"`), not the full null-padded buffer and not the string literal used to build the fake response. Fix the expected value in the test to `"MX Mas"` before running it — the assertion above intentionally documents the truncation-by-length behavior the implementation must have.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter DeviceNameClientTests`
Expected: FAIL to compile — `DeviceNameClient` does not exist yet.

- [ ] **Step 4: Implement `DeviceNameClient`**

```csharp
using System.Text;

namespace LogiBoltTray.Protocol;

public sealed class DeviceNameClient
{
    private const ushort DeviceNameFeatureId = 0x0005;
    private const byte GetLengthFunctionId = 0x00;
    private const byte GetNameFunctionId = 0x01;
    private const byte GetKindFunctionId = 0x02;
    private const byte SoftwareId = 0x1;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidppTransport _transport;
    private readonly byte _deviceIndex;
    private readonly RootFeatureClient _rootFeatureClient;

    public DeviceNameClient(IHidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        _deviceIndex = deviceIndex;
        _rootFeatureClient = new RootFeatureClient(transport, deviceIndex);
    }

    public string? GetName()
    {
        byte? featureIndex = _rootFeatureClient.FindFeatureIndex(DeviceNameFeatureId);
        if (featureIndex is not { } index)
        {
            return null;
        }

        var lengthResponse = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, index, GetLengthFunctionId, SoftwareId), DefaultTimeout);
        if (lengthResponse is not { IsError: false } lr)
        {
            return null;
        }

        int length = lr.Params[0];
        var nameResponse = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, index, GetNameFunctionId, SoftwareId), DefaultTimeout);
        if (nameResponse is not { IsError: false } nr)
        {
            return null;
        }

        int available = Math.Min(length, nr.Params.Length);
        return Encoding.ASCII.GetString(nr.Params, 0, available);
    }

    public DeviceType GetDeviceType()
    {
        byte? featureIndex = _rootFeatureClient.FindFeatureIndex(DeviceNameFeatureId);
        if (featureIndex is not { } index)
        {
            return DeviceType.Unknown;
        }

        var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, index, GetKindFunctionId, SoftwareId), DefaultTimeout);
        if (response is not { IsError: false } r)
        {
            return DeviceType.Unknown;
        }

        return r.Params[0] switch
        {
            0x00 => DeviceType.Keyboard,
            0x04 => DeviceType.Mouse,
            0x06 => DeviceType.Trackball,
            0x08 => DeviceType.Headset,
            _ => DeviceType.Other,
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter DeviceNameClientTests`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/LogiBoltTray.Protocol/DeviceType.cs src/LogiBoltTray.Protocol/DeviceNameClient.cs tests/LogiBoltTray.Protocol.Tests/DeviceNameClientTests.cs
git commit -m "Add device type and name reading via feature 0x0005"
```

---

## Task 8: Bolt receiver enumeration and HID transport (Hid project)

**Files:**
- Create: `src/LogiBoltTray.Hid/BoltReceiverIds.cs`
- Create: `src/LogiBoltTray.Hid/ReceiverEnumerator.cs`
- Create: `src/LogiBoltTray.Hid/HidppHidTransport.cs`
- Create: `src/LogiBoltTray.Hid/DiagnosticDump.cs`

**Interfaces:**
- Consumes: `HidApi.Net` (`Hid`, `DeviceInfo`, `Device`), `IHidppTransport`, `HidppFrame` (Task 2, 3).
- Produces: `BoltReceiverIds` (constants). `ReceiverEnumerator` with `static IReadOnlyList<HidApi.DeviceInfo> FindBoltInterfaces()`. `HidppHidTransport : IHidppTransport` with `ctor(HidApi.Device device)`. `DiagnosticDump` with `static string DumpAllLogitechInterfaces()` — a debug helper the user runs once on Windows to confirm the real PID/UsagePage/InterfaceNumber of their receiver (Global Constraints note: PID `0xC548` is a best-effort default, not a certainty). Consumed by Task 9 (`DeviceDiscoveryService`) and Task 14 (App wiring).

This task cannot be unit-tested meaningfully without a real Bolt receiver (there is nothing to fake at this layer — it's a thin adapter over the native library). It builds on macOS (P/Invoke declarations resolve at compile time without the native lib present) but every behavior here is verified manually on the user's Windows PC in Step 5.

- [ ] **Step 1: Write `BoltReceiverIds`**

```csharp
namespace LogiBoltTray.Hid;

public static class BoltReceiverIds
{
    public const ushort LogitechVendorId = 0x046D;

    /// <summary>
    /// Most commonly documented Logitech Bolt receiver product ID. Bolt receivers may ship
    /// under additional PIDs — run DiagnosticDump.DumpAllLogitechInterfaces() on the target
    /// PC and add any missing PID found there.
    /// </summary>
    public static readonly ushort[] KnownProductIds = { 0xC548 };

    /// <summary>
    /// Vendor-defined HID usage page Logitech uses for the HID++ short/long report interface.
    /// If no interface on the receiver matches this, ReceiverEnumerator falls back to
    /// returning every interface for the PID so the user can identify the right one manually.
    /// </summary>
    public const ushort HidppUsagePage = 0xFF43;
}
```

- [ ] **Step 2: Write `ReceiverEnumerator`**

```csharp
using HidApi;

namespace LogiBoltTray.Hid;

public static class ReceiverEnumerator
{
    public static IReadOnlyList<DeviceInfo> FindBoltInterfaces()
    {
        var matches = new List<DeviceInfo>();
        var fallback = new List<DeviceInfo>();

        foreach (ushort productId in BoltReceiverIds.KnownProductIds)
        {
            foreach (var info in Hid.Enumerate(BoltReceiverIds.LogitechVendorId, productId))
            {
                fallback.Add(info);
                if (info.UsagePage == BoltReceiverIds.HidppUsagePage)
                {
                    matches.Add(info);
                }
            }
        }

        return matches.Count > 0 ? matches : fallback;
    }
}
```

- [ ] **Step 3: Write `HidppHidTransport`**

```csharp
using HidApi;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.Hid;

public sealed class HidppHidTransport : IHidppTransport, IDisposable
{
    private readonly Device _device;

    public HidppHidTransport(Device device)
    {
        _device = device;
    }

    public HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout)
    {
        _device.Write(request.ToBytes());

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            int remainingMs = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            int maxLength = request.ReportId == HidppFrame.LongReportId ? 20 : 7;
            var buffer = new byte[maxLength];
            int read = _device.ReadTimeout(buffer, remainingMs);
            if (read <= 0)
            {
                continue;
            }

            var response = HidppFrame.Parse(buffer.AsSpan(0, read));
            bool matchesRequest = response.DeviceIndex == request.DeviceIndex
                && response.FunctionId == request.FunctionId
                && response.SoftwareId == request.SoftwareId
                && (response.FeatureIndex == request.FeatureIndex || response.IsError);

            if (matchesRequest)
            {
                return response;
            }
            // Otherwise this was an unrelated notification (e.g. connect/disconnect) — keep waiting.
        }

        return null;
    }

    public void Dispose() => _device.Dispose();
}
```

- [ ] **Step 4: Write the one-time diagnostic dump helper**

```csharp
using System.Text;
using HidApi;

namespace LogiBoltTray.Hid;

public static class DiagnosticDump
{
    /// <summary>
    /// Run this once on the target Windows PC (see Task 8's manual verification steps) to
    /// find the real product ID / usage page / interface number of the connected Bolt
    /// receiver if the defaults in BoltReceiverIds don't find it.
    /// </summary>
    public static string DumpAllLogitechInterfaces()
    {
        var sb = new StringBuilder();
        foreach (var info in Hid.Enumerate(BoltReceiverIds.LogitechVendorId, 0))
        {
            sb.AppendLine($"PID=0x{info.ProductId:X4} Usage=0x{info.Usage:X4} UsagePage=0x{info.UsagePage:X4} " +
                          $"Interface={info.InterfaceNumber} Product=\"{info.ProductString}\" Path={info.Path}");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 5: Build to verify it compiles on macOS**

Run: `dotnet build src/LogiBoltTray.Hid`
Expected: builds with 0 errors.

- [ ] **Step 6: Manual verification on Windows (record output for the next tasks)**

On the Windows PC, with the Bolt receiver plugged in, write a throwaway `Console.WriteLine(LogiBoltTray.Hid.DiagnosticDump.DumpAllLogitechInterfaces())` in a scratch console app referencing `LogiBoltTray.Hid`, run it, and paste the output back. Confirm whether PID `0xC548` and usage page `0xFF43` actually show up — if not, note the real values so `BoltReceiverIds` can be corrected in a follow-up commit before Task 14's end-to-end wiring.

- [ ] **Step 7: Commit**

```bash
git add src/LogiBoltTray.Hid/BoltReceiverIds.cs src/LogiBoltTray.Hid/ReceiverEnumerator.cs src/LogiBoltTray.Hid/HidppHidTransport.cs src/LogiBoltTray.Hid/DiagnosticDump.cs
git commit -m "Add Bolt receiver enumeration and HidApi.Net-backed HID++ transport"
```

---

## Task 9: Device discovery service (ties Protocol together)

**Files:**
- Create: `src/LogiBoltTray.Protocol/LogiBoltDeviceInfo.cs`
- Create: `src/LogiBoltTray.Protocol/DeviceDiscoveryService.cs`
- Test: `tests/LogiBoltTray.Protocol.Tests/DeviceDiscoveryServiceTests.cs`

**Interfaces:**
- Consumes: `IHidppTransport`, `BatteryReader`, `DeviceNameClient`, `DeviceType` (Task 2-7).
- Produces: `readonly record struct LogiBoltDeviceInfo(byte DeviceIndex, string Name, DeviceType Type, BatteryStatus Battery)`. `DeviceDiscoveryService` with `ctor(IHidppTransport transport)` and `IReadOnlyList<LogiBoltDeviceInfo> DiscoverDevices()` — probes device indices `0x01`..`0x06` (Bolt supports up to 6 paired devices), skipping any index that doesn't respond. Consumed directly by the App project's `PollingService` (Task 15).

- [ ] **Step 1: Write `LogiBoltDeviceInfo`**

```csharp
namespace LogiBoltTray.Protocol;

public readonly record struct LogiBoltDeviceInfo(byte DeviceIndex, string Name, DeviceType Type, BatteryStatus Battery);
```

- [ ] **Step 2: Write the failing tests**

```csharp
using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class DeviceDiscoveryServiceTests
{
    private static HidppFrame RootResponse(byte deviceIndex, byte featureIndex)
        => HidppFrame.Short(deviceIndex, 0x00, 0x00, 0x1, featureIndex, 0x00, 0x00);

    [Fact]
    public void DiscoverDevices_OneDeviceRespondingAtIndexOne_ReturnsSingleDevice()
    {
        var transport = new FakeHidppTransport();

        // Device index 0x01: BatteryReader tries 0x1000 first (found at 0x05)
        transport.EnqueueResponse(RootResponse(0x01, 0x05));
        transport.EnqueueResponse(HidppFrame.Short(0x01, 0x05, 0x00, 0x1, 76, 70, 0x00));
        // DeviceNameClient: 0x0005 found at 0x03, length 2, name "M5"
        transport.EnqueueResponse(RootResponse(0x01, 0x03));
        transport.EnqueueResponse(HidppFrame.Short(0x01, 0x03, 0x00, 0x1, 2, 0, 0));
        byte[] nameBytes = new byte[16];
        nameBytes[0] = (byte)'M';
        nameBytes[1] = (byte)'5';
        transport.EnqueueResponse(HidppFrame.Long(0x01, 0x03, 0x01, 0x1, nameBytes));
        transport.EnqueueResponse(RootResponse(0x01, 0x03));
        transport.EnqueueResponse(HidppFrame.Short(0x01, 0x03, 0x02, 0x1, 0x04, 0, 0)); // kind = mouse

        // Device indices 0x02..0x06: no response at all (not paired)
        // FakeHidppTransport returns null automatically once its queue is empty.

        var devices = new DeviceDiscoveryService(transport).DiscoverDevices();

        var device = Assert.Single(devices);
        Assert.Equal((byte)0x01, device.DeviceIndex);
        Assert.Equal("M5", device.Name);
        Assert.Equal(DeviceType.Mouse, device.Type);
        Assert.Equal(76, device.Battery.Percent);
    }

    [Fact]
    public void DiscoverDevices_NoDeviceResponds_ReturnsEmptyList()
    {
        var transport = new FakeHidppTransport();

        var devices = new DeviceDiscoveryService(transport).DiscoverDevices();

        Assert.Empty(devices);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter DeviceDiscoveryServiceTests`
Expected: FAIL to compile — `DeviceDiscoveryService` does not exist yet.

- [ ] **Step 4: Implement `DeviceDiscoveryService`**

```csharp
namespace LogiBoltTray.Protocol;

public sealed class DeviceDiscoveryService
{
    private const byte FirstDeviceIndex = 0x01;
    private const byte LastDeviceIndex = 0x06; // Bolt receivers support up to 6 paired devices

    private readonly IHidppTransport _transport;

    public DeviceDiscoveryService(IHidppTransport transport)
    {
        _transport = transport;
    }

    public IReadOnlyList<LogiBoltDeviceInfo> DiscoverDevices()
    {
        var devices = new List<LogiBoltDeviceInfo>();

        for (byte deviceIndex = FirstDeviceIndex; deviceIndex <= LastDeviceIndex; deviceIndex++)
        {
            var battery = new BatteryReader(_transport, deviceIndex).Read();
            if (battery.IsUnknown)
            {
                continue; // device not paired at this index, or doesn't support any known battery feature
            }

            var nameClient = new DeviceNameClient(_transport, deviceIndex);
            string name = nameClient.GetName() ?? $"Device {deviceIndex}";
            DeviceType type = nameClient.GetDeviceType();

            devices.Add(new LogiBoltDeviceInfo(deviceIndex, name, type, battery));
        }

        return devices;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests --filter DeviceDiscoveryServiceTests`
Expected: PASS (2 tests)

- [ ] **Step 6: Run the full Protocol test suite**

Run: `dotnet test tests/LogiBoltTray.Protocol.Tests`
Expected: PASS (all tests across Tasks 2-9)

- [ ] **Step 7: Commit**

```bash
git add src/LogiBoltTray.Protocol/LogiBoltDeviceInfo.cs src/LogiBoltTray.Protocol/DeviceDiscoveryService.cs tests/LogiBoltTray.Protocol.Tests/DeviceDiscoveryServiceTests.cs
git commit -m "Add device discovery service that probes indices 1-6 for paired devices"
```

---

## Task 10: Settings model and JSON persistence (Core project)

**Files:**
- Create: `src/LogiBoltTray.Core/IconStyle.cs`
- Create: `src/LogiBoltTray.Core/SettingsModel.cs`
- Create: `src/LogiBoltTray.Core/SettingsStore.cs`
- Test: `tests/LogiBoltTray.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `enum IconStyle { A_Number, B_GlyphBadge, C_BatteryBar, D_Hybrid }`. `class SettingsModel` (mutable, JSON-serializable) with `int PollIntervalSeconds`, `IconStyle IconStyle`, `int LowBatteryThresholdPercent`, `bool LowBatteryNotificationsEnabled`, `bool AutostartEnabled`, `Dictionary<string, bool> DeviceVisibility` (keyed by device name), plus a `static SettingsModel Default()` factory matching the spec's defaults. `SettingsStore` with `ctor(string baseDirectory)`, `SettingsModel Load()` (returns `Default()` if the file doesn't exist or fails to parse), `void Save(SettingsModel settings)`. Consumed by the App project's `PollingService`, `TrayIconManager`, and the settings window (Task 14+).

- [ ] **Step 1: Write `IconStyle`**

```csharp
namespace LogiBoltTray.Core;

public enum IconStyle
{
    A_Number,
    B_GlyphBadge,
    C_BatteryBar,
    D_Hybrid,
}
```

- [ ] **Step 2: Write `SettingsModel`**

```csharp
namespace LogiBoltTray.Core;

public sealed class SettingsModel
{
    public int PollIntervalSeconds { get; set; } = 60;
    public IconStyle IconStyle { get; set; } = IconStyle.B_GlyphBadge;
    public int LowBatteryThresholdPercent { get; set; } = 20;
    public bool LowBatteryNotificationsEnabled { get; set; } = true;
    public bool AutostartEnabled { get; set; } = false;
    public Dictionary<string, bool> DeviceVisibility { get; set; } = new();

    public static SettingsModel Default() => new();
}
```

- [ ] **Step 3: Write the failing tests for `SettingsStore`**

```csharp
using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LogiBoltTrayTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_NoFileExists_ReturnsDefaults()
    {
        var store = new SettingsStore(_tempDir);

        SettingsModel settings = store.Load();

        Assert.Equal(60, settings.PollIntervalSeconds);
        Assert.Equal(IconStyle.B_GlyphBadge, settings.IconStyle);
        Assert.Equal(20, settings.LowBatteryThresholdPercent);
        Assert.True(settings.LowBatteryNotificationsEnabled);
        Assert.False(settings.AutostartEnabled);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(_tempDir);
        var original = new SettingsModel
        {
            PollIntervalSeconds = 120,
            IconStyle = IconStyle.D_Hybrid,
            LowBatteryThresholdPercent = 15,
            LowBatteryNotificationsEnabled = false,
            AutostartEnabled = true,
        };
        original.DeviceVisibility["MX Master"] = false;

        store.Save(original);
        SettingsModel loaded = store.Load();

        Assert.Equal(120, loaded.PollIntervalSeconds);
        Assert.Equal(IconStyle.D_Hybrid, loaded.IconStyle);
        Assert.Equal(15, loaded.LowBatteryThresholdPercent);
        Assert.False(loaded.LowBatteryNotificationsEnabled);
        Assert.True(loaded.AutostartEnabled);
        Assert.False(loaded.DeviceVisibility["MX Master"]);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{ not valid json");
        var store = new SettingsStore(_tempDir);

        SettingsModel settings = store.Load();

        Assert.Equal(60, settings.PollIntervalSeconds);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter SettingsStoreTests`
Expected: FAIL to compile — `SettingsStore` does not exist yet.

- [ ] **Step 5: Implement `SettingsStore`**

```csharp
using System.Text.Json;

namespace LogiBoltTray.Core;

public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore(string baseDirectory)
    {
        _filePath = Path.Combine(baseDirectory, "config.json");
    }

    public SettingsModel Load()
    {
        if (!File.Exists(_filePath))
        {
            return SettingsModel.Default();
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SettingsModel>(json) ?? SettingsModel.Default();
        }
        catch (JsonException)
        {
            return SettingsModel.Default();
        }
    }

    public void Save(SettingsModel settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter SettingsStoreTests`
Expected: PASS (3 tests)

- [ ] **Step 7: Commit**

```bash
git add src/LogiBoltTray.Core/IconStyle.cs src/LogiBoltTray.Core/SettingsModel.cs src/LogiBoltTray.Core/SettingsStore.cs tests/LogiBoltTray.Core.Tests/SettingsStoreTests.cs
git commit -m "Add settings model and JSON-backed settings store"
```

---

## Task 11: Icon color threshold logic (Core project)

**Files:**
- Create: `src/LogiBoltTray.Core/BatteryColor.cs`
- Test: `tests/LogiBoltTray.Core.Tests/BatteryColorTests.cs`

**Interfaces:**
- Produces: `enum BatteryColor { Red, Orange, Green, Purple }`. `static class BatteryColorPolicy` with `static BatteryColor GetColor(int percent)` implementing the spec's exact thresholds. Consumed later by the App project's `TrayBitmapRenderer` (Task 16), which maps each `BatteryColor` to an actual `System.Drawing.Color`.

- [ ] **Step 1: Write the failing tests**

```csharp
using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class BatteryColorTests
{
    [Theory]
    [InlineData(0, BatteryColor.Red)]
    [InlineData(19, BatteryColor.Red)]
    [InlineData(20, BatteryColor.Orange)]
    [InlineData(45, BatteryColor.Orange)]
    [InlineData(60, BatteryColor.Orange)]
    [InlineData(61, BatteryColor.Green)]
    [InlineData(99, BatteryColor.Green)]
    [InlineData(100, BatteryColor.Purple)]
    public void GetColor_MatchesSpecThresholds(int percent, BatteryColor expected)
    {
        Assert.Equal(expected, BatteryColorPolicy.GetColor(percent));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter BatteryColorTests`
Expected: FAIL to compile — `BatteryColorPolicy` does not exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace LogiBoltTray.Core;

public enum BatteryColor
{
    Red,
    Orange,
    Green,
    Purple,
}

public static class BatteryColorPolicy
{
    public static BatteryColor GetColor(int percent)
    {
        if (percent == 100) return BatteryColor.Purple;
        if (percent > 60) return BatteryColor.Green;
        if (percent >= 20) return BatteryColor.Orange;
        return BatteryColor.Red;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter BatteryColorTests`
Expected: PASS (8 tests)

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.Core/BatteryColor.cs tests/LogiBoltTray.Core.Tests/BatteryColorTests.cs
git commit -m "Add battery color threshold policy"
```

---

## Task 12: Low battery notification state machine (Core project)

**Files:**
- Create: `src/LogiBoltTray.Core/LowBatteryNotifier.cs`
- Test: `tests/LogiBoltTray.Core.Tests/LowBatteryNotifierTests.cs`

**Interfaces:**
- Produces: `LowBatteryNotifier` with `ctor(int thresholdPercent)` and `bool ShouldNotify(string deviceKey, int currentPercent)` — returns `true` exactly once per downward crossing of the threshold per device, resets once the device rises back above the threshold. Consumed by the App project's `PollingService` (Task 15), which calls `NotifyIcon.ShowBalloonTip` when this returns `true`.

- [ ] **Step 1: Write the failing tests**

```csharp
using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class LowBatteryNotifierTests
{
    [Fact]
    public void ShouldNotify_FirstReadingBelowThreshold_ReturnsTrueOnce()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.True(notifier.ShouldNotify("mouse-1", 15));
        Assert.False(notifier.ShouldNotify("mouse-1", 14)); // still below threshold, already notified
    }

    [Fact]
    public void ShouldNotify_StaysAboveThreshold_NeverNotifies()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.False(notifier.ShouldNotify("mouse-1", 80));
        Assert.False(notifier.ShouldNotify("mouse-1", 70));
    }

    [Fact]
    public void ShouldNotify_RisesAboveThresholdThenDropsAgain_NotifiesAgain()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.True(notifier.ShouldNotify("mouse-1", 10));
        Assert.False(notifier.ShouldNotify("mouse-1", 5));
        // charged back up above threshold
        Assert.False(notifier.ShouldNotify("mouse-1", 90));
        // dropped below threshold again — should notify again
        Assert.True(notifier.ShouldNotify("mouse-1", 12));
    }

    [Fact]
    public void ShouldNotify_TracksEachDeviceIndependently()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.True(notifier.ShouldNotify("mouse-1", 10));
        Assert.True(notifier.ShouldNotify("keyboard-1", 5)); // different device, notifies independently
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter LowBatteryNotifierTests`
Expected: FAIL to compile — `LowBatteryNotifier` does not exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace LogiBoltTray.Core;

public sealed class LowBatteryNotifier
{
    private readonly int _thresholdPercent;
    private readonly Dictionary<string, bool> _alreadyNotifiedBelowThreshold = new();

    public LowBatteryNotifier(int thresholdPercent)
    {
        _thresholdPercent = thresholdPercent;
    }

    public bool ShouldNotify(string deviceKey, int currentPercent)
    {
        bool isBelow = currentPercent < _thresholdPercent;
        bool alreadyNotified = _alreadyNotifiedBelowThreshold.GetValueOrDefault(deviceKey);

        if (!isBelow)
        {
            _alreadyNotifiedBelowThreshold[deviceKey] = false;
            return false;
        }

        if (alreadyNotified)
        {
            return false;
        }

        _alreadyNotifiedBelowThreshold[deviceKey] = true;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter LowBatteryNotifierTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.Core/LowBatteryNotifier.cs tests/LogiBoltTray.Core.Tests/LowBatteryNotifierTests.cs
git commit -m "Add low battery notification crossing-detection state machine"
```

---

## Task 13: Poll interval policy (Core project)

**Files:**
- Create: `src/LogiBoltTray.Core/PollIntervalPolicy.cs`
- Test: `tests/LogiBoltTray.Core.Tests/PollIntervalPolicyTests.cs`

**Interfaces:**
- Produces: `static class PollIntervalPolicy` with `const int MinSeconds = 15`, `const int MaxSeconds = 1800`, `static int Clamp(int requestedSeconds)`. Consumed by the App project's settings window (Task 17) when the user edits the poll interval, and by `SettingsModel` validation before `PollingService` starts its timer.

- [ ] **Step 1: Write the failing tests**

```csharp
using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class PollIntervalPolicyTests
{
    [Theory]
    [InlineData(60, 60)]
    [InlineData(15, 15)]
    [InlineData(1800, 1800)]
    [InlineData(5, 15)]     // below min clamps up
    [InlineData(3600, 1800)] // above max clamps down
    public void Clamp_ReturnsValueWithinAllowedRange(int requested, int expected)
    {
        Assert.Equal(expected, PollIntervalPolicy.Clamp(requested));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter PollIntervalPolicyTests`
Expected: FAIL to compile — `PollIntervalPolicy` does not exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace LogiBoltTray.Core;

public static class PollIntervalPolicy
{
    public const int MinSeconds = 15;
    public const int MaxSeconds = 1800; // 30 minutes

    public static int Clamp(int requestedSeconds) => Math.Clamp(requestedSeconds, MinSeconds, MaxSeconds);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter PollIntervalPolicyTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Run the full Core test suite**

Run: `dotnet test tests/LogiBoltTray.Core.Tests`
Expected: PASS (all tests across Tasks 10-13)

- [ ] **Step 6: Commit**

```bash
git add src/LogiBoltTray.Core/PollIntervalPolicy.cs tests/LogiBoltTray.Core.Tests/PollIntervalPolicyTests.cs
git commit -m "Add poll interval clamping policy"
```

---

## Task 14: App scaffold — entry point, single instance, tray-only startup

> **From here on, every task's build/run verification happens on the user's Windows PC.** I cannot compile `net8.0-windows` projects on macOS. Each task lists exact manual steps; report back the result (worked / error text) so the next task can account for it.

**Files:**
- Modify: `src/LogiBoltTray.App/App.xaml`
- Modify: `src/LogiBoltTray.App/App.xaml.cs`
- Create: `src/LogiBoltTray.App/SingleInstanceGuard.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks yet (this task only establishes the process shell).
- Produces: an `App` that starts with **no visible window** (tray-only), holding a `Mutex`-based single-instance guard. Consumed by Task 15 (`PollingService` wiring) and Task 18 (`MainWindow` show/hide).

- [ ] **Step 1: Prevent WPF's default "show first window" behavior**

Edit `src/LogiBoltTray.App/App.xaml` — remove the `StartupUri` attribute if the template generated one, so the app doesn't open a window automatically:

```xml
<Application x:Class="LogiBoltTray.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources />
</Application>
```

- [ ] **Step 2: Write the single-instance guard**

```csharp
using System.Threading;

namespace LogiBoltTray.App;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose() => _mutex.Dispose();
}
```

- [ ] **Step 3: Wire it into `App.xaml.cs`**

```csharp
using System.Windows;

namespace LogiBoltTray.App;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceGuard = new SingleInstanceGuard("LogiBoltTray-SingleInstance");
        if (!_instanceGuard.IsFirstInstance)
        {
            MessageBox.Show("LogiBoltTray is already running — check the system tray.", "LogiBoltTray");
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown; // no window open yet, don't exit immediately
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Manual verification on Windows**

```
dotnet build src/LogiBoltTray.App
dotnet run --project src/LogiBoltTray.App
```
Expected: the process starts and stays running with no visible window and no crash (check Task Manager for `LogiBoltTray.exe`). Run it a second time in parallel — expect the message box "already running" and the second instance to exit. Stop the first instance via Task Manager before continuing.

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.App/App.xaml src/LogiBoltTray.App/App.xaml.cs src/LogiBoltTray.App/SingleInstanceGuard.cs
git commit -m "Add tray-only app startup with single-instance guard"
```

---

## Task 15: Live HID++ transport wiring + polling service

**Files:**
- Create: `src/LogiBoltTray.App/DeviceRegistry.cs`
- Create: `src/LogiBoltTray.App/PollingService.cs`
- Modify: `src/LogiBoltTray.App/App.xaml.cs`

**Interfaces:**
- Consumes: `ReceiverEnumerator`, `HidppHidTransport` (Task 8), `DeviceDiscoveryService`, `LogiBoltDeviceInfo` (Task 9), `SettingsStore`, `PollIntervalPolicy` (Task 10, 13).
- Produces: `readonly record struct TrackedDevice(LogiBoltDeviceInfo Info, bool IsStale)`. `DeviceRegistry` — thread-safe holder of the latest `IReadOnlyList<TrackedDevice>`, with a `event Action Updated`. A device missing from one scan is kept for exactly one extra cycle marked `IsStale = true` (spec: "при ошибке/таймауте чтения используется последнее известное значение с пометкой устарело") before being dropped on a second consecutive miss (real unplug/unpair). `PollingService` with `ctor(DeviceRegistry registry, SettingsStore settingsStore)`, `void Start()`, `void Stop()`, `void PollNow()` (used by both the timer and the manual "Refresh" button in Task 18). Consumed by Task 16 (`TrayIconManager`) and Task 18 (`MainWindow`).

- [ ] **Step 1: Write `DeviceRegistry`**

```csharp
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

// Requires ImplicitUsings (enabled by the WPF project template) for System.Linq's
// ToDictionary/Select extension methods used below.
public readonly record struct TrackedDevice(LogiBoltDeviceInfo Info, bool IsStale);

public sealed class DeviceRegistry
{
    private readonly object _lock = new();
    private IReadOnlyList<TrackedDevice> _devices = Array.Empty<TrackedDevice>();

    public event Action? Updated;

    public IReadOnlyList<TrackedDevice> Devices
    {
        get { lock (_lock) { return _devices; } }
    }

    public void Replace(IReadOnlyList<LogiBoltDeviceInfo> freshScan)
    {
        lock (_lock)
        {
            var freshByIndex = freshScan.ToDictionary(d => d.DeviceIndex);
            var merged = new List<TrackedDevice>();

            foreach (var previous in _devices)
            {
                if (freshByIndex.TryGetValue(previous.Info.DeviceIndex, out var fresh))
                {
                    merged.Add(new TrackedDevice(fresh, IsStale: false));
                    freshByIndex.Remove(previous.Info.DeviceIndex);
                }
                else if (!previous.IsStale)
                {
                    merged.Add(previous with { IsStale = true }); // one grace cycle before dropping
                }
                // else: already stale and still missing this cycle -> drop (device unplugged/unpaired)
            }

            foreach (var newlySeen in freshByIndex.Values)
            {
                merged.Add(new TrackedDevice(newlySeen, IsStale: false));
            }

            _devices = merged;
        }

        Updated?.Invoke();
    }
}
```

- [ ] **Step 2: Write `PollingService`**

```csharp
using LogiBoltTray.Core;
using LogiBoltTray.Hid;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public sealed class PollingService : IDisposable
{
    private readonly DeviceRegistry _registry;
    private readonly SettingsStore _settingsStore;
    private System.Threading.Timer? _timer;

    public PollingService(DeviceRegistry registry, SettingsStore settingsStore)
    {
        _registry = registry;
        _settingsStore = settingsStore;
    }

    public void Start()
    {
        int intervalSeconds = PollIntervalPolicy.Clamp(_settingsStore.Load().PollIntervalSeconds);
        PollNow();
        _timer = new System.Threading.Timer(_ => PollNow(), null, TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(intervalSeconds));
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // v1 supports exactly one physical Bolt receiver at a time. Device indices (1-6) are only
    // unique within a single receiver, so only the first interface FindBoltInterfaces() returns
    // is polled — this is a deliberate scope decision to avoid a device-index collision if a
    // second receiver were ever polled in the same cycle, not an oversight.
    public void PollNow()
    {
        var allDevices = new List<LogiBoltDeviceInfo>();

        var receiverInterface = ReceiverEnumerator.FindBoltInterfaces().FirstOrDefault();
        if (receiverInterface is not null)
        {
            try
            {
                using var hidDevice = receiverInterface.ConnectToDevice();
                using var transport = new HidppHidTransport(hidDevice);
                allDevices.AddRange(new DeviceDiscoveryService(transport).DiscoverDevices());
            }
            catch (HidApi.HidException)
            {
                // Receiver interface disappeared between enumeration and connect (unplugged) — skip this cycle.
            }
        }

        _registry.Replace(allDevices);
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 3: Wire it into `App.xaml.cs`**

Add fields and start the service on startup:

```csharp
// Add to the App class from Task 14:
private DeviceRegistry? _deviceRegistry;
private PollingService? _pollingService;
private SettingsStore? _settingsStore;

// At the end of OnStartup, before the closing brace, after the single-instance check passes:
_settingsStore = new SettingsStore(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\LogiBoltTray");
_deviceRegistry = new DeviceRegistry();
_pollingService = new PollingService(_deviceRegistry, _settingsStore);
_pollingService.Start();

// Add to OnExit, before base.OnExit(e):
_pollingService?.Dispose();
```

- [ ] **Step 4: Manual verification on Windows**

With the Bolt receiver and at least one paired device connected, add a temporary `_deviceRegistry.Updated += () => System.Diagnostics.Debug.WriteLine(string.Join(", ", _deviceRegistry.Devices.Select(t => $"{t.Info.Name}={t.Info.Battery.Percent}%{(t.IsStale ? " (stale)" : "")}")));` right after `_pollingService.Start();`, run under the Visual Studio / `dotnet run` debugger, and confirm the Debug Output window prints your real device name(s) and a plausible battery percentage within the first poll cycle. Remove the temporary line once confirmed working (or leave it — it's harmless `Debug.WriteLine`, but it's not part of the design, so remove it before committing). If nothing prints or the percentage looks wrong, capture the exception/output and report back — this is exactly the point where `BoltReceiverIds` (Task 8) or the byte offsets in `BatteryReader` (Task 6) may need correcting against your real receiver.

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.App/DeviceRegistry.cs src/LogiBoltTray.App/PollingService.cs src/LogiBoltTray.App/App.xaml.cs
git commit -m "Wire live HID++ polling into the app via DeviceRegistry and PollingService"
```

---

## Task 16: Tray icon rendering and per-device NotifyIcon management

**Files:**
- Create: `src/LogiBoltTray.App/TrayBitmapRenderer.cs`
- Create: `src/LogiBoltTray.App/TrayIconManager.cs`
- Modify: `src/LogiBoltTray.App/App.xaml.cs`

**Interfaces:**
- Consumes: `LogiBoltDeviceInfo`, `DeviceType` (Task 9), `IconStyle`, `SettingsModel`, `SettingsStore`, `BatteryColor`, `BatteryColorPolicy` (Task 10, 11), `DeviceRegistry` (Task 15).
- Produces: `TrayBitmapRenderer` with `static Icon Render(LogiBoltDeviceInfo device, IconStyle style)`. `TrayIconManager` with `ctor(DeviceRegistry registry, SettingsStore settingsStore, Action onIconClicked)`, subscribes to `registry.Updated` and keeps one `NotifyIcon` per visible device in sync (create/update/remove + tooltip text). Consumed by Task 18 (clicking any icon opens `MainWindow`).

- [ ] **Step 1: Implement `TrayBitmapRenderer`**

```csharp
using System.Drawing;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public static class TrayBitmapRenderer
{
    private static Color ToColor(BatteryColor color) => color switch
    {
        BatteryColor.Red => Color.FromArgb(192, 57, 43),
        BatteryColor.Orange => Color.FromArgb(230, 126, 34),
        BatteryColor.Green => Color.FromArgb(46, 204, 113),
        BatteryColor.Purple => Color.FromArgb(142, 68, 173),
        _ => Color.Gray,
    };

    public static Icon Render(LogiBoltDeviceInfo device, IconStyle style)
    {
        Color color = ToColor(BatteryColorPolicy.GetColor(device.Battery.Percent));
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);

        switch (style)
        {
            case IconStyle.A_Number:
                DrawNumberStyle(g, device.Battery.Percent, color);
                break;
            case IconStyle.B_GlyphBadge:
                DrawGlyphBadgeStyle(g, device.Type, color);
                break;
            case IconStyle.C_BatteryBar:
                DrawBatteryBarStyle(g, device.Battery.Percent, color);
                break;
            case IconStyle.D_Hybrid:
                DrawHybridStyle(g, device, color);
                break;
        }

        nint hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static void DrawNumberStyle(Graphics g, int percent, Color color)
    {
        g.FillRectangle(new SolidBrush(color), 0, 0, 16, 16);
        using var font = new Font("Segoe UI", 7, System.Drawing.FontStyle.Bold);
        var textSize = g.MeasureString(percent.ToString(), font);
        g.DrawString(percent.ToString(), font, Brushes.Black, (16 - textSize.Width) / 2, (16 - textSize.Height) / 2);
    }

    private static void DrawGlyphBadgeStyle(Graphics g, DeviceType type, Color color)
    {
        using var font = new Font("Segoe UI Emoji", 9);
        string glyph = type switch
        {
            DeviceType.Mouse => "\U0001F5B1",
            DeviceType.Keyboard => "⌨",
            DeviceType.Headset => "\U0001F3A7",
            DeviceType.Trackball => "●",
            _ => "•",
        };
        g.DrawString(glyph, font, Brushes.White, -2, -2);
        g.FillEllipse(new SolidBrush(color), 9, 0, 7, 7);
    }

    private static void DrawBatteryBarStyle(Graphics g, int percent, Color color)
    {
        g.DrawRectangle(Pens.Gray, 1, 4, 12, 8);
        g.FillRectangle(Brushes.Gray, 13, 6, 2, 4);
        int fillWidth = (int)Math.Round(10 * (percent / 100.0));
        g.FillRectangle(new SolidBrush(color), 2, 5, Math.Max(0, fillWidth), 6);
    }

    private static void DrawHybridStyle(Graphics g, LogiBoltDeviceInfo device, Color color)
    {
        g.FillRectangle(Brushes.DimGray, 0, 0, 16, 8);
        using var glyphFont = new Font("Segoe UI Emoji", 6);
        string glyph = device.Type switch
        {
            DeviceType.Mouse => "\U0001F5B1",
            DeviceType.Keyboard => "⌨",
            DeviceType.Headset => "\U0001F3A7",
            _ => "●",
        };
        g.DrawString(glyph, glyphFont, Brushes.White, 2, -2);

        using var pctFont = new Font("Segoe UI", 6, System.Drawing.FontStyle.Bold);
        string pctText = device.Battery.Percent.ToString();
        var textSize = g.MeasureString(pctText, pctFont);
        g.DrawString(pctText, pctFont, new SolidBrush(color), (16 - textSize.Width) / 2, 8);
    }
}
```

- [ ] **Step 2: Implement `TrayIconManager`**

```csharp
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public sealed class TrayIconManager : IDisposable
{
    private readonly DeviceRegistry _registry;
    private readonly SettingsStore _settingsStore;
    private readonly Action _onIconClicked;
    private readonly Dictionary<byte, NotifyIcon> _icons = new();

    public TrayIconManager(DeviceRegistry registry, SettingsStore settingsStore, Action onIconClicked)
    {
        _registry = registry;
        _settingsStore = settingsStore;
        _onIconClicked = onIconClicked;
        _registry.Updated += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var settings = _settingsStore.Load();
        var currentTracked = _registry.Devices;
        var currentIndices = new HashSet<byte>(currentTracked.Select(d => d.Info.DeviceIndex));

        foreach (byte staleIndex in _icons.Keys.Except(currentIndices).ToList())
        {
            DisposeRenderedIcon(_icons[staleIndex].Icon);
            _icons[staleIndex].Visible = false;
            _icons[staleIndex].Dispose();
            _icons.Remove(staleIndex);
        }

        foreach (TrackedDevice tracked in currentTracked)
        {
            LogiBoltDeviceInfo device = tracked.Info;
            bool visible = !settings.DeviceVisibility.TryGetValue(device.Name, out bool hidden) || !hidden;
            // DeviceVisibility maps name -> hidden=true means "don't show"; absent key defaults to visible.

            if (!_icons.TryGetValue(device.DeviceIndex, out var icon))
            {
                icon = new NotifyIcon { Visible = false };
                icon.Click += (_, _) => _onIconClicked();
                _icons[device.DeviceIndex] = icon;
            }

            // Icon.FromHandle (used by TrayBitmapRenderer) wraps a raw GDI handle that .NET does
            // NOT free on Icon.Dispose() — without DestroyIcon this leaks one GDI handle per
            // redraw and eventually exhausts the process's GDI handle quota on a long-running
            // tray app that redraws every poll cycle.
            Icon? previousIcon = icon.Icon;
            icon.Icon = TrayBitmapRenderer.Render(device, settings.IconStyle);
            DisposeRenderedIcon(previousIcon);

            string status = device.Battery.IsCharging ? "заряжается" : "не заряжается";
            string staleSuffix = tracked.IsStale ? " — устарело" : "";
            icon.Text = Truncate($"{device.Name}: {device.Battery.Percent}% ({status}){staleSuffix}", 63); // NotifyIcon.Text max length
            icon.Visible = visible;
        }
    }

    private static void DisposeRenderedIcon(Icon? icon)
    {
        if (icon is null)
        {
            return;
        }

        NativeMethods.DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    public void Dispose()
    {
        _registry.Updated -= Refresh;
        foreach (var icon in _icons.Values)
        {
            DisposeRenderedIcon(icon.Icon);
            icon.Visible = false;
            icon.Dispose();
        }
        _icons.Clear();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
```

- [ ] **Step 3: Wire it into `App.xaml.cs`**

```csharp
// Add field:
private TrayIconManager? _trayIconManager;

// After _pollingService.Start() in OnStartup:
_trayIconManager = new TrayIconManager(_deviceRegistry!, _settingsStore!, onIconClicked: ShowMainWindow);

// Add a placeholder until Task 18 implements it for real:
private void ShowMainWindow()
{
    // Implemented in Task 18.
}

// Add to OnExit, alongside _pollingService?.Dispose():
_trayIconManager?.Dispose();
```

- [ ] **Step 4: Manual verification on Windows**

Run the app with a real device connected. Expect: one tray icon per paired device, rendered in style B (the default) with the correct emoji glyph and a colored badge matching the current battery percentage's threshold color. Hover each icon and confirm the tooltip shows the right device name, percentage, and charging state. Unplug a paired device (or turn it off) and confirm its tooltip gains an "— устарело" suffix after the next poll (one grace cycle, per Task 15's `DeviceRegistry` merge logic), then confirm the icon disappears entirely after a second consecutive missed poll; reconnect it and confirm the icon reappears without a stale marker. Separately, leave the app running for a while (or drop the poll interval to 15s and wait several minutes) and spot-check via Task Manager's "GDI objects" column that the process's GDI handle count stays roughly flat across many redraws instead of climbing — this confirms the `DestroyIcon` cleanup in `TrayIconManager` is actually working.

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.App/TrayBitmapRenderer.cs src/LogiBoltTray.App/TrayIconManager.cs src/LogiBoltTray.App/App.xaml.cs
git commit -m "Render per-device tray icons (style B default) with battery color thresholds"
```

---

## Task 17: Icon style live preview helper (shared by Settings window)

**Files:**
- Create: `src/LogiBoltTray.App/IconStylePreview.cs`

**Interfaces:**
- Consumes: `TrayBitmapRenderer` (Task 16), `LogiBoltDeviceInfo`, `DeviceType`, `BatteryStatus` (Task 7, 9).
- Produces: `static class IconStylePreview` with `static Icon[] RenderAllStyles(int samplePercent, DeviceType sampleType)` — renders the same sample device at a given percent across all four `IconStyle` values, in enum declaration order (`A_Number, B_GlyphBadge, C_BatteryBar, D_Hybrid`). Consumed by Task 19 (`SettingsView`) to show a live preview row next to the style picker.

- [ ] **Step 1: Implement**

```csharp
using System.Drawing;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public static class IconStylePreview
{
    public static Icon[] RenderAllStyles(int samplePercent, DeviceType sampleType)
    {
        var sampleDevice = new LogiBoltDeviceInfo(
            DeviceIndex: 0x01,
            Name: "Preview",
            Type: sampleType,
            Battery: new BatteryStatus(samplePercent, IsCharging: false, IsUnknown: false));

        return Enum.GetValues<IconStyle>()
            .Select(style => TrayBitmapRenderer.Render(sampleDevice, style))
            .ToArray();
    }
}
```

- [ ] **Step 2: Manual verification on Windows**

This is a pure rendering helper with no window of its own yet — it's exercised visually once Task 19 builds the settings UI on top of it. No standalone verification needed here; proceed to Task 18.

- [ ] **Step 3: Commit**

```bash
git add src/LogiBoltTray.App/IconStylePreview.cs
git commit -m "Add icon style preview helper for the settings window"
```

---

## Task 18: Main window — device list, Refresh, Close

**Files:**
- Modify: `src/LogiBoltTray.App/MainWindow.xaml`
- Modify: `src/LogiBoltTray.App/MainWindow.xaml.cs`
- Modify: `src/LogiBoltTray.App/App.xaml.cs`

**Interfaces:**
- Consumes: `DeviceRegistry`, `PollingService` (Task 15), `LogiBoltDeviceInfo` (Task 9).
- Produces: `MainWindow` showing a live-updating device list, a "Обновить" button calling `PollingService.PollNow()`, and a "Закрыть" button that fully exits the app (`Application.Current.Shutdown()`). `App.ShowMainWindow()` now creates the window on first call and reuses/activates it on subsequent tray icon clicks. Consumed by Task 19, which adds the Settings section to the same window.

- [ ] **Step 1: Write `MainWindow.xaml`**

```xml
<Window x:Class="LogiBoltTray.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="LogiBoltTray" Height="420" Width="480" WindowStartupLocation="CenterScreen">
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button x:Name="RefreshButton" Content="Обновить" Width="100" Margin="0,0,8,0" Click="RefreshButton_Click" />
            <Button x:Name="CloseButton" Content="Закрыть" Width="100" Click="CloseButton_Click" />
        </StackPanel>
        <ListView x:Name="DeviceListView">
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="Устройство" DisplayMemberBinding="{Binding Name}" Width="180" />
                    <GridViewColumn Header="Тип" DisplayMemberBinding="{Binding Type}" Width="100" />
                    <GridViewColumn Header="Заряд" DisplayMemberBinding="{Binding PercentText}" Width="80" />
                    <GridViewColumn Header="Статус" DisplayMemberBinding="{Binding StatusText}" Width="100" />
                </GridView>
            </ListView.View>
        </ListView>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Write `MainWindow.xaml.cs`**

```csharp
using System.Windows;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public sealed class DeviceRow
{
    public required string Name { get; init; }
    public required DeviceType Type { get; init; }
    public required string PercentText { get; init; }
    public required string StatusText { get; init; }
}

public partial class MainWindow : Window
{
    private readonly DeviceRegistry _registry;
    private readonly PollingService _pollingService;

    public MainWindow(DeviceRegistry registry, PollingService pollingService)
    {
        InitializeComponent();
        _registry = registry;
        _pollingService = pollingService;
        _registry.Updated += RefreshList;
        RefreshList();
    }

    private void RefreshList()
    {
        Dispatcher.Invoke(() =>
        {
            DeviceListView.ItemsSource = _registry.Devices.Select(t => new DeviceRow
            {
                Name = t.Info.Name,
                Type = t.Info.Type,
                PercentText = $"{t.Info.Battery.Percent}%",
                StatusText = (t.Info.Battery.IsCharging ? "Заряжается" : "Подключено") + (t.IsStale ? " (устарело)" : ""),
            }).ToList();
        });
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _pollingService.PollNow();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    protected override void OnClosed(EventArgs e)
    {
        _registry.Updated -= RefreshList;
        base.OnClosed(e);
    }
}
```

- [ ] **Step 3: Implement `App.ShowMainWindow` for real**

```csharp
// Replace the placeholder from Task 16:
private MainWindow? _mainWindow;

private void ShowMainWindow()
{
    if (_mainWindow is null)
    {
        _mainWindow = new MainWindow(_deviceRegistry!, _pollingService!);
        _mainWindow.Closed += (_, _) => _mainWindow = null;
    }

    _mainWindow.Show();
    _mainWindow.Activate();
}
```

Also change `CloseButton_Click`'s shutdown path to go through the same app-wide teardown used elsewhere — `Application.Current.Shutdown()` already triggers `App.OnExit`, which Task 15/16 wired to dispose `_pollingService` and `_trayIconManager`, so no extra change is needed here.

- [ ] **Step 4: Manual verification on Windows**

Run the app, click any tray icon — the window opens showing the live device list. Click "Обновить" and confirm the list refreshes (add a temporary artificial delay or just trust the timer log from Task 15 if the values are already fresh). Click "Закрыть" and confirm the process fully exits and every tray icon disappears (check Task Manager). Click a tray icon again to reopen the window, close via the window's own X button, and confirm that also fully exits the app (per the design decision that the window has no "just hide" mode).

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.App/MainWindow.xaml src/LogiBoltTray.App/MainWindow.xaml.cs src/LogiBoltTray.App/App.xaml.cs
git commit -m "Add main window with live device list, Refresh, and full-exit Close"
```

---

## Task 19: Settings section — poll interval, icon style, per-device visibility

**Files:**
- Modify: `src/LogiBoltTray.App/MainWindow.xaml`
- Modify: `src/LogiBoltTray.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `SettingsModel`, `IconStyle`, `PollIntervalPolicy` (Task 10, 13), `IconStylePreview` (Task 17), `PollingService.Restart()` (Task 15).
- Produces: a "Настройки" section in the same window: poll interval numeric input, icon style radio buttons with live preview images, a per-device visibility checkbox list, save-on-change behavior. This is the last piece the design's main window section requires — autostart and low-battery toggles are added in Task 20-21 in the same section.

- [ ] **Step 1: Extend `MainWindow.xaml`** — add a settings `GroupBox` below the device list, inside the `DockPanel` (before the button row, after the `ListView`):

```xml
<GroupBox Header="Настройки" DockPanel.Dock="Bottom" Margin="0,12,0,0">
    <StackPanel Margin="8">
        <DockPanel Margin="0,0,0,8">
            <TextBlock Text="Интервал опроса (сек):" VerticalAlignment="Center" Width="180" />
            <TextBox x:Name="PollIntervalTextBox" Width="80" LostFocus="PollIntervalTextBox_LostFocus" />
        </DockPanel>
        <TextBlock Text="Стиль иконки:" Margin="0,0,0,4" />
        <StackPanel x:Name="IconStylePanel" Orientation="Horizontal" Margin="0,0,0,8" />
        <TextBlock Text="Показывать в трее:" Margin="0,0,0,4" />
        <StackPanel x:Name="DeviceVisibilityPanel" Orientation="Vertical" />
    </StackPanel>
</GroupBox>
```

- [ ] **Step 2: Extend `MainWindow.xaml.cs`** — add settings wiring alongside the existing device-list wiring:

Add these usings at the top of the file (alongside the existing `using System.Windows;` and `using LogiBoltTray.Protocol;` from Task 18) — `Imaging`/`BitmapSource` are needed to convert the `System.Drawing.Icon` previews from `IconStylePreview` into something a WPF `Image` control can display, and `DllImport` is needed for the `NativeMethods.DestroyIcon` cleanup:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LogiBoltTray.Core;
```

```csharp
// Add fields:
private readonly SettingsStore _settingsStore;
private SettingsModel _settings = null!;
private bool _isLoadingSettings;

// Change the constructor signature and body:
public MainWindow(DeviceRegistry registry, PollingService pollingService, SettingsStore settingsStore)
{
    InitializeComponent();
    _registry = registry;
    _pollingService = pollingService;
    _settingsStore = settingsStore;
    _registry.Updated += RefreshList;
    RefreshList();
    LoadSettingsIntoUi();
}

private void LoadSettingsIntoUi()
{
    _isLoadingSettings = true;
    _settings = _settingsStore.Load();

    PollIntervalTextBox.Text = _settings.PollIntervalSeconds.ToString();

    BuildIconStylePanel();
    RebuildDeviceVisibilityPanel();
    _isLoadingSettings = false;
}

private void BuildIconStylePanel()
{
    IconStylePanel.Children.Clear();

    IconStyle[] styles = Enum.GetValues<IconStyle>();
    // Sample device used purely to render a representative preview icon per style — 76% on a
    // mouse is an arbitrary mid-range choice with nothing charging, so all four styles show a
    // clearly non-edge-case color/shape.
    System.Drawing.Icon[] previewIcons = IconStylePreview.RenderAllStyles(samplePercent: 76, sampleType: DeviceType.Mouse);

    try
    {
        for (int i = 0; i < styles.Length; i++)
        {
            IconStyle style = styles[i];
            BitmapSource previewSource = Imaging.CreateBitmapSourceFromHIcon(
                previewIcons[i].Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            var content = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            content.Children.Add(new System.Windows.Controls.Image
            {
                Source = previewSource,
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 2),
            });
            content.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = style.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10,
            });

            var radio = new System.Windows.Controls.RadioButton
            {
                Content = content,
                GroupName = "IconStyle",
                IsChecked = style == _settings.IconStyle,
                Tag = style,
                Margin = new Thickness(0, 0, 12, 0),
            };
            radio.Checked += IconStyleRadio_Checked;
            IconStylePanel.Children.Add(radio);
        }
    }
    finally
    {
        // Imaging.CreateBitmapSourceFromHIcon copies pixel data into a managed BitmapSource, so
        // the raw HICONs from IconStylePreview are no longer needed after the loop above — release
        // them immediately (same DestroyIcon pattern as TrayIconManager, see Task 16/17) rather than
        // leaking one GDI handle per style every time this panel rebuilds.
        foreach (System.Drawing.Icon icon in previewIcons)
        {
            NativeMethods.DestroyIcon(icon.Handle);
            icon.Dispose();
        }
    }
}

private static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}

private void RebuildDeviceVisibilityPanel()
{
    DeviceVisibilityPanel.Children.Clear();
    foreach (var tracked in _registry.Devices)
    {
        string deviceName = tracked.Info.Name;
        bool hidden = _settings.DeviceVisibility.TryGetValue(deviceName, out bool h) && h;
        var checkBox = new System.Windows.Controls.CheckBox
        {
            Content = deviceName,
            IsChecked = !hidden,
            Tag = deviceName,
        };
        checkBox.Checked += DeviceVisibilityCheckBox_Changed;
        checkBox.Unchecked += DeviceVisibilityCheckBox_Changed;
        DeviceVisibilityPanel.Children.Add(checkBox);
    }
}

private void PollIntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
{
    if (_isLoadingSettings) return;
    if (!int.TryParse(PollIntervalTextBox.Text, out int requested)) return;

    int clamped = PollIntervalPolicy.Clamp(requested);
    PollIntervalTextBox.Text = clamped.ToString();
    _settings.PollIntervalSeconds = clamped;
    _settingsStore.Save(_settings);
    _pollingService.Restart();
}

private void IconStyleRadio_Checked(object sender, RoutedEventArgs e)
{
    if (_isLoadingSettings) return;
    var radio = (System.Windows.Controls.RadioButton)sender;
    _settings.IconStyle = (IconStyle)radio.Tag;
    _settingsStore.Save(_settings);
    _pollingService.PollNow(); // forces TrayIconManager to redraw with the new style on next Updated event
}

private void DeviceVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
{
    if (_isLoadingSettings) return;
    var checkBox = (System.Windows.Controls.CheckBox)sender;
    string deviceName = (string)checkBox.Tag;
    _settings.DeviceVisibility[deviceName] = checkBox.IsChecked != true;
    _settingsStore.Save(_settings);
    _pollingService.PollNow();
}
```

Also add `RebuildDeviceVisibilityPanel();` as the first line inside the existing `RefreshList()`'s `Dispatcher.Invoke` block (after setting `DeviceListView.ItemsSource`), so newly discovered devices get a visibility checkbox without waiting for the settings section to reload.

- [ ] **Step 3: Update the `App.ShowMainWindow` call site** to pass the settings store:

```csharp
_mainWindow = new MainWindow(_deviceRegistry!, _pollingService!, _settingsStore!);
```

- [ ] **Step 4: Manual verification on Windows**

Open the window and confirm the "Стиль иконки" row shows 4 actual rendered preview images (not just text labels) — each should visibly match its style's real appearance (A: number on a colored square, B: mouse glyph + colored badge, C: battery bar, D: hybrid). Change the poll interval to something small like 15, tab away from the field, and confirm (via the Task 15 debug log or just watching the tray icons update sooner) that polling actually sped up. Switch icon style to each of A/B/C/D and confirm all tray icons re-render in the new style within one poll cycle. Uncheck a device's visibility checkbox and confirm its tray icon disappears immediately (via the forced `PollNow()`); re-check it and confirm it reappears. Close and reopen the app (fully, via "Закрыть" then relaunch) and confirm all three settings persisted.

- [ ] **Step 5: Commit**

```bash
git add src/LogiBoltTray.App/MainWindow.xaml src/LogiBoltTray.App/MainWindow.xaml.cs src/LogiBoltTray.App/App.xaml.cs
git commit -m "Add settings section: poll interval, icon style, per-device visibility"
```

---

## Task 20: Low battery notifications wiring

**Files:**
- Modify: `src/LogiBoltTray.App/PollingService.cs`
- Modify: `src/LogiBoltTray.App/MainWindow.xaml`
- Modify: `src/LogiBoltTray.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `LowBatteryNotifier` (Task 12), `SettingsModel.LowBatteryNotificationsEnabled` / `LowBatteryThresholdPercent` (Task 10), `NotifyIcon.ShowBalloonTip` (WinForms, via `TrayIconManager`'s icons).
- Produces: `PollingService` raises a `event Action<LogiBoltDeviceInfo>? LowBatteryDetected` after each poll; `App` subscribes and shows a balloon tip using any one of `TrayIconManager`'s live `NotifyIcon`s. Settings section gains a checkbox + numeric threshold for this.

- [ ] **Step 1: Make `LowBatteryNotifier`'s threshold updatable in place**

`LowBatteryNotifier` (Task 12) currently takes its threshold once in the constructor. Settings can change the threshold at runtime, but `PollingService` must keep using the *same* notifier instance across polls — constructing a new one on every settings change would wipe the `_alreadyNotifiedBelowThreshold` tracking and could re-fire notifications that already fired. In `src/LogiBoltTray.Core/LowBatteryNotifier.cs`, change the field from `private readonly int _thresholdPercent;` to `private int _thresholdPercent;`, and add:

```csharp
public void UpdateThreshold(int thresholdPercent) => _thresholdPercent = thresholdPercent;
```

- [ ] **Step 2: Test `UpdateThreshold` preserves notified state**

Add to `tests/LogiBoltTray.Core.Tests/LowBatteryNotifierTests.cs`:

```csharp
[Fact]
public void UpdateThreshold_ChangesThresholdWithoutResettingNotifiedState()
{
    var notifier = new LowBatteryNotifier(thresholdPercent: 20);
    Assert.True(notifier.ShouldNotify("mouse-1", 15)); // notifies once at threshold 20

    notifier.UpdateThreshold(10);

    Assert.False(notifier.ShouldNotify("mouse-1", 12)); // above new threshold 10, no notification
    Assert.True(notifier.ShouldNotify("mouse-1", 8));   // below new threshold, notifies again
}
```

Run: `dotnet test tests/LogiBoltTray.Core.Tests --filter LowBatteryNotifierTests`
Expected: PASS (5 tests, including the new one)

- [ ] **Step 3: Wire the notifier and event into `PollingService`**

In `src/LogiBoltTray.App/PollingService.cs`, add the field, event, and constructor initialization:

```csharp
// Add using:
using LogiBoltTray.Core;

// Add field (constructed once — never replaced — so notified-state persists across polls):
private readonly LowBatteryNotifier _lowBatteryNotifier;

// Add event:
public event Action<LogiBoltDeviceInfo>? LowBatteryDetected;

// Change the constructor to initialize it from the current settings:
public PollingService(DeviceRegistry registry, SettingsStore settingsStore)
{
    _registry = registry;
    _settingsStore = settingsStore;
    _lowBatteryNotifier = new LowBatteryNotifier(settingsStore.Load().LowBatteryThresholdPercent);
}
```

Then update `PollNow()`: load `settings` once at the top (if it doesn't already), push the current threshold into the notifier before scanning, and check for crossings after `_registry.Replace(allDevices);`:

```csharp
public void PollNow()
{
    var settings = _settingsStore.Load();
    _lowBatteryNotifier.UpdateThreshold(settings.LowBatteryThresholdPercent);

    var allDevices = new List<LogiBoltDeviceInfo>();

    // v1 supports exactly one physical Bolt receiver at a time (see Task 15) — only the first
    // interface found is polled, to avoid a device-index collision across receivers.
    var receiverInterface = ReceiverEnumerator.FindBoltInterfaces().FirstOrDefault();
    if (receiverInterface is not null)
    {
        try
        {
            using var hidDevice = receiverInterface.ConnectToDevice();
            using var transport = new HidppHidTransport(hidDevice);
            allDevices.AddRange(new DeviceDiscoveryService(transport).DiscoverDevices());
        }
        catch (HidApi.HidException)
        {
            // Receiver interface disappeared between enumeration and connect (unplugged) — skip this cycle.
        }
    }

    _registry.Replace(allDevices);

    if (settings.LowBatteryNotificationsEnabled)
    {
        foreach (var device in allDevices)
        {
            if (_lowBatteryNotifier.ShouldNotify(device.Name, device.Battery.Percent))
            {
                LowBatteryDetected?.Invoke(device);
            }
        }
    }
}
```

This replaces the entire `PollNow()` method body written in Task 15 Step 2 — the receiver-scanning loop in the middle is unchanged from Task 15, only the settings load at the top and the notification check at the bottom are new.

- [ ] **Step 4: Subscribe to the event in `App.xaml.cs`**

```csharp
// After _trayIconManager is created in OnStartup:
_pollingService!.LowBatteryDetected += device =>
{
    Dispatcher.Invoke(() => _trayIconManager?.ShowBalloonTip(device));
};
```

- [ ] **Step 5: Add `ShowBalloonTip` to `TrayIconManager`**

```csharp
// Add to TrayIconManager in src/LogiBoltTray.App/TrayIconManager.cs:
public void ShowBalloonTip(LogiBoltDeviceInfo device)
{
    if (_icons.TryGetValue(device.DeviceIndex, out var icon))
    {
        icon.BalloonTipTitle = "Низкий заряд";
        icon.BalloonTipText = $"{device.Name}: {device.Battery.Percent}%";
        icon.ShowBalloonTip(5000);
    }
}
```

- [ ] **Step 6: Add the notification settings UI** — extend the `GroupBox` in `MainWindow.xaml` (inside the same `StackPanel`, after the device visibility panel):

```xml
<CheckBox x:Name="LowBatteryEnabledCheckBox" Content="Уведомлять о низком заряде" Margin="0,8,0,4" Checked="LowBatteryEnabledCheckBox_Changed" Unchecked="LowBatteryEnabledCheckBox_Changed" />
<DockPanel>
    <TextBlock Text="Порог (%):" VerticalAlignment="Center" Width="180" />
    <TextBox x:Name="LowBatteryThresholdTextBox" Width="80" LostFocus="LowBatteryThresholdTextBox_LostFocus" />
</DockPanel>
```

And in `MainWindow.xaml.cs`, add to `LoadSettingsIntoUi()`:

```csharp
LowBatteryEnabledCheckBox.IsChecked = _settings.LowBatteryNotificationsEnabled;
LowBatteryThresholdTextBox.Text = _settings.LowBatteryThresholdPercent.ToString();
```

And add the two handlers:

```csharp
private void LowBatteryEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
{
    if (_isLoadingSettings) return;
    _settings.LowBatteryNotificationsEnabled = LowBatteryEnabledCheckBox.IsChecked == true;
    _settingsStore.Save(_settings);
}

private void LowBatteryThresholdTextBox_LostFocus(object sender, RoutedEventArgs e)
{
    if (_isLoadingSettings) return;
    if (!int.TryParse(LowBatteryThresholdTextBox.Text, out int requested)) return;

    int clamped = Math.Clamp(requested, 1, 99);
    LowBatteryThresholdTextBox.Text = clamped.ToString();
    _settings.LowBatteryThresholdPercent = clamped;
    _settingsStore.Save(_settings);
}
```

- [ ] **Step 7: Manual verification on Windows**

Temporarily set the threshold to a value just above your current real device's battery percentage (e.g. if the mouse is at 76%, set threshold to 80) and confirm a Windows balloon tip appears within one poll cycle reading "Низкий заряд" with the device name and percentage. Set it back down and confirm no further balloon appears while staying below the threshold across multiple poll cycles (it should only fire once per crossing, per Task 12's design).

- [ ] **Step 8: Commit**

```bash
git add src/LogiBoltTray.Core/LowBatteryNotifier.cs tests/LogiBoltTray.Core.Tests/LowBatteryNotifierTests.cs src/LogiBoltTray.App/PollingService.cs src/LogiBoltTray.App/TrayIconManager.cs src/LogiBoltTray.App/MainWindow.xaml src/LogiBoltTray.App/MainWindow.xaml.cs src/LogiBoltTray.App/App.xaml.cs
git commit -m "Wire low battery balloon notifications with configurable threshold"
```

---

## Task 21: Autostart with Windows

**Files:**
- Create: `src/LogiBoltTray.App/AutostartManager.cs`
- Modify: `src/LogiBoltTray.App/MainWindow.xaml`
- Modify: `src/LogiBoltTray.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SettingsModel.AutostartEnabled` (Task 10), `Microsoft.Win32.Registry` (BCL, Windows-only at runtime).
- Produces: `AutostartManager` with `static void SetEnabled(bool enabled)`, `static bool IsEnabled()` — reads/writes `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\LogiBoltTray`. Wired to a settings checkbox; this is the last item from the spec's feature list.

- [ ] **Step 1: Implement `AutostartManager`**

```csharp
using Microsoft.Win32;

namespace LogiBoltTray.App;

public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LogiBoltTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
```

- [ ] **Step 2: Add the settings UI** — extend `MainWindow.xaml`'s settings `GroupBox` (after the low-battery threshold row):

```xml
<CheckBox x:Name="AutostartCheckBox" Content="Запускать вместе с Windows" Margin="0,8,0,0" Checked="AutostartCheckBox_Changed" Unchecked="AutostartCheckBox_Changed" />
```

And in `MainWindow.xaml.cs`, add to `LoadSettingsIntoUi()`:

```csharp
AutostartCheckBox.IsChecked = _settings.AutostartEnabled;
```

And add the handler:

```csharp
private void AutostartCheckBox_Changed(object sender, RoutedEventArgs e)
{
    if (_isLoadingSettings) return;
    bool enabled = AutostartCheckBox.IsChecked == true;
    AutostartManager.SetEnabled(enabled);
    _settings.AutostartEnabled = enabled;
    _settingsStore.Save(_settings);
}
```

- [ ] **Step 3: Manual verification on Windows**

Check the autostart checkbox, then open `regedit` and confirm `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` now has a `LogiBoltTray` value pointing at the built exe path. Uncheck it and confirm the value is removed. Optionally log out/in (or reboot) with it checked and confirm the app actually launches automatically with tray icons appearing.

- [ ] **Step 4: Commit**

```bash
git add src/LogiBoltTray.App/AutostartManager.cs src/LogiBoltTray.App/MainWindow.xaml src/LogiBoltTray.App/MainWindow.xaml.cs
git commit -m "Add Windows autostart toggle via HKCU Run key"
```

---

## Task 22: Publish instructions and README

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: nothing — this is documentation for the user's own future builds.
- Produces: build/publish instructions so the user doesn't have to reconstruct the `dotnet publish` invocation from memory later.

- [ ] **Step 1: Write `README.md`**

```markdown
# LogiBoltTray

Windows tray app showing battery percentage for devices connected via a Logitech Bolt receiver.
See `docs/superpowers/specs/2026-09-13-logi-bolt-tray-design.md` for the full design.

## Build (Windows only)

    dotnet build LogiBoltTray.sln

## Run

    dotnet run --project src/LogiBoltTray.App

## Publish a self-contained single-file exe

    dotnet publish src/LogiBoltTray.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish

## Run tests (cross-platform parts only)

    dotnet test tests/LogiBoltTray.Protocol.Tests
    dotnet test tests/LogiBoltTray.Core.Tests
```

- [ ] **Step 2: Manual verification on Windows**

Run the publish command above and confirm `./publish/LogiBoltTray.exe` exists and launches standalone (double-click it, not via `dotnet run`) with tray icons appearing, on a machine that doesn't have the .NET runtime installed separately (self-contained publish should not need it).

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Add build, run, and publish instructions"
```
