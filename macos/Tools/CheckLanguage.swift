import AppKit

@main
struct CheckLanguage {
    static func main() {
        precondition(InterfaceLanguage.system.resolved(using: ["fr-CA", "ja-JP"]) == .japanese)
        precondition(InterfaceLanguage.system.resolved(using: ["zh-Hant-HK"]) == .chinese)
        precondition(InterfaceLanguage.system.resolved(using: ["fr"]) == .english)

        _ = NSApplication.shared
        let delegate = AppDelegate()
        let dashboard = DashboardController()
        let prefs = UserDefaults.standard
        let previous = prefs.object(forKey: "interfaceLanguage")
        defer {
            if let previous { prefs.set(previous, forKey: "interfaceLanguage") }
            else { prefs.removeObject(forKey: "interfaceLanguage") }
        }

        func labels(_ view: NSView) -> [String] {
            let own = (view as? NSTextField).map { [$0.stringValue] } ??
                      (view as? NSButton).map { [$0.title] } ?? []
            return own + view.subviews.flatMap(labels)
        }
        let timestamp = Date().addingTimeInterval(3600).timeIntervalSince1970
        let quotas = Quotas(["rateLimits": ["primary": ["usedPercent": 23, "windowDurationMins": 10080,
                                                         "resetsAt": timestamp]]])
        let error = InterfaceMessage(.missingCLI)
        let fixtures: [(InterfaceLanguage, String, String, String)] = [
            (.japanese, "Codex 利用枠", "週間利用枠", "設定…"),
            (.english, "Codex quota", "Weekly quota", "Settings…"),
            (.chinese, "Codex 剩余额度", "每周额度", "设置…")
        ]
        let menu = NSMenu()
        for (language, title, period, settings) in fixtures {
            delegate.menuNeedsUpdate(menu)
            let languages = menu.items.compactMap(\.submenu).flatMap(\.items).compactMap(\.submenu)
                .first { $0.items.contains { $0.title == "日本語" } }!
            let select = languages.items.first { $0.title == language.menuTitle }!
            precondition(NSApp.sendAction(select.action!, to: select.target, from: select))
            precondition(InterfaceLanguage.preference == language && quotas.primary?.label == period)
            delegate.menuNeedsUpdate(menu)
            precondition(menu.items.first?.title == title)
            let updated = menu.items.compactMap(\.submenu).flatMap(\.items).compactMap(\.submenu)
                .flatMap(\.items).first { $0.title == language.menuTitle }
            precondition(updated?.state == .on)
            dashboard.refreshLanguage()
            dashboard.update(quotas: quotas, lastUpdate: Date(), error: error.text, stale: false,
                             refreshing: false, mode: "primary", colorStyle: "auto", petImage: nil)
            let content = labels(dashboard.window.contentView!)
            precondition([title, period, settings, error.text, "77%"].allSatisfy(content.contains))
            let summary = tr(.quotaDetail, period, 77)
            precondition(summary.contains("77%") && !summary.contains("%@"))
        }
        print("macOS language menu, preference, dashboard, quota, error and system-language checks passed.")
    }
}
