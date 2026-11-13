# NEO development

Use Windows 11 and the .NET 10 SDK. The solution is `NEO.sln`; the application assembly and executable are `NEO`. Namespaces use `NEO`, `NEO.Core`, `NEO.Infrastructure`, and `NEO.Tests`. Keep the workspace directory selected by the owner; its folder name need not match the executable.

Run `dotnet build NEO.sln -c Release` and `dotnet test NEO.sln -c Release`. Build warnings are errors. Run with `dotnet run --project src/App/NEO.csproj -c Release`.

App settings and logs are per-user under local application data. Never commit local configuration, audio, credentials, model weights, CUDA libraries, or build output. No inference engine lifecycle management is enabled.

Manual checks before release: multiple monitors with negative coordinates and mixed DPI; light/dark/high contrast and reduced motion; taskbar placement; focus preservation; keyboard-only operation; shortcut conflicts; fullscreen apps; unplugged monitors; Arabic/English text; tray quit; repeated panel/settings open-close; idle and active resource measurements. Voice-specific checks are pending implementation.

The project source is licensed under Apache-2.0; preserve its `LICENSE` file in source and binary distributions. Keep third-party notices current. Release gates: complete and verify remaining product phases, audit dependency notices and privacy, run CI, publish a versioned package named NEO, and create the owner's GitHub repository as NEO only when publication is authorized.
