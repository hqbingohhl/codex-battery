using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexBattery {
    internal static class UiText {
        // Plain source messages from the CLI and Pet readers are translated at display time
        // so an in-flight read or a language change can never leave stale translated text.
        static readonly Dictionary<string, string[]> Messages = new Dictionary<string, string[]> {
            { "语言", new[] { "Language", "言語" } },
            { "跟随系统", new[] { "System default", "システム設定に合わせる" } },
            { "设置", new[] { "Settings", "設定" } },
            { "刷新", new[] { "Refresh", "更新" } },
            { "立即刷新", new[] { "Refresh now", "今すぐ更新" } },
            { "收起到托盘", new[] { "Hide to tray", "トレイに格納" } },
            { "正在读取 ChatGPT Pet…", new[] { "Reading ChatGPT Pet…", "ChatGPT のペットを読み込み中…" } },
            { "Codex 剩余额度", new[] { "Codex usage limits", "Codex の利用枠" } },
            { "可用额度", new[] { "Available quota", "利用可能な枠" } },
            { "尚无周期数据", new[] { "No period data yet", "利用期間のデータがありません" } },
            { "重置时间未提供", new[] { "Reset time unavailable", "リセット時刻は未定です" } },
            { "上次剩余", new[] { "last known", "前回の残量" } },
            { "剩余", new[] { "remaining", "残り" } },
            { "重新同步", new[] { "Sync again", "再同期" } },
            { "立即刷新 Codex 额度", new[] { "Refresh Codex usage", "Codex の利用枠を更新" } },
            { "（上次读数）", new[] { " (last known)", "（前回の値）" } },
            { "正在读取…", new[] { "Loading…", "読み込み中…" } },
            { "暂无额度数据", new[] { "No usage data yet", "利用枠のデータがありません" } },
            { "连接异常 · 打开窗口查看原因", new[] { "Connection issue · Open window for details", "接続エラー · 詳細はウィンドウで確認" } },
            { "打开额度窗口…", new[] { "Open usage window…", "利用枠ウィンドウを開く…" } },
            { "退出", new[] { "Quit", "終了" } },
            { "显示周期", new[] { "Quota period", "表示する期間" } },
            { "剩余较少的周期", new[] { "Period with least remaining", "残量が少ない方" } },
            { "等待读取额度", new[] { "Waiting for usage data", "利用枠の読み込み待ち" } },
            { "当前账号仅有{0}", new[] { "Available period · {0}", "利用可能な期間 · {0}" } },
            { "电池颜色", new[] { "Battery color", "バッテリーの色" } },
            { "按余额变色", new[] { "By remaining quota", "残量に応じて変更" } },
            { "青绿色", new[] { "Teal", "ティール" } },
            { "经典单色", new[] { "Monochrome", "モノクロ" } },
            { "托盘图标大小", new[] { "Tray icon size", "トレイアイコンのサイズ" } },
            { "小", new[] { "Small", "小" } },
            { "标准", new[] { "Standard", "標準" } },
            { "大", new[] { "Large", "大" } },
            { "紧凑托盘（仅电池）", new[] { "Compact tray (battery only)", "トレイをコンパクトに（電池のみ）" } },
            { "显示 ChatGPT 的 Pet", new[] { "Show ChatGPT Pet", "ChatGPT のペットを表示" } },
            { "选择备用图片…", new[] { "Choose fallback image…", "代替画像を選択…" } },
            { "移除自选备用图片", new[] { "Remove fallback image", "代替画像を削除" } },
            { "移除备用图片失败。{0}", new[] { "Could not remove the image. {0}", "代替画像を削除できませんでした。{0}" } },
            { "创建跟随 Pet 的快捷方式…", new[] { "Create shortcut with Pet icon…", "ペット連動のショートカットを作成…" } },
            { "窗口宠物大小", new[] { "Window Pet size", "ウィンドウのペットサイズ" } },
            { "{0}（{1} 点）", new[] { "{0} ({1} pt)", "{0}（{1} pt）" } },
            { "自动刷新", new[] { "Auto-refresh", "自動更新" } },
            { "每 {0} 秒", new[] { "Every {0} seconds", "{0} 秒ごと" } },
            { "剩余 20% 时提醒", new[] { "Alert at 20% remaining", "残量 20% で通知" } },
            { "选择 Codex 程序…", new[] { "Choose Codex executable…", "Codex の実行ファイルを選択…" } },
            { "保存设置失败。{0}", new[] { "Could not save settings. {0}", "設定を保存できませんでした。{0}" } },
            { "选择 Codex", new[] { "Choose Codex", "Codex を選択" } },
            { "Codex 程序", new[] { "Codex executable", "Codex 実行ファイル" } },
            { "Codex 额度提醒", new[] { "Codex usage alert", "Codex 利用枠の通知" } },
            { "{0}剩余 {1}%", new[] { "{0} · {1}% remaining", "{0} · 残り {1}%" } },
            { "暂时无法读取本机 Pet，稍后重试", new[] { "Could not read the local Pet. Retrying soon.", "ペットを読み込めません。しばらくしてから再試行します" } },
            { "选择备用 Pet 图片", new[] { "Choose a fallback Pet image", "ペットの代替画像を選択" } },
            { "PNG 或 JPEG 图片", new[] { "PNG or JPEG images", "PNG または JPEG 画像" } },
            { "读取图片失败。{0}", new[] { "Could not read the image. {0}", "画像を読み込めませんでした。{0}" } },
            { "快捷方式图标暂未更新", new[] { "Shortcut icon update pending", "ショートカットのアイコンは更新待ちです" } },
            { "保存跟随 Pet 的快捷方式", new[] { "Save shortcut with Pet icon", "ペット連動のショートカットを保存" } },
            { "Windows 快捷方式", new[] { "Windows shortcuts", "Windows ショートカット" } },
            { "创建快捷方式失败。{0}", new[] { "Could not create the shortcut. {0}", "ショートカットを作成できませんでした。{0}" } },
            { "已到重置时间", new[] { "reset due", "リセット時刻に到達" } },
            { "{0} 天后", new[] { "in {0} days", "{0} 日後" } },
            { "1 天后", new[] { "in 1 day", "1 日後" } },
            { "{0} 小时后", new[] { "in {0} h", "{0} 時間後" } },
            { "{0} 分钟后", new[] { "in {0} min", "{0} 分後" } },
            { "重置于 {0} · {1}", new[] { "Resets {0} · {1}", "{0} にリセット · {1}" } },
            { "连接异常", new[] { "Connection issue", "接続エラー" } },
            { "等待读取", new[] { "Loading", "読み込み待ち" } },
            { "暂无最新额度", new[] { "No current data", "最新のデータがありません" } },
            { "更新于 {0}", new[] { "Updated {0}", "{0} 更新" } },
            { "正在读取额度…", new[] { "Reading usage…", "利用枠を読み込み中…" } },
            { "正在刷新 · {0}", new[] { "Refreshing · {0}", "更新中 · {0}" } },
            { "数据已过期 · {0}", new[] { "Stale data · {0}", "古いデータ · {0}" } },
            { "Pet 原图待同步", new[] { "Waiting for Pet artwork", "ペット画像の同期を待っています" } },
            { "自选备用图片", new[] { "Custom fallback image", "選択した代替画像" } },
            { "自选图片", new[] { "Custom image", "選択した画像" } },
            { "等待 ChatGPT 加载所选 Pet 的图像", new[] { "Waiting for ChatGPT to load the selected Pet", "ChatGPT が選択中のペット画像を読み込むのを待っています" } },
            { "等待本机 ChatGPT 的 Pet 设置", new[] { "Waiting for a ChatGPT Pet selection", "ChatGPT でのペット選択を待っています" } },
            { "ChatGPT 已关闭 Pet", new[] { "Pet is turned off in ChatGPT", "ChatGPT でペットがオフになっています" } },
            { "自定义 Pet", new[] { "Custom Pet", "カスタムペット" } },
            { "暂时无法读取本机 Pet，下次检查时重试", new[] { "Could not read the local Pet. Will retry.", "ペットを読み込めません。次の確認時に再試行します" } },
            { "Pet 使用 WebP，此 Windows 尚无可用图像解码器", new[] { "This Pet uses WebP. A Windows WebP decoder is required.", "このペットには Windows 用の WebP デコーダーが必要です" } },
            { "已同步 · {0}", new[] { "Synced · {0}", "同期済み · {0}" } },
            { "请选择 Windows 快捷方式 (.lnk)。", new[] { "Choose a Windows shortcut (.lnk).", "Windows ショートカット (.lnk) を選択してください。" } },
            { "请选择 16 MB 以内的 PNG 或 JPEG 图片。", new[] { "Choose a PNG or JPEG up to 16 MB.", "16 MB 以下の PNG または JPEG を選択してください。" } },
            { "请选择 PNG 或 JPEG 图片。", new[] { "Choose a PNG or JPEG image.", "PNG または JPEG 画像を選択してください。" } },
            { "请将图片缩小到 2400 万像素以内再导入。", new[] { "Resize the image to 24 megapixels or less before importing.", "画像を 2,400 万画素以下に縮小してから読み込んでください。" } },
            { "文件尺寸异常", new[] { "Invalid file size", "ファイルサイズが正しくありません" } },
            { "设置格式异常", new[] { "Invalid settings format", "設定ファイルの形式が正しくありません" } },
            { "未知周期", new[] { "Unknown period", "期間不明" } },
            { "每周额度", new[] { "Weekly limit", "週間の利用枠" } },
            { "{0} 小时额度", new[] { "{0}-hour limit", "{0} 時間の利用枠" } },
            { "{0} 分钟额度", new[] { "{0}-minute limit", "{0} 分の利用枠" } },
            { "读取超时，请检查网络和 Codex 登录状态。", new[] { "Request timed out. Check your connection and Codex sign-in.", "読み込みがタイムアウトしました。ネットワークと Codex のログイン状態を確認してください。" } },
            { "未找到 Codex CLI。请安装 Codex CLI，或选择其原生 codex.exe。", new[] { "Codex CLI was not found. Install it or select its codex.exe.", "Codex CLI が見つかりません。インストールするか codex.exe を選択してください。" } },
            { "Codex 连接已关闭，将自动重试。", new[] { "Codex closed the connection. Retrying automatically.", "Codex との接続が閉じました。自動で再試行します。" } },
            { "无法启动 Codex。请检查所选程序路径，或重新选择 codex.exe。", new[] { "Could not start Codex. Check the path or select codex.exe again.", "Codex を起動できません。パスを確認するか codex.exe を選び直してください。" } },
            { "Codex 连接已中断，请刷新重试。", new[] { "The Codex connection was interrupted. Refresh to reconnect.", "Codex との接続が切れました。更新して再接続してください。" } },
            { "Codex 连接已关闭，请刷新重试。", new[] { "The Codex connection closed. Refresh to reconnect.", "Codex との接続が閉じました。更新して再接続してください。" } },
            { "请先在 Codex 登录 ChatGPT 账号，再刷新额度。", new[] { "Sign in to ChatGPT in Codex, then refresh usage.", "Codex で ChatGPT にログインしてから利用枠を更新してください。" } },
            { "Codex 暂时无法读取额度，请检查网络和账号后刷新。", new[] { "Usage is unavailable. Check your connection and account, then refresh.", "利用枠を取得できません。ネットワークとアカウントを確認して更新してください。" } },
            { "Codex 返回了无效的响应，请更新 CLI 后重试。", new[] { "Codex returned an invalid response. Update the CLI and try again.", "Codex の応答が無効です。CLI を更新して再試行してください。" } },
            { "Codex 没有返回额度字段，请更新 CLI 后刷新。", new[] { "Codex returned no usage limits. Update the CLI and refresh.", "利用枠が返されませんでした。CLI を更新してから再度読み込んでください。" } },
            { "Codex Battery 启动失败", new[] { "Could not start Codex Battery", "Codex Battery を起動できませんでした" } },
            { "托盘中仍在运行 Codex Battery {0}。请在旧版设置中点「退出」，再双击新版 EXE。", new[] { "Codex Battery {0} is still running in the tray. Quit it from Settings, then open the new EXE.", "トレイで Codex Battery {0} が実行中です。旧版の設定から終了し、新しい EXE を開いてください。" } },
            { "未能加载随附的界面字体，请完整解压 ZIP。", new[] { "The bundled fonts could not be loaded. Extract the entire ZIP.", "付属のフォントを読み込めません。ZIP をすべて展開してください。" } },
        };

        internal static string Language { get; private set; }
        internal static CultureInfo Culture { get; private set; }

        static UiText() { Use("system"); }

        internal static bool IsChoice(string choice) {
            return choice=="system" || choice=="zh" || choice=="ja" || choice=="en";
        }
        internal static void Use(string choice) {
            string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            string requested = choice=="system" || !IsChoice(choice) ? system : choice;
            Language = requested=="zh" || requested=="ja" ? requested : "en";
            Culture = CultureInfo.GetCultureInfo(Language=="zh" ? "zh-CN" : Language=="ja" ? "ja-JP" : "en-US");
        }
        internal static string T(string source) {
            string[] translations;
            if(source==null || Language=="zh" || !Messages.TryGetValue(source,out translations)) return source;
            return translations[Language=="ja" ? 1 : 0];
        }
        internal static string F(string source, params object[] values) {
            return string.Format(Culture,T(source),values);
        }
        internal static string Period(Quota quota) {
            if(!quota.Minutes.HasValue) return T("未知周期");
            int minutes=quota.Minutes.Value;
            if(minutes==10080) return T("每周额度");
            return minutes%60==0 ? F("{0} 小时额度",minutes/60) : F("{0} 分钟额度",minutes);
        }
        internal static string PetStatus(string status) {
            const string synced="已同步 · ";
            return status.StartsWith(synced,StringComparison.Ordinal)
                ? F("已同步 · {0}",status.Substring(synced.Length)=="自定义 Pet" ? T("自定义 Pet") : status.Substring(synced.Length))
                : T(status);
        }
    }
}
