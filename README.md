# LogiBoltTray

A lightweight Windows system tray app that shows live battery percentage for every Logitech
device connected through a **Logi Bolt** receiver — no Logi Options+ / G HUB required. It talks
to the receiver directly over the HID++ 2.0 protocol.

![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-8-512BD4)

## Features

- **One tray icon per device**, colored by charge level and tinted blue while charging.
- **Three icon styles**, switchable live in Settings:
  - **A — Number**: percentage on a colored background.
  - **B — Letter badge**: device-type letter (M/K/H/T) with a colored corner badge.
  - **C — Battery bar**: a filled battery pictogram.
- Click any tray icon to open the main window: live device list, manual refresh, and settings.
- Configurable poll interval (1 second – 30 minutes).
- Per-device visibility toggle (hide a device's tray icon without disabling it).
- Low-battery balloon notification with a configurable threshold.
- Launch with Windows (optional, toggled in Settings).
- Closing the window or clicking the X just minimizes it back to the tray — the tray icons are
  the persistent interface. Use the **Выход** (Exit) button to actually quit.
- Built-in diagnostics: `diagnostics.log` records every poll's outcome, and `--diagnose` dumps
  every Logitech HID interface Windows can see, for troubleshooting a receiver that isn't detected.

## Requirements

- Windows 10 or 11 (x64)
- A Logitech Bolt USB receiver, with at least one paired device
- Nothing else — no Logi Options+, no G HUB, no .NET runtime install needed if you use the
  self-contained build from [Releases](../../releases)

## Installation

Download the latest `LogiBoltTray-win-x64.zip` from the [Releases](../../releases) page, extract
it anywhere, and run `LogiBoltTray.exe`. It's self-contained — no separate .NET install required.

There's no installer: it doesn't write anything outside `%AppData%\LogiBoltTray\` (settings and
logs) and, optionally, the Windows startup registry key if you enable "Запускать вместе с
Windows" in Settings. To uninstall, just delete the folder (and untick autostart first, if it was
enabled).

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build LogiBoltTray.sln
dotnet run --project src/LogiBoltTray.App
```

### Publish a self-contained single-file exe

```
dotnet publish src/LogiBoltTray.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

`native/win-x64/hidapi.dll` (see below) is copied to the output/publish directory automatically —
no extra step needed, and no `-p:IncludeNativeLibrariesForSelfExtract` flag required.

### Run the tests

Only `LogiBoltTray.Protocol` and `LogiBoltTray.Core` are plain, cross-platform `net8.0` class
libraries with real automated tests — the WPF/WinForms app itself has no automated tests (WPF
requires Windows to build at all) and is verified by hand.

```
dotnet test tests/LogiBoltTray.Protocol.Tests
dotnet test tests/LogiBoltTray.Core.Tests
```

## Native dependency: hidapi.dll

The `HidApi.Net` NuGet package contains only the managed C# binding — it does **not** bundle the
native library it wraps. The official `hidapi.dll` (x64, from
[libusb/hidapi](https://github.com/libusb/hidapi) 0.15.0 — licensed under GPLv3, BSD-3-Clause, or
the original HIDAPI license, at your choice) is vendored in this repository at
`native/win-x64/hidapi.dll` and copied next to the built exe automatically by
`LogiBoltTray.App.csproj`.

## Troubleshooting

The app never opens a window on its own, and a bad poll cycle is swallowed rather than crashing
the whole process — which also means a real problem (wrong receiver detected, feature not
supported, etc.) can otherwise fail *silently*. Three things help:

- **A placeholder tray icon** (a warning triangle) always appears if zero devices are ever found,
  so there's always something to click even when nothing else is working yet.
- **`%AppData%\LogiBoltTray\diagnostics.log`** — records every poll's outcome (status changes and
  failures, not every single cycle) and, per device, which HID++ feature answered with the raw
  response bytes. Check this first.
- **`LogiBoltTray.exe --diagnose`** — dumps every Logitech HID interface Windows can see
  (VID/PID/UsagePage/Usage/Interface/Path) to a message box and to
  `%AppData%\LogiBoltTray\diagnostic.txt`, then exits without starting the tray app. Use this if
  the receiver isn't detected at all — compare the output against
  `src/LogiBoltTray.Hid/BoltReceiverIds.cs`'s expected PID/UsagePage.

## Architecture

```
src/
  LogiBoltTray.Protocol/   HID++ 2.0 framing, feature discovery, battery reading, device naming.
                           Plain net8.0 — fully unit tested, no Windows dependency.
  LogiBoltTray.Hid/        Real hardware transport, wrapping the HidApi.Net / hidapi.dll binding.
                           Plain net8.0.
  LogiBoltTray.Core/       Settings persistence, icon color policy, low-battery notification
                           state machine, poll interval clamping. Plain net8.0 — unit tested.
  LogiBoltTray.App/        WPF + WinForms tray app: icons, main window, polling orchestration.
                           net8.0-windows — Windows-only, no automated tests.
tests/
  LogiBoltTray.Protocol.Tests/
  LogiBoltTray.Core.Tests/
```

The app talks to the receiver directly over HID++ 2.0 (feature discovery via the root feature,
then battery level via feature `0x1000`/`0x1004`/`0x1001` with a voltage-to-percentage fallback
table, device name/type via feature `0x0005`) rather than depending on Logitech's own software.

## Known limitations

- Only one physical Bolt receiver is polled at a time (the first one Windows reports). Supporting
  more would need a redesign of how devices are keyed internally.
- The device-type byte mapping for headsets and trackballs is unconfirmed — it hasn't been tested
  against real hardware of those kinds. Keyboards (`0x00`) and mice (`0x03`) are confirmed correct.
- Battery percentage for devices that only expose feature `0x1001` (raw voltage, no direct
  percentage) is an estimate from a generic Li-Po discharge curve, not a per-device calibration —
  it can be somewhat off for a specific device's actual battery.

## License

MIT — see [LICENSE](LICENSE). This covers this project's own code only;
`native/win-x64/hidapi.dll` is a third-party binary under its own license (see above).
