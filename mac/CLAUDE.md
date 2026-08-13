# Note for a future Claude — Mac side of Grimly

You're picking up mid-project. The user is Kenneth Spencer Brown; the project is **Grimly**, a local-LLM writing assistant (Windows + macOS) that runs on local LLM runtimes. Windows is the primary platform. **Mac status: v1.1.0/v1.2.0 features were ported in commit `ad12d49` (2026-08-05). Windows has since shipped v1.3.0 — see the next section for what to port and what to skip.**

## Catch-up as of 2026-08-13 — Windows is at v1.3.0

Release: <https://github.com/dirtjeans/grimly-readability-helper/releases/tag/v1.3.0> (Windows binaries attached; **`Grimly.app.zip` slot is empty — build and attach one** with `gh release upload v1.3.0 Grimly.app.zip --clobber`).

### Port to Mac (works cross-platform)

1. **External local-LLM providers — Ollama and LM Studio.** Windows added `ExternalLlmProviderService.cs`: probes each provider's localhost server (Ollama `http://localhost:11434`, list via `/api/tags`; LM Studio `http://localhost:1234`, list via `/v1/models`), injects their models into the model picker prefixed `ollama:` / `lmstudio:`, and routes chat requests to the provider's OpenAI-compatible `/v1/chat/completions` with the prefix stripped. Both apps exist on macOS with the same ports/CLIs — this ports directly. Key integration points to mirror in Swift:
   - Routing lives in `FoundryLocalClient.cs` (prefix match → different base URL)
   - `FoundryManager.IsNonFoundryModel()` guards keep Foundry warm-up/reconnect/status from clobbering a non-Foundry selection — **don't skip these**, they prevent the "my model choice keeps reverting" bug
   - Auto-start installed-but-idle servers: spawn `ollama serve` / `lms server start` detached, poll the list endpoint (~8 s window). Mac paths: `ollama` on PATH or `/usr/local/bin` / `/opt/homebrew/bin`; `~/.lmstudio/bin/lms`
   - Settings UI: providers detected on disk show a "Start" link, missing ones an "Install" link (macOS: link to vendor sites or `brew install ollama` — Windows uses winget), plus hint text "Ollama and LM Studio models appear here if running", provider icons in the model list (🦙 for Ollama, purple LM badge, Microsoft squares for Foundry)
2. **Settings save-preservation bug** — Windows `SettingsViewModel.Save()` was constructing a fresh settings object, silently resetting fields the dialog doesn't edit. If the Mac settings save does the same (check `SettingsView`/whatever persists), fix it the same way: load-mutate-save.
3. **Gear button in the popup title bar** that opens Settings directly (tray/menu-bar only was too buried), and refresh the popup's "Connected · model" status line right after settings save so it doesn't show a stale model name.
4. **Model browser blurb** updated to: "Pick a model. Foundry Local downloads can take several minutes on the first fetch; models already on this PC — cached, Windows AI, or served by Ollama, LM Studio, or GenieX — switch instantly." (Adapt the provider list to what the Mac build actually supports.)

### Skip on Mac (platform-specific)

- **Windows AI / Aion Instruct (`windows-ai` virtual model)** — Copilot+ Windows NPU only (WinRT `AionInstructPreview.Text`). No macOS equivalent; a future Apple-Intelligence analog would be its own design conversation.
- **Qualcomm GenieX provider** (`geniex:` prefix, port 18181) — Snapdragon-only runtime. Harmless to include the passive probe, but there's nothing to detect on a Mac; fine to omit.
- **winget one-click installs** — Windows-only mechanism; on Mac link out or use Homebrew.

## Historical: catch-up as of 2026-08-03 — Windows at v1.2.0 (PORTED — done in `ad12d49`, kept for reference)

Ported source targets (public repo): `source/grimly-readability-helper/src/Grimly.Core/`. Where a Windows path is cited below, look for the equivalent Swift file under `mac/Grimly/{Services,ViewModels,Views}`.

