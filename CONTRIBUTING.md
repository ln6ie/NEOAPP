# Contributing to NEO

Understand existing code before making small, focused changes. Keep WPF and Windows handles out of NEO.Core. Keep transport code out of views. Use cancellation and bounded buffers for background work, dispose native resources, and do not introduce dependencies without a functional need.

Build and run tests using the commands in the README. Include actual verification evidence; distinguish simulated transport tests from live inference. Never label audio as working based on health or a WebSocket handshake. Preserve accessibility, Arabic text rendering, focus behavior and Windows theme integration.

Do not commit local user data, models, recordings, secrets or build output. Discuss licensing with the owner before accepting contributions for public release; the project license is currently undecided.
