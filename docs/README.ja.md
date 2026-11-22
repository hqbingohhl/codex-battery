# Codex Battery

[中文](../README.md) · [English](README.en.md)

Mac のメニューバーや Windows の通知領域で、Codex の残り使用枠を電池アイコンの中にパーセントで表示します。メニューから対象の期間とリセット時刻を確認できます。ChatGPT で選んだ Pet を電池の横に表示することもできます。

| ダウンロード | 動作環境 | 起動方法 |
| --- | --- | --- |
| [macOS ユニバーサル版](https://github.com/hqbingohhl/codex-battery/releases/latest/download/Codex-Battery-macOS.zip) | macOS 13 以降、Apple Silicon または Intel | ZIP を展開し、**Codex Battery.app** を `~/Applications` に移して起動します。 |
| [Windows ポータブル版](https://github.com/hqbingohhl/codex-battery/releases/latest/download/Codex-Battery-Windows.zip) | Windows 10 / 11、.NET Framework 4.8 または 4.8.1 | 新しいフォルダーに ZIP 全体を展開し、Assets フォルダーと同じ場所にある **CodexBattery.exe** を起動します。 |

アップデートする前に、旧版のメニューからアプリを終了してください。以前のアプリ識別子を使う Mac 版から更新した場合は、初回起動時に表示設定を選び直してください。macOS 版はアドホック署名で、Apple の公証は受けていません。Windows 版には Authenticode 署名がありません。更新内容と SHA-256 チェックサムは [Releases](https://github.com/hqbingohhl/codex-battery/releases) をご覧ください。

## 使い方

同じパソコンに Codex をインストールし、使用枠を確認したいアカウントでログインします。Windows では Windows 側にインストールした Codex を使用します。アプリはローカルの Codex の `account/rateLimits/read` から、アカウントで利用できる期間のデータを読み取ります。接続が切れた場合は、前回取得した値であることを示します。

**設定 → 言語** で、システムの言語、中文、日本語、English を選べます。設定はパソコンに保存されます。表示する期間、電池の色、アイコンのサイズ、自動更新の間隔も変更できます。詳細ウィンドウを閉じても、メニューバーや通知領域から引き続き確認できます。

Windows のメニューは画面下部のタスクバーから上に開きます。隠れているアイコンは時計の横にドラッグできます。EXE ファイルのアイコンは青い Codex Pet で固定され、起動中のアプリにはそのパソコンで選んだ Pet が表示されます。画像の読み込みを待つ間は、自分の画像を指定することもできます。

## ビルド

macOS では Xcode Command Line Tools をインストールし、`macos` で `zsh build.sh` を実行します。Windows では `windows` で `Build.cmd` を実行し、`Test.cmd` で動作を確認できます。配布物に個人の Pet、アカウント設定、使用履歴は含まれません。Codex Battery は独立したプロジェクトです。製品名および内蔵 Pet の権利は各権利者に帰属します。[フォントに関する表記](../windows/Assets/Fonts/README.md)
