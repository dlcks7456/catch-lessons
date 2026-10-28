<p align="center"><img src="Assets/readme-hero-catchlessons.png" alt="CatchLessons: listen, understand, and keep the context" width="100%"></p>

# CatchLessons

A personal desktop app that captures system audio, shows the source transcript and an optional translation in a floating window, and saves captions and notes locally.

[한국어 문서](README.ko.md)

> **Not instant real-time translation.** Source previews are provisional. Final captions are recognized in audio segments and translated through an agent request, so display can lag and earlier previews can change. Network, agent response, audio segmentation, and hardware all affect delay; no fixed latency is guaranteed.

<p align="center"><img src="Assets/screenshot-main.png" alt="Illustrative CatchLessons window showing a floating caption interface" width="880"></p>

## ✨ What it does

- **Translation or transcription:** view the original and translated captions, or capture only the original.
- **Floating captions:** drag and resize the window; controls hide when the pointer leaves. Adjust font size and opacity.
- **Pause recording:** the pause button stops accepting new audio; already captured speech and queued translation continue. Resume keeps the recording timeline continuous.
- **Notes:** choose a summary category and language, edit category prompts, and save a Markdown or plain-text summary. Run a summary on demand using the finalized transcript, or enable it when stopping.
- **Your agent:** use a signed-in Codex or Claude CLI conversation per recording. Choose a model, effort and optional translation instruction. Codex Fast mode is optional and can consume more of your allowance.
- **Local or API speech recognition:** local Whisper is the default. OpenAI API speech recognition is an alternative and requires an API key.
- **Background app:** close the window to hide it; restore or quit from the tray/menu bar.

Summaries and translations share the recording's agent conversation. A manual summary and a translation request are serialized through that conversation, so a long summary can delay later translations. A summary covers finalized source text; an unfinished preview may not yet be included.

## 📦 Installation status

| Platform | Status | Prerequisites |
| --- | --- | --- |
| Windows x64 | Installer built; isolated install, engine checks, and uninstall passed for API-only and Whisper configurations | Windows, and a signed-in Codex CLI or Claude Code CLI |
| macOS 14+ | Package build scripts prepared; a Mac package has **not** been built or tested | macOS 14+, screen/audio capture permission, and a signed-in agent CLI |

