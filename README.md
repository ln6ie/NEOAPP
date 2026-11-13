# NEO

NEO is a lightweight, native Windows 11 floating AI assistant powered by an external llama.cpp server. C# · WPF · .NET 10. No browser runtime, model bundling, analytics, or background microphone recording.

## Current milestone

The Arabic RTL interface has a frameless floating voice orb at the top. Clicking it opens a separate centered conversation window with a round raster logo and a thin green/red connection strip. Assistant replies use native selectable Markdown without bubbles or sender labels. The slim composer grows with lines and supports Send, attachments, file drops and direct image/file clipboard paste. Enter sends; Shift+Enter inserts a line.

Images use the server's multimodal chat input. Text, DOCX and text-based PDF attachments are extracted locally; scanned PDFs require OCR and are rejected. Limits: six files, 12 MiB per file, 24 MiB total image bytes and 80,000 extracted characters. Markdown supports headings, lists, tables, emphasis, code and safe web links. WPF Fluent follows Windows theme colors; settings remain native Arabic controls.
Voice transport and UI are implemented, but the release gate remains open until a spoken microphone turn is observed on the target Windows device. Generated audio was received and sent to the speaker path during live verification. Do not infer speech input from server health or a successful WebSocket handshake. See [verification](docs/verification.md) and [protocol audit](docs/realtime-protocol.md).

## Build and run

Windows 11 x64 and .NET 10 SDK are required.

```powershell
dotnet restore NEO.sln
dotnet build NEO.sln -c Release
dotnet test NEO.sln -c Release
dotnet run --project src/App/NEO.csproj -c Release
```

The application executable is `src/App/bin/Release/net10.0-windows/NEO.exe` during development. To create a self-contained Windows x64 package locally, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-windows.ps1
```

The resulting package is in `artifacts/NEO-win-x64/` and includes `NEO.exe` plus the .NET Windows runtime. This build is performed on the local Windows machine and does not require GitHub Actions.

```powershell
dotnet publish src/App/NEO.csproj -c Release -r win-x64 --self-contained true -o artifacts/NEO-win-x64
```

Click the orb or press **Ctrl+Alt+Space** to open the centered conversation and focus input. Escape hides it. The tray provides show/hide, retry, embedded settings, and **Quit NEO** (which gracefully stops only an owned server). Startup registration is not implemented; NEO does not register itself automatically. The VS Code **Run NEO** task launches the Debug development script and streams logs to its terminal.

## Live development diagnostics

Live voice now starts automatically when NEO opens (disable `Start Live voice automatically` in settings to opt out). Choose the intended microphone in settings; the default is the Windows communications input. Speak after Live is listening, then pause to end the turn. The local detector calibrates to background noise and supports quiet microphones; it keeps a short pre-roll so the start of speech is preserved and does not send idle silence into the model. Voice capture is active while Live is enabled. The microphone icon toggles Live. Simultaneous interruption during generated speech is not implemented yet; this is automatic turn-taking rather than a claim of full ChatGPT Live parity.

Close any running NEO instance through its tray menu, then run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\dev.ps1
```

This builds Debug, launches the native application, prints live JSON log lines in the terminal, and saves a separate timestamped file under `artifacts/logs/`. Quit through the tray for normal cleanup. Ctrl+C stops the development instance launched by the script. No production publish is required for each development run.

Events include app lifecycle, UI actions, window coordinates/DPI/visibility, health results, text request counts, WebSocket events, voice state transitions, input levels, turn commits, first-audio latency, playback queue size, consumed bytes, cancellation and errors. Audio metrics are sampled once per second. Raw microphone/audio data, base64 payloads and conversation text are not logged. Each line includes time, run ID, thread and a diagnostic-queue drop counter. Logging writes asynchronously and rotates at 5 MiB with one previous file. Outside the development script the default destination is `%LOCALAPPDATA%/NEO/NEO.log`.

## Configuration storage

NEO automatically starts and reuses the installed local b12360 CUDA server by default. It waits for the configured model, verifies identity and leaves externally owned servers running. Disable this in the embedded Connection settings page. See [server installation and lifecycle verification](docs/server-management.md).

Settings are stored in `%LOCALAPPDATA%/NEO/NEO.settings.json`. Save through NEO settings, or copy [NEO.settings.example.json](NEO.settings.example.json) there while NEO is closed. Width, height and top offset are device-independent pixels. Monitor indices follow the current Windows enumeration; unplugged displays fall back to an available display. Only loopback endpoints are currently accepted. NEO starts the installed inference server only when automatic management is enabled; models stay at their existing paths.

Diagnostics are stored locally in `%LOCALAPPDATA%/NEO/NEO.log`, capped with one rotated file. They contain exception diagnostics, not conversation or audio content. Review local paths before sharing logs.

## Troubleshooting

- **Server offline:** verify the installation and model paths in [server management](docs/server-management.md); NEO retries automatically every ten seconds. No server or model is downloaded by NEO.
- **Voice input is silent:** select an enabled Windows capture device, allow microphone access for NEO, then press Voice and speak while `Listening` is shown. The probe reports `PEAK_INPUT_LEVEL` so silent capture is distinguishable from a working microphone.
- **Shortcut unavailable:** another application owns Ctrl+Alt+Space; open NEO from its tray icon.
- **Hidden capsule:** leave fullscreen mode, or use tray settings to disable fullscreen hiding.
- **Invalid configuration:** NEO reports the error and uses defaults without overwriting the invalid file.

## Repository and licensing

The intended GitHub repository name is **NEO**. No remote repository has been created or published by this implementation. The source is prepared for an open-source release, but the project license still requires the owner's selection; no project license grant is implied.

See [architecture](docs/architecture.md), [development](docs/development.md), [contribution guidelines](CONTRIBUTING.md), and [third-party notices](THIRD-PARTY-NOTICES.md).
