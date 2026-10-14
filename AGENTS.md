# BatteryInfo — agent guide

A personal Windows 11 tray app (.NET 10, WPF) that shows the battery level of the owner's
peripherals by speaking each vendor's device protocol directly. Only allow-listed read commands
may ever reach a device: some neighbouring vendor commands change settings or reboot a device
into firmware-update mode.

- Branches, PRs, CI and self-merging → [.agents/wiki/git-workflow.md](.agents/wiki/git-workflow.md)
- The owner's devices, allowed commands, live checks → [.agents/wiki/hardware-safety.md](.agents/wiki/hardware-safety.md)
- Unverified behaviour and open questions → [.agents/wiki/known-issues.md](.agents/wiki/known-issues.md)
- One reverse-engineered protocol per vendor → [docs/protocols/README.md](docs/protocols/README.md)
- Design spec → [docs/superpowers/specs/2026-10-14-battery-tray-app-design.md](docs/superpowers/specs/2026-10-14-battery-tray-app-design.md)

## Rules you must not break

1. **Never commit on `main`.** One branch per feature or fix, pushed right away, merged through a
   PR that you merge yourself with a merge commit once CI passes. → git-workflow.md
2. **Small commits, one topic each**, made as you go (the owner's global commit rules apply).
3. **Only allow-listed reads reach a device.** Every HID write goes through `GuardedHidConnection`
   with the vendor's `ICommandPolicy`; the HUAWEI socket checks its policy before every send.
   → hardware-safety.md
4. **`dotnet build` (warnings are errors) and `dotnet test` pass before every push.**
5. **Update the README** in the same PR when what the user sees changes.
6. **No NuGet packages** in `BatteryInfo.Core`, `BatteryInfo.App` or `BatteryInfo.Cli`.

## Commands

```
dotnet build BatteryInfo.slnx
dotnet test BatteryInfo.slnx
dotnet run --project src/BatteryInfo.Cli -- list --trace      # read every device once, frames on stderr
dotnet run --project src/BatteryInfo.Cli -- hid               # HID collections on this PC
powershell -File scripts/publish.ps1 -Start                   # install to %LOCALAPPDATA%\Programs\BatteryInfo
```

## Where things go

```
src/BatteryInfo.Core/Vendors/<Vendor>/   <Vendor>Protocol (pure), <Vendor>CommandPolicy, <Vendor>BatteryProvider
src/BatteryInfo.Core/ProviderFactory.cs  the providers the app and CLI run
tests/BatteryInfo.Core.Tests/Fakes/      FakeHidBackend, ManualTimeProvider: tests never touch real devices
docs/protocols/<vendor>.md               the protocol a provider implements
```

- Providers never throw for device problems: they return a reading with a `DeviceStatus` and `Detail`.

## Workflow: adding or changing a vendor provider

1. Read `docs/protocols/<vendor>.md`; if the protocol itself changes, update that file first.
2. Branch from an up-to-date `main` (`feature/<vendor>-...` or `bugfix/...`).
3. TDD the protocol codec with byte vectors captured from the device, then the command policy
   (allowed frames pass, known dangerous neighbours are refused), then the provider on `FakeHidBackend`.
4. Register the provider in `ProviderFactory.CreateAll()`.
5. Live check on the owner's PC: `batteryinfo list --vendor <name> --trace`.
6. Update the README and known-issues.md, then push, open the PR and merge it after CI.
