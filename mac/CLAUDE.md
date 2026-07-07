# Note for a future Claude — Mac side of Grimly

You're picking up mid-project. The user is Kenneth Spencer Brown; the project is **Grimly**, a local-LLM writing assistant (Windows + macOS) that runs on Microsoft Foundry Local. Windows is the primary platform and is fully caught up; **the Mac is one version behind** and needs a build + some source ports.

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

Version to display: **1.0.0**. Build date: read the app bundle's modification date.

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

# Attach to the v1.0.0 release
cd ../../../..
gh release upload v1.0.0 Grimly.app.zip \
  --repo dirtjeans/grimly-readability-helper --clobber
```

`CODE_SIGN_IDENTITY="-"` ad-hoc signs. First launch on any other user's machine will trip Gatekeeper — right-click → Open → Open. That's expected for an unsigned release; no notarization until Grimly gets real usage.

## Repo status snapshot

- Public repo: <https://github.com/dirtjeans/grimly-readability-helper>
- Windows binaries are attached to release v1.0.0 (`GrimlyARM64.exe`, `GrimlyX64.exe`)
- `Grimly.app.zip` slot is empty — that's what you're producing
- `main` branch is where all changes go; no PR flow needed

## Style / preferences

- Concise output, no headers-and-sections for simple questions
- Match the existing code style — no reformatting drive-bys
- Don't add features beyond what's asked; small focused PRs

Good luck. Ping the user if the Xcode build fails and you can't figure out why from the error text — they know the project structure well.
