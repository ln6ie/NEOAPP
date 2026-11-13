# NEO verification record

## Speech quality and edge scrollbar correction

The reported session repeatedly returned short greetings. No input transcripts were retained, so intended speech cannot be inferred from logs. Turn-tail gain was observed near 32x. Adaptation now ignores sub-0.0003 RMS noise/tails, resets per utterance and caps at 8x. This fixes a processing defect but does not prove Arabic ASR accuracy. Instructions request direct answers, ASCII numbers, clarification and honest lack of execution tools. The pinned realtime protocol has no temperature field; no unsupported sampling option was added.

A live HTTP probe initially answered Arabic arithmetic with Chinese number characters. After instruction adjustment, arithmetic returned `7 + 5 = 12` and Iraq's capital answer was Baghdad. An action request still received an unhelpful uncertainty answer; model quality remains limited. A text-input-to-realtime-speech probe returned the correct Arabic Baghdad sentence: 103680 PCM bytes received and consumed, 24 kHz, 2.160 seconds PCM, 2.238 seconds playback. This does NOT verify microphone understanding. No model replacement or fine-tuning occurred. References: [official model card](https://huggingface.co/LiquidAI/LFM3-Omni-2B) and [pinned realtime source](https://github.com/ggml-org/llama.cpp/blob/2d8a32502/tools/server/server-realtime.cpp).

The system-themed WPF scrollbar now occupies its own column at the window's right edge. RTL text has a separate viewport. WPF tests verify no viewport/rail overlap, edge alignment and actual scrolling with overflow. Release build: zero warnings/errors and 38 tests passed. Spoken microphone comprehension and desktop visual acceptance remain separate gates.

Date: 2026-11-13. This is an initial native-shell milestone, not completion of the full product specification.

## Environment audit

- The selected workspace was empty. NEO was created there; the different workspace path in the supplied specification was not used as the project destination.
- Windows reports build 26300, x64. SDKs 8.0.425 and 9.0.318 were present; .NET SDK 10.0.404 was installed from Microsoft's winget package for the requested stack.
- The specified GGUF and projector files exist. Neither was opened for inference, modified, copied or downloaded by NEO.
- A later live check on 2026-11-13 found the existing server available at loopback port 8081. `/health` returned `{"status":"ok"}` and `/props` reported `b12360-2d8a32502`, F16, audio and vision modalities, and `female,male` voices.
- Official b12360 source was inspected at its immutable tag commit; no installed binary could be matched to it. Protocol findings are in the protocol audit.
- No Git repository or GitHub remote existed. A GitHub repository has not been created or published. Its intended name is NEO.

## Implemented and tested

- `dotnet build NEO.sln -c Release`: succeeded, zero warnings and errors.
- `dotnet test NEO.sln -c Release`: 15 tests passed. Coverage includes state transition ordering, disconnect/reconnect requirements, notification duplication, JSON configuration round-trip, malformed JSON, unsafe endpoints, non-finite geometry, HTTP ready/loading/error/invalid responses, caller cancellation, and chat response/error parsing. HTTP tests use a deterministic in-memory handler; they are not live server tests.
- Local Windows publish is provided by `scripts/build-windows.ps1`; it restores, builds, tests, then publishes a self-contained `win-x64` package to `artifacts/NEO-win-x64/`. GitHub Actions is optional CI and is not required to produce the Windows build.
- The self-contained package was launched from `artifacts/NEO-win-x64/NEO.exe`; the process reported `Running: true`, `Responding: true`, and the executable path matched the published package. The package size was 171.26 MiB including the .NET Windows runtime.
- NEO.exe launched and remained running, with process responsiveness reported true and no NEO error log observed. This is process-level evidence, not visual acceptance.
- One idle sample over 5.062 seconds reported 0.000% of one CPU core, 114.25 MiB working set, 54.44 MiB private memory. This brief sample is not a sustained performance or leak benchmark and does not establish active-audio resource usage.
- Source search found no remaining provisional project names or namespaces.
- A live HTTP chat request to the existing server returned exactly `NEO-OK` in 2,196 ms with `finish_reason=stop`. This validates the server/model path, not an in-app chat flow.
- The self-contained published NEO process was rechecked after the server became available: path matched `artifacts/NEO-win-x64/NEO.exe`, `Responding: true`, and it remained running.
- The chat transport and panel were implemented after the first shell build. `dotnet test NEO.sln -c Release` now passes 15 tests, including real chat response parsing and HTTP error handling. The self-contained Windows package was republished after this change.
- `NEO.VoiceProbe` was run against `ws://127.0.0.1:8081/v1/realtime` with the live b12360 server. The session reached `Connecting → Ready → Listening → Processing → Speaking → Ready`; the 5-second run captured 158,018 PCM bytes, sent 158,018 bytes, received 122,880 generated PCM bytes, and reported peak output level 0.351. WASAPI playback was initialized and fed immediately from output deltas. Peak input level was 0.000 because the unattended capture endpoint was silent, so a spoken microphone turn is still required for the voice completion gate.

