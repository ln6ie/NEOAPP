# Contributing to NEO

Understand existing code before making small, focused changes. Keep WPF and Windows handles out of NEO.Core. Keep transport code out of views. Use cancellation and bounded buffers for background work, dispose native resources, and do not introduce dependencies without a functional need.

Build and run tests using the commands in the README. Include actual verification evidence; distinguish simulated transport tests from live inference. Never label audio as working based on health or a WebSocket handshake. Preserve accessibility, Arabic text rendering, focus behavior and Windows theme integration.

By intentionally submitting a contribution for inclusion, you agree to license it under the Apache License 2.0, subject to its terms. Preserve existing copyright, attribution and license notices, and mark modified files as required by the license.

Do not commit local user data, models, recordings, secrets or build output. Third-party code, dependencies and model weights remain under their own licenses; review [third-party notices](THIRD-PARTY-NOTICES.md) before adding or redistributing them.
