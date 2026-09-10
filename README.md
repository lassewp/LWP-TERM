# LWP-TERM

A MobaXterm-inspired connection manager and terminal multiplexer for Windows, built with C# / WPF.

Folder-organized tree of unlimited saved sessions on the left, a tabbed session area in the center.
Protocol work is delegated to established libraries — this project is the glue, the UI, and the
session management.

## Planned protocol support

| Feature | Library / mechanism |
|---|---|
| SSH | SSH.NET `ShellStream` |
| Telnet | PrimS.Telnet (+ small IAC/NAWS shim) |
| Serial | `System.IO.Ports.SerialPort` |
| PowerShell / CMD / WSL | ConPTY pseudo-console (Pty.Net) |
| SFTP browser | SSH.NET `SftpClient` |
| FTP / FTPS | FluentFTP |
| RDP | MSTSC ActiveX hosted via `WindowsFormsHost` |
| VNC | MarcusW.VncClient rendered to a `WriteableBitmap` |
| Terminal rendering | xterm.js in a WebView2 control |
| Docking / tabs | AvalonDock |
| Credentials | Windows DPAPI at rest + optional master password |

## Solution layout

```
src/LwpTerm.Core          models, session store, crypto, settings  (net8.0)
src/LwpTerm.Connections    protocol adapters                        (net8.0-windows)
src/LwpTerm.App            WPF shell, view models, views            (net8.0-windows, x64)
tests/LwpTerm.Core.Tests   xUnit
```

## Build & run

Requires the .NET 8 SDK and the WebView2 runtime (already present on current Windows 10/11).

```bash
dotnet build
dotnet test
dotnet run --project src/LwpTerm.App
```

Config, sessions and logs live under `%APPDATA%\LwpTerm\`. Drop a `portable.txt` next to the
executable to switch to portable mode (everything under `.\data\`).

## Install & update (for users)

1. Open the repo's **[Releases](../../releases)** page and download `LwpTerm-win-Setup.exe`
   from the latest release.
2. Run it. Windows SmartScreen shows *"unknown publisher"* the first time — click
   **More info → Run anyway** (the build is not code-signed).
3. It installs per-user to `%LocalAppData%\LwpTerm` (no admin), adds a Start-menu / Desktop
   shortcut, and installs the Microsoft Edge WebView2 runtime if it is missing.

After that the app **updates itself**: it checks for a newer release on launch, downloads it in
the background, and offers to restart. You can also trigger it from the gear menu ▸
**Check for updates…**. Your sessions, vault and settings under `%APPDATA%\LwpTerm` are kept
across updates.

## Releasing (for the maintainer)

A push of a `v*` tag builds, packs and publishes a GitHub Release via
`.github/workflows/release.yml` (self-contained `win-x64`, packaged with
[Velopack](https://velopack.io)):

```bash
git tag v0.2.0
git push origin v0.2.0
```

Set the repo URL once in `src/LwpTerm.App/Services/UpdateService.cs` (`RepoUrl` const). The
repo must be **public** for users to download releases without a token. To build a release
locally:

```bash
dotnet tool install -g vpk
dotnet publish src/LwpTerm.App/LwpTerm.App.csproj -c Release -r win-x64 --self-contained true -p:Version=0.2.0 -o publish
vpk pack --packId LwpTerm --packVersion 0.2.0 --packDir publish --mainExe LwpTerm.exe --packTitle "LWP-TERM" --icon src/LwpTerm.App/app.ico --framework webview2
```

## Status

- **M0 — solution skeleton & docking shell** ✔ Generic Host + DI + Serilog, AvalonDock layout
  (Sessions / Transfers / Log panels + document area), placeholder tabs, layout persistence.
- **M1 — session model, storage & editor** ✔ Polymorphic session tree (`sessions.json`, atomic
  writes), DPAPI credential store with optional master password (`vault.json`), tree CRUD +
  drag-drop reorder, full protocol-aware session editor, master-password unlock prompt at startup.
- **M2 — terminal core & local shells** ✔ xterm.js hosted in WebView2 with a JSON bridge
  (input / output / resize), a hand-rolled ConPTY layer (`ITerminalConnection`), and
  PowerShell / pwsh / CMD / WSL tabs with live resize/reflow and optional per-session logging.
- **M3 — SSH + Telnet + Serial** ✔ Three more `ITerminalConnection` transports: SSH.NET shell
  channel with a `known_hosts` trust store + accept/reject prompt (password / key-file auth),
  a raw Telnet client with a compact NVT/IAC negotiator (SGA/ECHO/TTYPE/NAWS), and
  `System.IO.Ports` serial with configurable line settings and optional local echo.
- **M4 — SFTP + FTP file browser** ✔ `IFileTransferConnection` with SSH.NET SFTP and FluentFTP
  (FTP/FTPS) implementations; a dual-pane browser (local ↔ remote) with navigate / up / new
  folder / rename / delete; a concurrency-limited transfer queue shown in the Transfers panel
  with live progress, speed and per-item cancel; drag-drop between panes; and "Open SFTP" on an
  SSH session (reuses its host + credentials).
- **M5 — RDP + VNC embedded tabs** ✔ RDP hosts the MSTSC ActiveX control
  (`AxMsRdpClient9NotSafeForScripting`, interop committed under `src/LwpTerm.App/Interop`) in a
  `WindowsFormsHost` — server / credentials / fit-to-window (SmartSizing) / clipboard + drive
  redirect, disconnect-reason surfacing, and an "open in mstsc.exe" fallback. VNC hosts VncSharp's
  `RemoteDesktop` (view-only + Ctrl+Alt+Del). Secrets are decrypted only at connect time.
- **M6 — polish** ✔ live recursive tree filter; quick-connect bar
  (`ssh://user@host:port`, `rdp://host`, bare `host`, …); Settings dialog + `settings.json`
  (theme, terminal font / size / scrollback, default shell, master-password enable/disable);
  light + dark palette (applied at startup); per-session accent colour in the tree; import of
  saved PuTTY sessions from the registry. Document floating (tear-off) is AvalonDock's built-in.