Download the [Windows installer from GitHub Releases](https://github.com/dlcks7456/catch-lessons/releases/latest). The installer is also available locally at `artifacts/installers/CatchLessons-Windows-x64-1.0.0.exe` after building.

During installation, **local Whisper is selected by default**. Deselecting it makes the installation OpenAI-API-only; enter an API key in Connection settings. The installer bundles the .NET runtime and, when selected, the default Whisper and VAD models. The installer checks for agent executables and explains that sign-in is still required; it does not install or log in to an agent for you.

The Windows installer is currently **unsigned**, so Windows reputation checks may warn. The Mac build scripts produce unsigned/ad-hoc artifacts by default; public Mac distribution needs Developer ID signing and notarization. Mac installation, capture, Keychain, and shortcuts have not been verified on a Mac.

## 🚀 Start

1. Install and sign in to at least one agent: [Codex CLI](https://developers.openai.com/codex/cli) or [Claude Code](https://code.claude.com/docs/en/setup).
2. Install CatchLessons and choose whether to include local Whisper. Or, in this development workspace, run `CatchLessons.lnk`.
3. In **Settings → Connection**, choose the agent and speech engine. If using OpenAI API speech recognition, enter its API key.
4. Choose source language (auto-detect or fixed), translation language, and an optional topic hint. Korean is the default translation language; the app interface defaults to English and can be switched to Korean.
5. Start recording. Pause/resume as needed. Stop to save captions and, if enabled, create a summary.

Windows uses system-output loopback, not the microphone. The macOS capture path is prepared but unverified. Translation and summaries require an internet connection to the selected agent even when Whisper is local. The API speech option sends audio to OpenAI and may incur API charges; Codex/Claude usage follows the selected account and plan. Raw WAV recording is optional and off by default.

## ⌨️ Shortcuts

| Action | Default |
| --- | --- |
| Start / stop | `Ctrl + Alt + R` on Windows; `⌘ + Option + R` on Mac |
| Pause / resume | `Ctrl + Alt + P` on Windows; `⌘ + Option + P` on Mac |
| Settings | `Ctrl + Alt + S` on Windows; `⌘ + Option + S` on Mac |

Shortcuts are editable in Settings. Windows registers them globally when available; conflicts are shown in Settings and the conflicting key does not run. The current Mac implementation requires the app to have focus and has not been tested on macOS.

## 🗂️ Output

By default, each recording gets a new folder in `Documents/CatchLessons/`:

```text
<timestamp>_<id>/
├── srt/
│   ├── original.srt
│   └── translation.srt       # translation mode only
├── txt/
│   ├── original.txt
│   └── translation.txt       # translation mode only
├── <generated-title>.<language>.md  # or .txt; when summary enabled/run
└── audio.wav                 # only if original audio recording was enabled
```

A configured post-recording workflow can intentionally create other files. Existing recordings are not migrated or deleted. Settings and API-key storage retain the former CaptionGlass application-data location for compatibility. The app stores its recovery database and session metadata separately in its application-data directory; raw transcription and latency JSONL logs are no longer created. Summary files contain no YAML frontmatter or document links.

## 🔄 Updates

The build checks GitHub Releases at `dlcks7456/catch-lessons` from **Settings → General**.  Users can then check for a newer compatible installer and open its download in the browser. Run the downloaded installer yourself; the app does not silently download or execute it. Private GitHub Releases are not supported.

The app version and installer version come from `Directory.Build.props`. The repository can run `.github/workflows/build-installers.yml` manually to build artifacts; it does not publish a release automatically. The workflow and Mac packaging have not yet run on GitHub.

## 🛠️ Build

### Windows

Requires .NET 10 SDK, Inno Setup 6 and network access if the model files are not already available:

```powershell
./build.ps1
./scripts/build-windows-installer.ps1
```

The installer builder verifies model SHA-256 hashes and writes the standalone executable to `artifacts/installers/`. `./scripts/verify-windows-installer.ps1` tests both installer choices in unique workspace paths and uninstalls those test copies; it refuses to run if an existing CatchLessons installer registration is found.

### macOS — prepared, not verified

On a Mac with .NET 10 SDK, Xcode Command Line Tools and macOS 14 or later:

```bash
bash installer/macos/build-pkg.sh
```

The script builds the app and an architecture-specific package, downloads and verifies models if necessary, and provides an optional local-model installation choice. It has not been run on macOS. Optional `CAPTION_APP_SIGN_IDENTITY` and `CAPTION_INSTALLER_SIGN_IDENTITY` support local signing; notarization is a separate release step.

## 🔒 Data and limits

- Local Whisper processes audio on the device. The selected agent receives transcript text for translation, summary, or configured follow-up instructions.
- Optional WAV capture stores the source audio in the session folder. API speech recognition sends audio to OpenAI.
- Post-recording instructions may use configured tools and MCP under the agent's existing permission policy. Leave the instruction blank to disable the workflow.
- Speaker diarization and separate speaker audio are not implemented.
- User-created category names and instructions remain user data and are not automatically translated when the app UI language changes.

## 🧪 Verification and credits

The current review includes local regression tests, synthetic pause and queue tests, a public audio sample, and isolated Windows installer checks. See [VERIFICATION.md](VERIFICATION.md) for exact results and unverified paths. These checks do not prove live capture quality on every device.

CatchLessons uses Whisper.net, whisper.cpp, Silero VAD, NAudio, SQLite, Avalonia, Phosphor Icons and LobeHub brand assets. Notices are in [Assets/Licenses](Assets/Licenses) and [Assets/Icons](Assets/Icons). Hero and installer illustrations were generated for this project. No application license has been declared yet; review third-party licenses before distribution.


