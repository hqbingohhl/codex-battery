# Codex Battery

[中文](../README.md) · [日本語](README.ja.md)

See your remaining Codex quota inside a small battery on the macOS menu bar or in the Windows notification area. Open the menu to see the available quota periods and reset times. The app can also show the Pet you selected in ChatGPT.

| Download | Requirements | Getting started |
| --- | --- | --- |
| [macOS universal app](https://github.com/hqbingohhl/codex-battery/releases/latest/download/Codex-Battery-macOS.zip) | macOS 13 or later, Apple Silicon or Intel | Unzip, drag **Codex Battery.app** into `~/Applications`, and open it. |
| [Windows portable app](https://github.com/hqbingohhl/codex-battery/releases/latest/download/Codex-Battery-Windows.zip) | Windows 10 or 11, .NET Framework 4.8 or 4.8.1 | Extract the entire ZIP into a new folder. Keep the Assets folder alongside **CodexBattery.exe** and open the EXE. |

Quit the previous version before upgrading. If your previous Mac build used the earlier app identifier, reselect your display preferences on first launch. The macOS app is ad hoc signed and has not been notarized by Apple. The Windows EXE has no Authenticode signature. See [Releases](https://github.com/hqbingohhl/codex-battery/releases) for release notes and SHA-256 checksums.

## Using the app

Install Codex and sign in on the same computer. On Windows, use a native Windows Codex installation. The app uses the local Codex `account/rateLimits/read` interface and displays the periods offered by your account. It marks stale readings when the connection drops.

Under **Settings → Language**, choose the system language, English, 中文, or 日本語. The selection is saved locally. You can also change the quota period, battery color, icon size and refresh interval. Closing the details window keeps the menu bar or tray icon running.

On Windows the menu opens above the bottom taskbar. Drag a hidden icon next to the clock if desired. The EXE keeps its blue Codex Pet icon. The running app follows your local Pet selection and can use a personal fallback picture while the selected artwork is unavailable.

## Building

On macOS, install the Xcode Command Line Tools and run `zsh build.sh` in `macos`. On Windows, run `Build.cmd` and then `Test.cmd` in `windows`. The package contains no personal Pet, account settings or quota history. Codex Battery is an independent project. Product names and built-in Pet artwork belong to their respective owners. See the [bundled font notices](../windows/Assets/Fonts/README.md).
