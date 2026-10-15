# Working from the Claude desktop app

What an agent's commands can and cannot change when they run from the Claude desktop app (Code tab).
Back to [AGENTS.md](../../AGENTS.md).

## Everything you start is sandboxed

The Claude desktop app is a packaged (MSIX) app. Every process an agent starts from it (PowerShell,
bash, dotnet, the tray app, Setup.exe) runs in that app's job, and its writes are redirected:

| Written to | Really lands in | Seen by Windows (Explorer, sign-in, Settings > Apps) |
|---|---|---|
| a new folder in `%LOCALAPPDATA%` or `%APPDATA%` (`BatteryInfoApp`, `BatteryInfo`) | `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\...` | no |
| a folder in them that already existed for real (`Programs`, `Microsoft\Windows\Start Menu`) | where it says | yes |
| HKCU registry, even an existing key such as `Run` | the package's private hive | no |
| Desktop, `%TEMP%`, `D:\` | where it says | yes |

Inside the sandbox a private folder hides a real one of the same name, so once a real install exists,
a stale sandboxed copy still shadows it for anything started from the agent shell.

Inside the sandbox everything looks normal. That is how the owner's first install ended up only inside
it, updates included: its files and its Run and uninstall entries were sandboxed, but its shortcuts were
real, so the Start menu and desktop showed a blank icon for an exe Windows could not find. A sandboxed copy now
shows an "installed in a sandbox" toast when it starts.

## Rules

- Never run Setup.exe, `publish.ps1` (with or without `-Start`) or **Start with Windows** for the owner's
  real use from the agent shell. The owner installs and starts the app from Explorer.
- `--demo` and debug runs from the shell are fine: they only need to work inside the sandbox.
- To see the real state, ask WMI, which runs outside the sandbox:

  ```powershell
  Get-CimInstance CIM_DataFile -Filter "Name='C:\\Users\\liubo\\AppData\\Local\\BatteryInfoApp\\current\\BatteryInfo.exe'"
  Invoke-CimMethod -ClassName StdRegProv -MethodName GetStringValue -Arguments @{
      hDefKey = [uint32]2147483649; sSubKeyName = 'Software\Microsoft\Windows\CurrentVersion\Run'; sValueName = 'BatteryInfo' }
  ```

  In the sandbox, `(Get-Item <path>).Target` shows the LocalCache path of a redirected folder.

## Live UI checks

The flyout opens on the primary display, a virtual display that screenshots cannot capture. Drive it
with UI Automation instead: the device rows are toggle buttons named after the device, so a script can
read their bounds and order, move the real mouse with `SetCursorPos` and `mouse_event` (call
`SetProcessDpiAwarenessContext(-4)` first so the coordinates match), and read the result back from UI
Automation and `%LOCALAPPDATA%\BatteryInfo\demo\settings.json`.
