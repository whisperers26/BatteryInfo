# BatteryInfo

A personal Windows 11 tray app that shows the battery level of every connected peripheral
(Razer, Logitech, ROG, HUAWEI FreeClip 2, Betop/Beitong controllers, SteelSeries headsets, Apple AirPods and Beats)
in one flyout.

It talks to each device the way the vendor's own software does, so Synapse, G HUB, Armoury Crate,
BetopGame and SteelSeries GG do not need to be running. It only ever sends read commands, and to AirPods nothing
at all: see [docs/protocols](docs/protocols/README.md).

## Supported devices

| Vendor | Devices | Read through |
|---|---|---|
| Logitech | LIGHTSPEED and Unifying receivers and their HID++ devices, e.g. PRO X2 SUPERSTRIKE; also a mouse on its own cable | HID++ battery features 0x1004, 0x1000, 0x1001, 0x1F20 |
| ROG | OMNI receivers and the keyboards/mice paired to them, e.g. Azoth X; Azoth X on USB; Cetra Open Wireless earbuds (left/right/case) | ROG vendor reports `12 01` / `12 07`, Cetra `0xCC` |
| HUAWEI | FreeBuds/FreeClip earbuds over Bluetooth, e.g. FreeClip 2 (left/right/case) | HUAWEI serial protocol over RFCOMM; Windows' own (approximate) value as a fallback |
| Betop (Beitong) | 2.4 GHz controllers with plain-byte dongles, e.g. KP70C2 (also KP20/KP40/KP50/KP70 variants, untested) | Betop vendor report `02 15` |
| Razer | Basilisk V3 Pro 35K on its HyperSpeed dongle or cable (not yet tested on hardware) | Razer 90-byte feature report `07/80`, `07/84` |
| SteelSeries | Arctis Nova 5X (and Nova 5, untested) through its wireless base station | SteelSeries status report `00 B0` |
| Apple | AirPods (1st generation to AirPods 5), AirPods Pro 1-3, AirPods Max and Beats earbuds and headphones paired with the PC (tested with AirPods 4 ANC) | the battery broadcast AirPods send to nearby Apple devices, only listened to, in 10 % steps |

## Install and run

Download `BatteryInfoApp-win-Setup.exe` from the latest
[release](https://github.com/whisperers26/BatteryInfo/releases/latest) and run it. It installs BatteryInfo for
your user only (no admin rights), installs the .NET 10 Desktop Runtime if it is missing, and starts the app
in the tray. The installer is not code-signed, so Windows SmartScreen may ask you to confirm the first time
(**More info**, then **Run anyway**). Turn on **Start with Windows** in the flyout (or the tray menu) to
launch it at sign-in. Uninstall it from Windows Settings > Apps.

Run the installer from Explorer or your browser. An app that sandboxes what it starts (such as the Claude desktop
app's terminal) would install BatteryInfo only inside its sandbox, where Windows cannot see it: the Start menu
shortcut then has no icon and does not start it. BatteryInfo shows a warning when it finds itself installed that way.

### Building it yourself

```
powershell -File scripts/publish.ps1 -Start
```

This publishes a local build to `%LOCALAPPDATA%\Programs\BatteryInfo` and starts it, closing a running copy
of that build first. A local build does not update itself.

## Using the tray app

- Click the battery icon in the tray for the flyout; right-click it for Refresh, Start with Windows and Exit.
  Hovering shows every device's level in the tooltip.
- Only connected devices are listed: one that is asleep, switched off or unplugged disappears until it connects
  again (a device Windows will not let the app read stays, with the reason). Each row shows the level and
  whether the device is charging. Earbuds show left, right and case. Click a row for its last 7 days.
- Drag a row up or down to reorder the devices. The order is remembered, including for devices that are
  disconnected: one that comes back takes its old place.
- A level marked ≈ is approximate. "Estimated" means the device reports only a voltage or a coarse level.
  "Approx. from Windows" means the earbuds' own value could not be read (the HUAWEI battery channel is held by
  another app, or no AirPods broadcast is fresh), so Windows' value is shown, which can be about 5 points high;
  their 7-day chart shows only the left, right and case values, so it has a gap for that time.
- The gear button next to refresh opens the settings; they fold away again whenever the flyout closes. They set
  the low-battery alert level (10-30 %) and turn the alerts on or off. An alert fires once per discharge, when a
  device that is not charging reaches the level, as a Windows notification that stays in Notification Center
  (Win+N) until you dismiss it, so a banner hidden by Do Not Disturb is not lost. **Show first device's level in
  tray** fills the tray battery to the level of the first device in the list that has a live value (with a bolt
  while it charges, and red at or below the alert level while it does not); off (the default), the tray battery
  is always full. For earbuds that is the lower earbud: the case counts only when neither earbud has a value,
  though it still raises its own low-battery alert.
- Settings, history (kept 90 days), logs and the low-battery alerts already shown are in
  `%LOCALAPPDATA%\BatteryInfo`. The alerts are kept so that restarting the app does not repeat them.
- Updates come from this repository's releases. With **Update automatically** on (the default), the app
  checks a minute after it starts and every six hours, downloads a new version in the background and installs
  it the next time it starts; a notification says when one is ready. **Check for updates** in the flyout's settings or
  the tray menu checks now, and **Restart to update** installs a downloaded version right away.
- AirPods and Beats show what they broadcast to nearby Apple devices, so their levels come in 10 % steps: "≈80 %"
  means 80-89 %, and the row says "In 10 % steps". The app only listens, with Bluetooth on, and sends them nothing.
  - **Case:** AirPods report the case only while a pod is in it; an open, empty case reports nothing. So while you
    wear them, the case shows the level from the last time a pod was in it, for up to 6 hours, and the row says
    "Case read at 14:05". After that, or if it was never read, it shows "–".
  - **Telling yours apart:** AirPods change their broadcast address every few minutes and whenever a pod goes into
    the case or comes out. The app follows the address that appeared when they connected, or whose levels match the
    last ones shown, and otherwise the strongest one nearby (always so when the app starts with them already
    connected). A nearby pair of the same model can still be taken for yours.
  - **No broadcast:** the row says "Waiting for its Bluetooth battery broadcast", or for how long none has come, and
    keeps the last levels, dimmed; if Windows has its own value for them, that is shown instead. One report says
    some AirPods 4 firmware sends none to non-Apple devices.
- `BatteryInfo.exe --demo` shows sample devices in every state; `--theme=light|dark` overrides the Windows theme.
- In the demo, Start with Windows only flips the switch: it is not saved and does not change what starts at sign-in.

## Command line

```
dotnet run --project src/BatteryInfo.Cli -- list              # read every device once
dotnet run --project src/BatteryInfo.Cli -- list --json       # the same as JSON
dotnet run --project src/BatteryInfo.Cli -- list --vendor Logitech --trace   # one vendor, with device frames
dotnet run --project src/BatteryInfo.Cli -- apple-scan 30     # print the AirPods/Beats broadcasts in range for 30 s
```