- **Full screen** — the ⛶ icon on a tab header (or <kbd>F11</kbd> / the toolbar button / the gear
  menu / double-clicking the tab) lifts the active session into its own borderless window that
  fills a monitor — the second one if you have it — while the main window stays open and usable.
  <kbd>Shift</kbd> picks borderless (covers the taskbar) over windowed (work area). An mstsc-style
  bar drops from the top edge with the session host and Exit / mode / pin / minimise; <kbd>Esc</kbd>
  or <kbd>F11</kbd> also exit, caught by a low-level keyboard hook even while the RDP / VNC / terminal
  surface holds keyboard focus. Closing the session's tab closes its full-screen window too.

## Manual verification

Build: `dotnet build` · Tests: `dotnet test` (68). Then, against real endpoints:

| Feature | Check |
|---|---|
| Unlimited saved sessions / Folders | Right-click Sessions ▸ New Folder / New Session; nest freely; restart — tree persists (`sessions.json`) |
| Tabs | Open several sessions; drag a document tab out to float it; Ctrl+W closes |
| Full screen | ⛶ / F11 pops the session into its own window on the 2nd monitor; main app still works; Esc / F11 return it to the tab (still live). Works with RDP / terminal focused |
| SSH | Session to a Linux host; run `tmux` / `vim`; first connect prompts to trust the host key |
| Telnet | Session to a Telnet service; keys echo, screen apps redraw |
| Serial | com0com pair or a device; set baud/parity; bytes flow, optional local echo |
| PowerShell / CMD / WSL | Ctrl+T or a LocalShell session; run `htop` in WSL; resize the window → reflow |
| SFTP browser | "Open SFTP" on an SSH session; browse, drag a large file across, watch progress + Cancel |
| FTP | FTP/FTPS session; upload + download; explicit TLS |
| RDP | RDP session to a Windows host inside a tab; resize (SmartSizing); "open in mstsc.exe" fallback |
| VNC | VNC session to a server (e.g. TightVNC) inside a tab; interact; Send Ctrl+Alt+Del |

## Known limitations

- SSH sessions don't send window-resize to the remote PTY (SSH.NET has no public API); the PTY
  keeps its initial size.
- Theme change and terminal-font change apply to new tabs / after restart, not live to open tabs.
- "Restore tabs on startup" is stored but not yet implemented; folder-level SFTP/FTP transfers
  (whole directories) are not yet implemented.
- The RDP interop and VncSharp are Windows-desktop / .NET Framework components used via
  `WindowsFormsHost`; the app targets `win-x64`.
