# NEO architecture

- **NEO**: WPF .NET 10 application, composition root, one expandable top notch, native settings window, tray and Windows integration.
- **NEO.Core**: settings, health/chat/voice contracts, assistant states and local voice turn detection.
- **NEO.Infrastructure**: settings persistence, HTTP chat, llama.cpp realtime transport, WASAPI capture/playback and asynchronous diagnostics.
- **NEO.Tests**: deterministic configuration, state, HTTP, speech detection and lossless PCM queue tests.
- **NEO.VoiceProbe**: explicit live microphone and server-to-speaker verification.

## Windows interface

The top orb is a transparent 92x92 window. Clicking it toggles a separate 660x560 conversation window centered in the selected monitor work area. Geometry is capped to the available work area. The conversation has a thin green/red status strip, round raster logo, native icon actions and a slim expanding composer. Incoming voice does not force it open. Assistant text is rendered directly with native FlowDocument blocks parsed by Markdig; no browser, remote images or executable HTML. Streaming display updates are throttled to 120 ms. User messages retain a subtle background.

Attachments are selected, dropped or pasted. `Infrastructure/Files/AttachmentReader` validates supported image signatures and extracts UTF-8 text, DOCX and PDF text off the UI thread with bounded sizes. `Infrastructure/Chat/ChatClient` serializes image_url data URIs and text parts to the existing chat endpoint. The server must support image input; there is no fabricated OCR fallback. The generated raster N/e logo remains the application icon.

Arabic instructions are shared by HTTP and realtime. Input remains PCM16 at 16 kHz; playback uses the server-advertised rate. The local detector now waits one second for end-of-turn silence. Gain normalization is applied after raw VAD measurement and preserves sample count/rate. This helps quiet signals but does not establish Arabic recognition accuracy. Automatic reconnection runs every ten seconds and respects an explicitly stopped microphone. Fluent system resources supply colors and settings controls; NEO owns the orb, layout and compact composer template.
Settings use a standard system-framed window with Voice, Desktop and Connection tabs, native controls, microphone selection, Live startup, monitor/topmost/fullscreen options, and local endpoint settings. The window is scrollable and resizable.

## Audio and lifecycle

HTTP chat remains a cancellable non-streaming request to the existing llama.cpp server. Voice uses pinned b12360 PCM16 events, locally detected speech turns, a short input pre-roll, and WASAPI playback with a bounded lossless queue. Live starts automatically unless disabled. Automatic turn-taking is implemented; full simultaneous barge-in is not yet implemented or claimed.

One shared HttpClient, one voice session, reusable notch and settings windows, one tray icon and one low-priority fullscreen timer are owned by the application. View-model voice subscriptions are detached on replacement. Logs use a bounded asynchronous writer and exclude audio payloads and conversation contents. Display positioning uses the target monitor's DPI. Mixed-DPI interaction and visual acceptance still require desktop verification.



## Managed local CUDA server

Infrastructure now owns the injected `Infrastructure/Server/ServerManager` with health/model validation, per-port startup locking, async readiness, cancellation and explicit process ownership. Realtime connection and audio code live under `Infrastructure/Voice`. The small NEO.ServerHost companion isolates the owned process console for graceful shutdown; reused external servers are never signalled. Settings expose automatic-management opt-out. See [installation, ownership and measured verification](server-management.md). Earlier external-only descriptions are superseded by this section.
