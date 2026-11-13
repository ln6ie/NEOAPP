# NEO llama.cpp Realtime protocol audit

Audit date: 2026-11-13. Official tag **b12360** resolves to commit **2d8a32502909ca065c41c4cf5f2fb1c427c047fc**. This is source-level evidence, not verification of any installed binary.

Sources pinned to that commit:

- [server-realtime.cpp](https://github.com/ggml-org/llama.cpp/blob/2d8a32502909ca065c41c4cf5f2fb1c427c047fc/tools/server/server-realtime.cpp)
- [server-context.cpp](https://github.com/ggml-org/llama.cpp/blob/2d8a32502909ca065c41c4cf5f2fb1c427c047fc/tools/server/server-context.cpp)
- [server documentation](https://github.com/ggml-org/llama.cpp/blob/2d8a32502909ca065c41c4cf5f2fb1c427c047fc/tools/server/README.md)

The implementation distinguishes conversation and live transcription paths; do not substitute the transcription path for native speech conversation. Conversation source emits `session.created` and `session.updated`. Session audio configuration is nested under `audio.input.format` and `audio.output.format`; rates are advertised from model capabilities. The input rate may be configured within 8,000–192,000 Hz; model input rate and generation output rate need not match.

Audio input uses `input_audio_buffer.append` with base64 PCM16 mono. `input_audio_buffer.commit` commits the user turn; `response.create` starts the conversation response after committed audio or a message. An empty commit is rejected. `input_audio_buffer.clear` clears buffered input. `response.cancel` is recognized. Actual cancellation timing and playback flushing must be verified in the client before promising interruption.

Conversation output audio uses **`response.output_audio.delta`**, with base64 PCM16 in `delta`, not an assumed `response.audio.delta`. Output completion includes response status (completed/cancelled). Session-selected output modalities must match model capabilities. Server errors are structured events. Do not assume automatic VAD, full duplex, barge-in, or arbitrary OpenAI SDK compatibility.

NEO now connects with a deadline, parses `session.created` rates, sends `session.update`, streams bounded PCM16 microphone frames, commits with `input_audio_buffer.commit`, requests a response, decodes `response.output_audio.delta`, and feeds a bounded `BufferedWaveProvider`/WASAPI speaker path. `response.cancel` flushes local playback and the next listening turn can reconnect after a dropped socket. The standalone `NEO.VoiceProbe` reports capture, sent, and generated-audio byte counts.

The live probe against the local b12360 server produced real generated PCM and played it through the Windows shared output path. On the verification machine the default capture endpoint produced PCM buffers but its measured peak input level was 0.000 during the unattended run; a spoken turn still needs to be exercised with an active microphone before this feature is declared complete. No synthetic tone or fake transcript is used.
