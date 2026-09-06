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

Next: **M6 — polish** (quick-connect, search, tab tear-off, settings, theme, import). See `.claude/plans/`.
