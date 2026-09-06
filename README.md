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

Milestone **M0 — solution skeleton & docking shell** complete: Generic Host + DI + Serilog,
AvalonDock layout (Sessions / Transfers / Log panels + document area), sample session tree,
placeholder tabs, layout persistence. See `.claude/plans/` for the full milestone plan.