### v1.1.0 (2026-07-10)

1. **Sentence case removed.** Case dropdown now has AP title case + Chicago title case only. Mac may still have Sentence case in `CaseFormatter.swift`; strip the option and its menu entry. See §"(Note on sentence case)" below for the reasoning.
2. **Model browser: two-tier picker in Settings** with NPU/GPU filter checkboxes. On Mac, adapt to the Mac equivalent settings pane if one exists. The Mac hardware story is different (no NPU on most Macs), so a simpler single-list picker is probably fine — talk to the user before investing in the two-tier UI.
3. **LLM output language anchor.** The system prompt now begins with `"Reply in the same language as the input text. If the input is in English, respond in English."` — added because Qwen models drifted to Chinese. Copy this prepend into whatever Mac uses to assemble system prompts (likely `FoundryLocalClient.swift`).
4. **About tray menu item.** Already covered in §"About menu item" below — do this.

### v1.2.0 (2026-08-03) — this session

**New AP Style pipeline** — public Grimly gained a dedicated AP Style button (Row 3 of the popup grid, orange outline) that runs a deterministic code pass followed by an LLM pass for judgment calls. Ports needed:

- **`ApStyleCodePass.cs`** → create `mac/Grimly/Services/APStyleCodePass.swift`. Idempotent regex-based rewriter. Rules currently covered:
  - `10 percent` → `10%`, `&` → `and` (skipping proper-noun contexts)
  - Time format: `10:00 AM` → `10 a.m.` (drops `:00`, lowercase, periods)
  - Month abbreviations with dates: `January 5` → `Jan. 5` (Jan/Feb/Aug/Sept/Oct/Nov/Dec only; March–July stay spelled out but strip ordinals)
  - Courtesy titles: `Mr Smith` → `Mr. Smith` (Mr./Mrs./Ms./Dr.)
  - Political/military titles before names: `Senator Warren` → `Sen. Warren` (Sen./Rep./Gov./Lt. Gov./Gen./Col./Maj./Capt./Lt./Sgt.)
  - `over N` → `more than N` for numeric quantities
  - Single digits `1..9` spelled out (`3 reasons` → `three reasons`) with a long negative-lookahead over unit/age/time/money exceptions
  - State abbreviations after city names: `Boston, Massachusetts` → `Boston, Mass.` (list of 42 states; skip Alaska/Hawaii/Idaho/Iowa/Maine/Ohio/Texas/Utah — never abbreviated)
  - Address suffixes with numbered addresses: `1600 Pennsylvania Avenue` → `1600 Pennsylvania Ave.` (Avenue/Boulevard/Street only)
  - Directional address abbrevs: `100 East Main St.` → `100 E. Main St.` (E./W./N./S. only when preceded by a house number)
  - Company suffix commas dropped: `Apple, Inc.` → `Apple Inc.` (Inc./Corp./Co./Ltd./LLC — 2019 AP change)
  - Decade apostrophes: `1990's` → `1990s`, `90's` → `'90s`
  - Middle-initial periods: `John F Kennedy` → `John F. Kennedy`
  - Multiple-space collapse: `  +` → ` `
  - Ordinal-date strip for March–July: `March 5th` → `March 5`
  - Oxford comma REMOVAL: `red, white, and blue` → `red, white and blue` (public Grimly is AP-strict; StyleHelper reverses this)
  - Em-dash spacing removed: `word — word` → `word—word`; ASCII `--` → `—`

- **`ApStylePipeline.cs`** → create `mac/Grimly/Services/APStylePipeline.swift`. Two steps: run the code pass, then send the result through the LLM with a narrowly-scoped prompt covering only three categories:
  1. Attribution verbs: prefer `said` over `claimed`/`stated`/`noted`/`commented`/`remarked`/`expressed`/`declared`
  2. Passive-voice attribution: `was said by X` → `X said`
  3. Editorial framing: strip alarmist/promotional adjectives (`groundbreaking`, `shocking`, `unprecedented`) unless in a quote

