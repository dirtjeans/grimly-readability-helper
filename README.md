# Grimly Readability Helper

A small desktop utility that runs a local LLM ([Microsoft Foundry Local](https://learn.microsoft.com/en-us/azure/ai-foundry/foundry-local/)) against text you've selected anywhere on your machine — no cloud, no API keys, no telemetry. Plus a live grammar / spelling / punctuation panel that flags the kind of mechanical mistakes an LLM is bad at noticing.

## Features

- **Edit anywhere.** Select text in any app, hit the hotkey (default `Ctrl+Alt+G`), and a small editor popup appears with your selection ready for revision.
- **Local LLM, no cloud.** All inference runs on-device through Foundry Local. Models like `phi-4-mini` (CPU) or `qwen2.5-7b-instruct-quantized` (NPU/GPU) work well.
- **Live grammar checker.** A deterministic checker runs ~400 ms after you stop typing, flagging doubled words, lowercase "i", `would of`, `your's`, repeated punctuation, missing leading zeros, and more — without an LLM round-trip.
- **Quick Fix.** One click bundles every deterministic fix into a reviewable diff (accept/reject per change, just like the LLM passes).
- **Word-accurate readability.** If Microsoft Word is installed, the popup can compute the same Flesch reading-ease score Word displays. Otherwise it shows a fast local estimate.

## Install

### Windows (ARM64 or x64)

1. Download the matching `.exe` from the [latest release](../../releases/latest):
   - `GrimlyARM64.exe` for Snapdragon / Surface Pro X / other ARM64 devices
   - `GrimlyX64.exe` for everything else
2. Run it. (Windows SmartScreen will warn the first time — click "More info" → "Run anyway" since the binary isn't code-signed.)
3. Install Foundry Local via the bundled installer prompt, or follow the [Microsoft instructions](https://learn.microsoft.com/en-us/azure/ai-foundry/foundry-local/get-started).

### macOS (Apple Silicon)

1. Download `Grimly-macOS.zip` from the [latest release](../../releases/latest), unzip it.
2. Move `Grimly.app` to `/Applications`.
3. The first time you launch it, macOS Gatekeeper will block it (the app isn't notarized). Right-click the `.app` → "Open" → "Open" in the dialog. After that, normal launching works.
4. Grant Accessibility permissions when prompted (System Settings → Privacy & Security → Accessibility) so Grimly can read selected text from other apps.

## Build from source

### Prerequisites

- **.NET 9 SDK** (Windows build)
- **Xcode 15+** (macOS build)

### Windows

```powershell
git clone https://github.com/<you>/grimly-readability-helper.git
cd grimly-readability-helper
dotnet publish src/Grimly/Grimly.csproj -c Release -r win-arm64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish-arm64
```

The single-file `.exe` will be at `publish-arm64/Grimly.exe`. Replace `win-arm64` with `win-x64` for the Intel build.

### macOS

```bash
cd mac/Grimly
xcodebuild -project Grimly.xcodeproj -scheme Grimly -configuration Release \
  -derivedDataPath build clean build
# The built .app is at build/Build/Products/Release/Grimly.app
ditto -c -k --keepParent build/Build/Products/Release/Grimly.app Grimly-macOS.zip
```

## Releasing

Push a version tag and the [release workflow](.github/workflows/release.yml) will build both Windows binaries and create a draft release with them attached:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The Mac binary has to be built locally (no signing secrets in CI) and added to the draft release manually.

## Acknowledgements

- [Hunspell en_US dictionary](https://github.com/wooorm/dictionaries) — MIT
- [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell) — MIT
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MIT
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — CPOL

## License

[MIT](LICENSE) — Copyright © 2026 Kenneth Spencer Brown
