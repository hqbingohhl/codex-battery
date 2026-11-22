import Foundation

enum InterfaceLanguage: String, CaseIterable {
    case system, chinese = "zh-Hans", japanese = "ja", english = "en"

    static var preference: Self {
        Self(rawValue: UserDefaults.standard.string(forKey: "interfaceLanguage") ?? "") ?? .system
    }

    func resolved(using preferred: [String] = Locale.preferredLanguages) -> Self {
        guard self == .system else { return self }
        for identifier in preferred {
            switch identifier.lowercased().split(whereSeparator: { $0 == "-" || $0 == "_" }).first {
            case "zh": return .chinese
            case "ja": return .japanese
            case "en": return .english
            default: continue
            }
        }
        return .english
    }

    var menuTitle: String {
        switch self {
        case .system: return tr(.system)
        case .chinese: return "简体中文"
        case .japanese: return "日本語"
        case .english: return "English"
        }
    }

    static func date(_ date: Date, template: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: preference.resolved().rawValue)
        formatter.timeZone = .autoupdatingCurrent
        formatter.setLocalizedDateFormatFromTemplate(template)
        return formatter.string(from: date)
    }
}

enum CopyKey: CaseIterable {
    case loadingPet
    case badPet
    case readingQuota
    case awaitingQuota
    case emptyPeriod
    case firstPeriod
    case secondPeriod
    case lowestPeriod
    case period
    case automaticColor
    case teal
    case monochrome
    case color
    case small
    case regular
    case large
    case iconSize
    case compact
    case finderPending
    case sync
    case showPet
    case blink
    case portraitSize
    case cadence
    case lowAlert
    case openWindow
    case refreshQuota
    case hide
    case quitApp
    case lastQuota
    case title
    case reading
    case empty
    case connectionIssue
    case openWindowDots
    case refreshNow
    case settings
    case quit
    case unknownPeriod
    case weekly
    case missingCLI
    case closed
    case invalidData
    case cannotConnect
    case timedOut
    case login
    case unavailable
    case missingQuota
    case remaining
    case lastRemaining
    case noData
    case resetDue
    case lessMinute
    case noWindow
    case resetUnknown
    case readingQuotaDots
    case refresh
    case availableQuota
    case lowest
    case refreshAccess
    case menuDisplay
    case modeAccess
    case settingsDots
    case refreshingDots
    case refreshing
    case failed
    case waiting
    case expired
    case noAccountQuota
    case updated
    case waitingPet
    case customPet
    case waitCloudPet
    case disabledPet
    case waitBuiltinPet
    case waitLocalPet
    case language
    case system
    case petDecode
    case hourQuota
    case minuteQuota
    case quotaDetail
    case only
    case points
    case seconds
    case resetTime
    case resets
    case minutesUntil
    case hoursUntil
    case daysUntil
    case lastUpdate
    case updateTime
    case launchError
    case synced