- **Prompt-echo guard.** Both `ApStylePipeline.cs` and (in the private Illumio pipeline) `StyleGuidePipeline.cs` post-process LLM output to discard responses that contain prompt sentinel phrases or common meta-commentary preambles (`"You're about to"`, `"Here's the revised"`, `"Remember,"`, etc.). When the guard trips, fall back to the code-pass output. Mac needs the same guard — small on-device models parrot prompts too. The signature list is in `ApStylePipeline.cs` at the bottom of the class.

- **AP Style button in the popup.** In WPF this is `EditorPopupWindow.xaml` Row 3 with orange outline styling and `Command="{Binding RunApStyleCommand}"`. On Mac, the equivalent goes into `EditorPopupView.swift` alongside the mode pills.

- **Animated LLM glow border** (replaces the old "Revising…" progress bar). Overlays the working-text box with a red/blue/purple linear-gradient border while `isLoading` is true. Windows XAML uses `LinearGradientBrush` + a `RotateTransform` on `RelativeTransform` + a `DoubleAnimation` (0→360°, 1.5s) plus an opacity pulse (1.0↔0.55, 0.9s AutoReverse), wrapped with a `DropShadowEffect` for glow. SwiftUI equivalent: an `AngularGradient` (or `LinearGradient` with `.rotationEffect` animated) inside a `.stroke` on a `RoundedRectangle` overlay, plus `.opacity` animated on a `.repeatForever().autoreverses()` timeline. Only visible when `isLoading == true`.
  - Colors: `#3B82F6` (blue) → `#8B5CF6` (purple) → `#EF4444` (red)
  - Border thickness: 4pt, corner radius: 4pt, margin: -3pt so it sits just outside the text box
  - Remove the old "Revising…" progress bar wherever the Mac has it

### Version + release tag

The next release is **v1.2.0**. Update the About-menu version string and the release-upload command below (`v1.0.0` → `v1.2.0`).

## What Grimly does (one paragraph)

Select text in any app → hit a hotkey → a popup opens with the selection → user picks a rewrite mode (Fix Grammar, Shorter Sentences, Active Voice, Case, etc.) → the LLM revises → user reviews per-change → paste back. There's also a live deterministic grammar/spelling/punctuation panel that flags issues before any button click, plus a Quick Fix button that batches the deterministic corrections into a reviewable diff.

## Where things are

- Everything on the Mac lives under `mac/` in this repo
- Xcode project: `mac/Grimly.xcodeproj`
- Sources: `mac/Grimly/{Models,Services,ViewModels,Views}`
- Resources (bundle content): `mac/Grimly/Resources`

## What's already in the Mac source (as of last commit)

Recent Mac work already committed but NOT YET IN THE XCODE PROJECT (needs a drag-into-Xcode step):

1. **`mac/Grimly/Models/Violation.swift`** — new
2. **`mac/Grimly/Services/SpellCheckerService.swift`** — new (wraps `NSSpellChecker.shared`)
3. **`mac/Grimly/Services/GrammarChecker.swift`** — new (deterministic checker: doubled words, `your's`, weak adverbs, spelling, etc.)
4. **`mac/Grimly/Services/CaseFormatter.swift`** — new (AP/Chicago title case + sentence case with proper-noun preservation)
5. **`mac/Grimly/Services/ProperNounService.swift`** — new (embedded ~5,500-entry proper-noun list + ambiguous stoplist + multi-word bigrams)
6. **`mac/Grimly/Resources/proper_nouns.txt`** — new (plain-text list, one entry per line)

The existing files `EditorPopupViewModel.swift` and `EditorPopupView.swift` have already been updated to reference these new files.

## Step 1: build sanity check in Xcode (do this first)