## Implemented but not visually verified

Floating capsule, bottom connection/status surface, settings, tray menu and quit action, Ctrl+Alt+Space, Windows theme changes, high contrast, reduced-motion hover transition, non-activating capsule behavior, selected-monitor positioning, topmost policy, display-change handling, fullscreen rectangle heuristic, and lifecycle disposal paths.

The desktop automation helper returned `Computer Use native pipe is unavailable` (OS error 2); the alternate computer-use entry point reported the same missing native host. Consequently no screenshot, click, keyboard, multi-monitor, mixed-DPI, tray exit, or focus verification is claimed. The app was left running for manual inspection.

## Remaining release gates

### Floating orb chrome and generated icon correction

The transparent orb HWND now disables the automatic Fluent window backdrop and DWM non-client border/corner/backdrop rendering. The collapsed window is transparent except for the orb; only an explicitly opened conversation dropdown has a card surface. Fluent controls and system theme colors remain enabled.

The active N/e application icon was generated using the built-in image generation tool with a transparent exterior and blue tile. The generated full-resolution raster is `src/App/Resources/NEO-generated.png`; `scripts/create-icon.ps1` now resizes and packages this raster into PNG and ICO sizes rather than drawing vector geometry. The old SVG was removed.

### Arabic orb interface and generated speech

The current UI replaces the compact bar with a blue/white vector voice orb, uses Arabic RTL text/settings/tray, a green/red header connection point, and a rounded composer with Send only. N/e SVG, 256px PNG and 16/24/32/48/64/128/256px ICO assets were generated locally. Reduced motion and high contrast are handled by the orb control; no idle animation loop runs.

The live `--speaker-check --arabic` probe used the same Arabic instruction and output-language code as the app. The existing server returned Arabic response text and 72 audio chunks: 272,640 bytes received and consumed, 5.680 seconds of 24 kHz PCM, and 5.745 seconds of measured playback. This verifies Arabic generation and speaker delivery; microphone-to-Arabic understanding and subjective pronunciation still require a spoken user turn. 22 deterministic tests pass, including Arabic instruction placement without changing user messages. The updated Arabic orb/settings app launched, accepted session.updated and reached Listening without errors. Full desktop visual acceptance remains unverified.

### Unified Fluent notch interface

The old bottom conversation window and hand-written button template/palette were removed. The application now uses Microsoft's built-in WPF Fluent theme in System mode, system Segoe Fluent Icons, WindowChrome and native DWM rounded corners. The top notch expands in place for text input, conversation and live voice text; settings use native controls in Voice, Desktop and Connection tabs. Run NEO in VS Code now uses the Debug development launcher with live diagnostics.

Debug build succeeded with zero warnings/errors and 21 existing tests passed. Runtime logs confirmed Fluent resources resolving to the current dark system theme, compact geometry 360x56, expanded geometry 560x560 at top=10, and opening the settings window without an application error. The desktop inspection helper failed with native pipe unavailable (OS error 2); no screenshot, keyboard-focus or full visual acceptance is claimed.

### Automatic Live and quiet-microphone detection

The supplied real microphone log peaked at 0.004595, below the old fixed 0.025 threshold; no turn commit was possible. The detector now calibrates a background-noise floor, uses a minimum 0.0008 threshold, requires 120 ms of speech and 650 ms of subsequent silence, and preserves 350 ms of pre-roll. Idle PCM is not sent to the model. Tests cover the reported quiet microphone signal, steady background noise, silence, and automatic turn completion.

Live starts automatically by default, with an opt-out and microphone selection in settings. A native startup check reached Listening without clicking any voice control. The selected Windows communications device was Headset Microphone (HyperX Virtual Surround Sound), unmuted, Windows level 1.0. Silent capture stayed below threshold and sent zero bytes to the model during the check. 21 tests passed. A live spoken turn and simultaneous speech interruption remain unverified; simultaneous interruption is not implemented by this change.

### Speaker buffering correction