    func render(_ values: [String] = [], language: InterfaceLanguage = .preference) -> String {
        let copy: (zh: String, ja: String, en: String)
        switch self {
        case .loadingPet: copy = ("正在读取 ChatGPT 的 Pet 设置", "ChatGPT の Pet 設定を読み込み中", "Reading ChatGPT Pet preferences")
        case .badPet: copy = ("暂时无法显示这个 Pet 的图像", "この Pet の画像を表示できません", "This Pet image is unavailable")
        case .readingQuota: copy = ("正在读取额度", "利用枠を読み込み中", "Reading quota")
        case .awaitingQuota: copy = ("等待读取最新额度", "最新の利用枠を待機中", "Waiting for the latest quota")
        case .emptyPeriod: copy = ("该周期暂无额度数据", "この期間のデータはありません", "No data for this period")
        case .firstPeriod: copy = ("第一周期", "第1期間", "First period")
        case .secondPeriod: copy = ("第二周期", "第2期間", "Second period")
        case .lowestPeriod: copy = ("剩余较少的周期", "残量が少ない期間", "Period with least remaining")
        case .period: copy = ("显示周期", "表示する期間", "Display period")
        case .automaticColor: copy = ("按余额变色", "残量に応じて変更", "Color by remaining quota")
        case .teal: copy = ("青绿色", "ティール", "Teal")
        case .monochrome: copy = ("经典单色", "モノクロ", "Monochrome")
        case .color: copy = ("电池颜色", "バッテリーの色", "Battery color")
        case .small: copy = ("小", "小", "Small")
        case .regular: copy = ("标准", "標準", "Standard")
        case .large: copy = ("大", "大", "Large")
        case .iconSize: copy = ("菜单栏图标大小", "メニューバーのアイコンサイズ", "Menu bar icon size")
        case .compact: copy = ("紧凑菜单栏（仅电池）", "コンパクト表示（バッテリーのみ）", "Compact menu bar (battery only)")
        case .finderPending: copy = ("访达图标等待重新同步", "Finder アイコンの同期を待機中", "Finder icon sync pending")
        case .sync: copy = ("重新同步", "再同期", "Sync again")
        case .showPet: copy = ("显示 ChatGPT 的 Pet", "ChatGPT の Pet を表示", "Show ChatGPT Pet")
        case .blink: copy = ("允许宠物眨眼", "Pet のまばたきを表示", "Animate Pet blinks")
        case .portraitSize: copy = ("窗口宠物大小", "ウインドウの Pet サイズ", "Window Pet size")
        case .cadence: copy = ("自动刷新", "自動更新", "Auto-refresh")
        case .lowAlert: copy = ("剩余 20% 时提示音", "残量 20% で通知音を鳴らす", "Play a sound at 20% remaining")
        case .openWindow: copy = ("打开额度窗口", "利用枠ウインドウを開く", "Open quota window")
        case .refreshQuota: copy = ("刷新额度", "利用枠を更新", "Refresh quota")
        case .hide: copy = ("收起到菜单栏", "メニューバーに格納", "Hide to menu bar")
        case .quitApp: copy = ("退出 Codex Battery", "Codex Battery を終了", "Quit Codex Battery")
        case .lastQuota: copy = ("最近一次读取的额度", "前回取得した利用枠", "Last known quota")
        case .title: copy = ("Codex 剩余额度", "Codex 利用枠", "Codex quota")
        case .reading: copy = ("正在读取…", "読み込み中…", "Loading…")
        case .empty: copy = ("暂无额度数据", "利用枠のデータがありません", "No quota data")
        case .connectionIssue: copy = ("连接异常 · 打开窗口查看原因", "接続エラー · ウインドウで詳細を確認", "Connection issue · Open window for details")
        case .openWindowDots: copy = ("打开额度窗口…", "利用枠ウインドウを開く…", "Open quota window…")
        case .refreshNow: copy = ("立即刷新", "今すぐ更新", "Refresh now")
        case .settings: copy = ("设置", "設定", "Settings")
        case .quit: copy = ("退出", "終了", "Quit")
        case .unknownPeriod: copy = ("未知周期", "期間不明", "Unknown period")
        case .weekly: copy = ("每周额度", "週間利用枠", "Weekly quota")
        case .missingCLI: copy = ("找不到 Codex CLI。请安装 Codex 并登录，然后刷新。", "Codex CLI が見つかりません。Codex をインストールしてログインし、更新してください。", "Codex CLI was not found. Install Codex, sign in, then refresh.")
        case .closed: copy = ("Codex 连接已关闭，请刷新重连。", "Codex との接続が終了しました。更新して再接続してください。", "The Codex connection closed. Refresh to reconnect.")
        case .invalidData: copy = ("Codex 返回的数据无法解析，请更新 CLI 后刷新。", "Codex の応答を読み取れません。CLI を更新してから再読み込みしてください。", "The Codex response could not be read. Update the CLI, then refresh.")
        case .cannotConnect: copy = ("无法连接 Codex 用量服务，请刷新重试。", "Codex の使用量サービスに接続できません。更新して再試行してください。", "Could not connect to the Codex usage service. Refresh to retry.")
        case .timedOut: copy = ("用量读取超时，请检查网络或稍后刷新。", "読み込みがタイムアウトしました。接続を確認するか、後ほど更新してください。", "The quota request timed out. Check your connection or refresh later.")
        case .login: copy = ("请在 Codex 中登录 ChatGPT 账号，然后刷新额度。", "Codex で ChatGPT にログインしてから、利用枠を更新してください。", "Sign in to ChatGPT in Codex, then refresh your quota.")
        case .unavailable: copy = ("Codex 暂时无法读取额度，请检查网络和账号状态后刷新。", "利用枠を取得できません。接続とアカウントの状態を確認して更新してください。", "Codex could not read your quota. Check your connection and account, then refresh.")
        case .missingQuota: copy = ("Codex 用量响应缺少额度字段，请更新 CLI 后刷新。", "Codex の応答に利用枠データがありません。CLI を更新してから再読み込みしてください。", "The Codex response omitted quota data. Update the CLI, then refresh.")
        case .remaining: copy = ("剩余", "残り", "remaining")
        case .lastRemaining: copy = ("上次剩余", "前回の残量", "last known")
        case .noData: copy = ("暂无数据", "データなし", "No data")
        case .resetDue: copy = ("已到重置时间", "リセット時刻を経過", "Reset due")
        case .lessMinute: copy = ("不足 1 分钟", "1分以内", "in under a minute")
        case .noWindow: copy = ("尚无周期数据", "期間データがありません", "No period data yet")
        case .resetUnknown: copy = ("重置时间未提供", "リセット時刻は不明です", "Reset time unavailable")
        case .readingQuotaDots: copy = ("正在读取额度…", "利用枠を読み込み中…", "Reading quota…")
        case .refresh: copy = ("刷新", "更新", "Refresh")
        case .availableQuota: copy = ("可用额度", "利用可能な枠", "Available quota")
        case .lowest: copy = ("剩余较少者", "少ない方", "Lowest")
        case .refreshAccess: copy = ("立即刷新 Codex 额度", "Codex の利用枠を今すぐ更新", "Refresh Codex quota now")
        case .menuDisplay: copy = ("菜单栏显示", "メニューバー表示", "Menu bar")
        case .modeAccess: copy = ("菜单栏显示的额度周期", "メニューバーに表示する利用枠の期間", "Quota period shown in the menu bar")
        case .settingsDots: copy = ("设置…", "設定…", "Settings…")
        case .refreshingDots: copy = ("刷新中…", "更新中…", "Refreshing…")
        case .refreshing: copy = ("正在刷新", "更新中", "Refreshing")
        case .failed: copy = ("更新失败", "更新に失敗", "Update failed")
        case .waiting: copy = ("等待读取额度", "利用枠の取得を待機中", "Waiting for quota")
        case .expired: copy = ("数据已过期", "データが古くなっています", "Data out of date")
        case .noAccountQuota: copy = ("当前账户未返回周期额度", "このアカウントの期間別利用枠はありません", "No quota periods for this account")
        case .updated: copy = ("已更新", "更新しました", "Updated")
        case .waitingPet: copy = ("等待读取 ChatGPT 的 Pet 设置", "ChatGPT の Pet 設定を待機中", "Waiting for ChatGPT Pet preferences")
        case .customPet: copy = ("自定义 Pet", "カスタム Pet", "Custom Pet")
        case .waitCloudPet: copy = ("等待 ChatGPT 缓存所选 Pet", "選択した Pet の画像を待機中", "Waiting for the selected Pet image")
        case .disabledPet: copy = ("ChatGPT 已关闭 Pet", "ChatGPT で Pet がオフになっています", "Pet is turned off in ChatGPT")
        case .waitBuiltinPet: copy = ("等待 ChatGPT 内置 Pet 图像", "内蔵 Pet の画像を待機中", "Waiting for the built-in Pet image")
        case .waitLocalPet: copy = ("等待本地 Pet 图像", "ローカルの Pet 画像を待機中", "Waiting for the local Pet image")
        case .language: copy = ("语言", "言語", "Language")
        case .system: copy = ("跟随系统", "システム設定に従う", "System default")
        case .petDecode: copy = ("Pet 图像无法解码", "Pet 画像を読み込めません", "Could not decode the Pet image")
        case .hourQuota: copy = ("%@ 小时额度", "%@ 時間枠", "%@-hour quota")
        case .minuteQuota: copy = ("%@ 分钟额度", "%@ 分間枠", "%@-minute quota")
        case .quotaDetail: copy = ("%@剩余 %@%%", "%@の残量 %@%%", "%@: %@%% remaining")
        case .only: copy = ("当前账号仅有%@", "利用できる期間は%@のみです", "Only available period: %@")
        case .points: copy = ("%@（%@ 点）", "%@（%@ pt）", "%@ (%@ pt)")
        case .seconds: copy = ("每 %@ 秒", "%@ 秒ごと", "Every %@ seconds")
        case .resetTime: copy = ("重置时间 %@", "リセット日時 %@", "Resets %@")
        case .resets: copy = ("重置  %@", "リセット %@", "Resets %@")
        case .minutesUntil: copy = ("约 %@ 分钟后", "約%@分後", "in ~%@ min")
        case .hoursUntil: copy = ("%@小时%@分后", "%@時間%@分後", "in %@h %@m")
        case .daysUntil: copy = ("%@天%@小时后", "%@日%@時間後", "in %@d %@h")
        case .lastUpdate: copy = ("上次更新于 %@", "前回の更新 %@", "Last updated %@")
        case .updateTime: copy = ("更新于 %@", "更新 %@", "Updated %@")
        case .launchError: copy = ("无法启动 Codex：%@", "Codex を起動できません：%@", "Could not start Codex: %@")
        case .synced: copy = ("已同步 · %@", "同期済み · %@", "Synced · %@")
        }
        let template: String
        switch language.resolved() {
        case .chinese: template = copy.zh
        case .japanese: template = copy.ja
        default: template = copy.en
        }
        return values.isEmpty ? template : String(format: template, arguments: values)
    }
}

func tr(_ key: CopyKey, _ arguments: CustomStringConvertible...) -> String {
    key.render(arguments.map(\.description))
}

// Store messages across polls so changing languages also updates an existing error.
struct InterfaceMessage: Equatable {
    let key: CopyKey
    var detail: String?
    init(_ key: CopyKey, _ detail: String? = nil) { self.key = key; self.detail = detail }
    var text: String { key.render(detail.map { [$0] } ?? []) }
}