```bash
cd mac/Grimly
open Grimly.xcodeproj
```

Then in Xcode:

1. **Right-click the `Models` group in the project navigator → Add Files to "Grimly"…** → select `Violation.swift` → uncheck "Copy items if needed" (the file is already in place), verify the Grimly target is checked → Add.
2. Same for the `Services` group: add `SpellCheckerService.swift`, `GrammarChecker.swift`, `CaseFormatter.swift`, `ProperNounService.swift` (four files).
3. Right-click the `Resources` group → Add Files → `proper_nouns.txt`. Uncheck "Copy items if needed", check the Grimly target so it's bundled at build time.
4. Hit **⌘B**. If it builds, you're set.

Known gotcha: the Xcode project had stale references to `Illumio*.swift` files from earlier work — those were stripped in commit `25554f6`. If Xcode still complains about a missing `IllumioTheme.swift` or `IllumioModePillButton.swift`, someone re-added them by mistake; delete those references.

## Step 2: one feature that exists on Windows but NOT on Mac yet

### About menu item

Windows added an "About" item to the tray-icon context menu that shows a `MessageBox` with app name + version + build date. Mac doesn't have a tray menu — the natural home is the AppKit main menu bar (App menu → About Grimly) or the menu-bar-icon menu (`MenuBarView.swift`).

Simplest approach: add a `Button("About Grimly") { showAbout() }` to `MenuBarView.swift`, where `showAbout()` calls `NSApplication.shared.orderFrontStandardAboutPanel(nil)` with a custom credits string. The version comes from `Bundle.main.infoDictionary?["CFBundleShortVersionString"]` (set in Info.plist).

Version to display: **1.2.0**. Build date: read the app bundle's modification date.

### (Note on sentence case)

A prior version of Grimly had Sentence case and an LLM refinement pass that quietly double-checked proper-noun capitalization. Both were removed. Sentence case turned out to be a use case writers rarely need to look up (unlike title case, where AP vs Chicago matters), and phi-3-mini's context reading wasn't strong enough to disambiguate common-word/proper-noun collisions (nice/Nice, apple/Apple). The Case dropdown now offers **AP title case** and **Chicago title case** only. Don't add sentence case back without a fresh design conversation with the user.

## Step 3: produce Grimly.app.zip and upload to the release

Once the Xcode build is green:

```bash
cd mac/Grimly

xcodebuild \
  -project Grimly.xcodeproj \
  -scheme Grimly \
  -configuration Release \
  -derivedDataPath build \
  CODE_SIGN_IDENTITY="-" \
  CODE_SIGNING_REQUIRED=NO \
  CODE_SIGNING_ALLOWED=NO \
  clean build

cd build/Build/Products/Release
ditto -c -k --keepParent Grimly.app ../../../../Grimly.app.zip

# Attach to the v1.2.0 release (current)
cd ../../../..
gh release upload v1.2.0 Grimly.app.zip \
  --repo dirtjeans/grimly-readability-helper --clobber
```

`CODE_SIGN_IDENTITY="-"` ad-hoc signs. First launch on any other user's machine will trip Gatekeeper — right-click → Open → Open. That's expected for an unsigned release; no notarization until Grimly gets real usage.

## Repo status snapshot

- Public repo: <https://github.com/dirtjeans/grimly-readability-helper>
- Windows binaries are attached to release v1.2.0 (`Grimly-windows-x64.exe`, `Grimly-windows-arm64.exe`)
- `Grimly.app.zip` slot on v1.2.0 is empty — that's what you're producing
- `main` branch is where all changes go; no PR flow needed

## Style / preferences

- Concise output, no headers-and-sections for simple questions
- Match the existing code style — no reformatting drive-bys
- Don't add features beyond what's asked; small focused PRs

Good luck. Ping the user if the Xcode build fails and you can't figure out why from the error text — they know the project structure well.
