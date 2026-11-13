# Local llama.cpp lifecycle

NEO manages the existing b12360 CUDA installation without a Windows service or elevated runtime permissions. No model or runtime downloads occur. The current installation is `%LOCALAPPDATA%\Programs\llama.cpp`; administrator installs may use `C:\Program Files\llama.cpp`. The original unpacked distribution remains untouched as the installation source, not a second managed runtime.

`scripts/install-llama.ps1` installs the existing distribution and the three CUDA 12 runtime DLLs already present in LM Studio's vendor directory. It preserves user PATH entries, appends the installation once, verifies b12360 and CUDA0 in a new PowerShell process, and records SHA256 binary inventory in `NEO-install-manifest.json` inside the installation directory. It neither moves nor modifies GGUF files. Existing interactive shells, including VS Code, may retain their old environment until restarted; NEO uses the absolute installed path and does not depend on that refresh.

## Configuration

Settings remain in `%LOCALAPPDATA%\NEO\NEO.settings.json`. Missing new properties receive these defaults:

- `AutoManageServer`: true; disable in the embedded Connection settings page to retain manual server management.
- `ServerExecutablePath`: installed per-user executable, otherwise existing Program Files executable.
- `ServerModelPath`: `%USERPROFILE%\.lmstudio\models\LiquidAI\LFM3-Omni-2B-GGUF\LFM3-Omni-2B-F16.gguf`.
- `ServerProjectorPath`: same directory, `mmproj-LFM3-Omni-2B-F16.gguf`.
- HTTP / Realtime: `http://127.0.0.1:8081/` and `ws://127.0.0.1:8081/v1/realtime`.

Path overrides in JSON must be absolute expanded filesystem paths. Auto management requires matching literal IPv4 loopback endpoints. Arguments use `ProcessStartInfo.ArgumentList`: `-m <model> --mmproj <projector> -ngl 99 -c 4096 --host 127.0.0.1 --port 8081 --device CUDA0 --offline`. Explicit CUDA device selection prevents silent CPU fallback; offline mode prevents implicit model retrieval.

## Ownership and architecture

`IServerManager` / `ServerManager` live in `src/Infrastructure/Server`. The composition root injects the shared HTTP client, helper executable and lock directory. An async semaphore and per-port exclusive file lock serialize readiness/start operations, including across NEO manager instances. An occupied or unrecognized endpoint is not terminated. Readiness requires `/health`, exact model path, b12360 build identity, and audio modality from `/props`. The HTTP API does not expose the projector's filesystem path: external compatibility verifies its audio capability, while owned startup supplies the exact configured projector path.

Loading, Ready, Error and Disconnected are observable states. The native UI presents loading/error/disconnected text and the colored connection surface; the embedded settings page and accessibility label expose status. Startup awaits readiness asynchronously (three-minute deadline), then enables chat and realtime. Recovery retries every ten seconds, with only one readiness operation active.

`NEO.ServerHost` is a small native Windows/.NET companion packaged beside NEO. It gives an owned server a private hidden console, forwards filtered diagnostics and listens to the parent's redirected control pipe. Normal exit or pipe EOF sends Ctrl+C to that isolated console, waits up to 15 seconds, then terminates only its own child if unresponsive. It never attaches to a user's console or signals an external server. A reused server has no owned process handle and survives NEO exit. Use the tray Quit action for normal cleanup; `NEO.exe --quit` is an automation-friendly graceful exit signal for the same Windows session.

Server diagnostics share the bounded rotating NEO JSONL log under event `server.output`; raw prompts, audio and HTTP bodies are not intentionally captured. The normal application does not retain complete verbose server output. Live verification tools are explicit diagnostics, not background services.

## Verification on 2026-11-13

- Fresh PowerShell resolved installed `llama-server`, build 12360 / commit `2d8a32502`, and enumerated CUDA0 RTX 5060 Ti (16 GiB).
- Loaded modules included installed ggml/llama/mtmd, cudart, cuBLAS and cuBLASLt, plus the system NVIDIA driver.
- NEO started from no server process, launched the installed executable, and reached Ready in 2.86 seconds after Loading. GPU usage rose to approximately 6393 MiB, then returned near 1322 MiB on exit (whole-GPU snapshots, not exclusive per-process allocation).
- A verbose external fixture using the same installed executable/model/arguments reported `offloaded 25/25 layers to GPU`, 3298.17 MiB CUDA model buffer, and CUDA0 projector backend.
- `/health` returned `ok`; real chat returned `2 + 2 equals 4.` in 215 ms. Realtime emitted `session.created` in 11 ms. These are single-run observations, not performance benchmarks or proof of voice conversation.
- Owned normal shutdown sent Ctrl+C and exited code 0 in approximately 0.85 seconds. Server process and listener disappeared.
- An external fixture stayed at PID 27276 across two NEO launches and NEO exits; logs reported `server.reused` with `owned:false`, with no extra llama-server. The diagnostic fixture was subsequently stopped by its own control channel and exited code 0.
- 37 automated tests passed, covering reuse without ownership, incompatible identities, cancellation during external loading, opt-out, argument safety, existing chat/audio/Markdown and layout behavior.
- The final self-contained Windows x64 package also auto-started the server from stopped state (2.87 seconds), returned real chat in 283 ms and realtime session creation in 10 ms. Quitting that published NEO sent Ctrl+C, exited code 0 in about 1.15 seconds, and left zero server/helper processes.

Live diagnostic: `dotnet run --project tools/VoiceProbe -c Release -- --server-check`. Full spoken Arabic conversation remains a separate acceptance gate. Timeout forced termination and GPU/driver failure recovery have deterministic guard paths but were not deliberately induced on this machine. Initial load time depends on disk cache and hardware. No installation of another model, permanent service or network-facing listener was performed.

Protocol identity fields and signal handling were checked against [the pinned server context](https://github.com/ggml-org/llama.cpp/blob/2d8a32502/tools/server/server-context.cpp) and [pinned server shutdown implementation](https://github.com/ggml-org/llama.cpp/blob/2d8a32502/tools/server/server.cpp).