The original 600 ms output buffer silently discarded overflow when generation outran playback. It is replaced with a bounded two-second queue that asynchronously waits for space, preserving all PCM samples and allowing cancellation. Playback keeps the server-advertised 24,000 Hz rate. Listening resumes after the PCM queue and final WASAPI device latency drain.

Live `NEO.VoiceProbe --speaker-check` against the existing server generated 169 audio chunks: 645,120 bytes received and 645,120 bytes consumed by WASAPI. PCM duration was 13.440 seconds; measured playback wall time was 13.578 seconds. This verifies generated output delivery and pacing, not microphone recognition or subjective voice quality. Regression tests verify a seven-second burst survives a two-second queue byte-for-byte and a blocked producer can be cancelled.

Spoken microphone-turn verification on the target device; HTTP streamed conversations; attachments and extraction; startup registration; advanced expansion/panel animations; exclusive-fullscreen distinction; active-voice and repeated-cycle memory benchmarks. Realtime cancellation/reconnect paths are implemented but should be exercised during a spoken turn.

## Next verification gates

1. Exercise a spoken microphone turn on the target Windows device and record a non-zero `PEAK_INPUT_LEVEL` with generated output.
2. Visually validate the native shell on the actual desktop and close through its tray menu.
3. Exercise cancellation, reconnect, repeated voice cycles, accessibility, performance and remaining attachment gates. Public release also requires the owner's project license selection. CI is authored but has not run on GitHub.

## Current centered conversation update

Release tests: 30 passed. New checks cover native Markdown structure and safe links, attachment serialization and extraction, PCM gain/sample preservation, and actual WPF composer measurement (slim single-line input, multiline growth and centered Send). This is layout verification, not full desktop visual acceptance.

The conversation is now a separate centered window, with plain assistant Markdown, attachment chips, file/image paste and an expanding composer. Earlier entries above describe historical interfaces and test counts. The application logo remains generated raster artwork, not SVG.

The current live multimodal probe failed with connection refused at 127.0.0.1:8081. Current microphone-to-Arabic-to-speaker operation is therefore NOT verified. Earlier speaker results are historical and do not prove these input changes. After starting the existing server, run:

```powershell
dotnet run --project tools/VoiceProbe -c Release -- --arabic-microphone-check
dotnet run --project tools/VoiceProbe -c Release -- --speaker-check --arabic
dotnet run --project tools/VoiceProbe -c Release -- --attachment-check src/App/Resources/NEO.png
```

The microphone check captures eight seconds only when explicitly invoked, prints measured levels and the returned transcript, and does not save the recording. Automatic turn-taking is implemented; simultaneous interruption remains unverified and is not claimed.

### Floating toolbar and attachment presentation

The generated raster icon was replaced using built-in image generation: preserve joined N/e, royal-blue monogram, white and orange dispersed pigment background, transparent exterior. The header now overlays the scrollable conversation; its three 30px actions share a rounded capsule. Explicit LTR chrome places the logo physically right and scrollbar right, with RTL message/composer content preserved. Geometry regression checks cover logo side, scroll direction and overlapping toolbar layout. Pending and sent attachments share cached 80px thumbnails, ellipsized base names, extension labels and file glyphs. No complete desktop visual acceptance is claimed by these layout tests.


### Movable conversation and brand orb

The orb now blends royal blue, white and orange using reusable gradient brushes and retains existing audio reaction/reduced-motion behavior. Conversation is not topmost, appears in the taskbar, supports drag from its upper free space/logo, native minimization and resizing via bottom-right grip. Reopening restores a minimized window and retains its position/size within the running session; display configuration changes can recenter it for reachability. Closing the conversation hides it; quitting the tray closes resources. Release build: zero warnings/errors, 30 tests passed. Interactive dragging/minimize/resize acceptance is not established by automated tests.


### Embedded settings

SettingsView is a UserControl hosted in ConversationWindow. Settings replace the message/composer area in the same HWND, with Back and Escape navigation; save reuses existing validation and persistence. Tray and keyboard settings entry points use the same embedded page. Header children are ordered physically left-to-right: Close, Minimize, Microphone, Settings; regression assertions cover the first two. 30 tests passed and Windows UI package published. The running older executable was left open to preserve the current conversation.


## Automatic server lifecycle verification

[Current server management evidence](server-management.md) supersedes the earlier offline-server status: installation, fresh-shell PATH, actual CUDA load, owned automatic startup/shutdown, external reuse across two NEO launches, real chat and realtime handshake passed. Automated suite: 37 passing. Voice conversation remains a separate gate.
