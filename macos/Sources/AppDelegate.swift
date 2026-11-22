import Cocoa
import CryptoKit

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate, NSWindowDelegate {
    private let client = UsageClient()
    private let petSource = PetSource()
    private let applicationIcon = ApplicationIconController()
    private var iconPose: CGImage?
    private var iconArtworkID: String?
    private let petDecoder = DispatchQueue(label: "app.codexbattery.macos.pet-render", qos: .userInitiated)
    private var petRevision = UUID()
    private var petStatus: () -> String = { tr(.loadingPet) }
    private var petName: String?
    private let defaults = UserDefaults.standard
    private var item: NSStatusItem!
    private var panel: DashboardController?
    private var snapshot: Quotas?
    private var lastUpdate: Date?
    private var usageError: InterfaceMessage?
    private var errorMessage: String? { usageError?.text }
    private var nextRead: Timer?
    private var blink: Timer?
    private var frame = 0
    private var failures = 0
    private var sleeping = false
    private var stopping = false
    private var screenAsleep = false
    private var tracking = false
    private var appearance: NSKeyValueObservation?
    private var observers: [(NotificationCenter, NSObjectProtocol)] = []
    private let reopen = Notification.Name("app.codexbattery.macos.reopen")

    private var mode: String { defaults.string(forKey: "window") ?? "primary" }
    private var colorStyle: String { defaults.string(forKey: "colorStyle") ?? "auto" }
    private var showPet: Bool { defaults.object(forKey: "showPet") as? Bool ?? true }
    private var motion: Bool { defaults.object(forKey: "animatePet") as? Bool ?? true }
    private var compact: Bool { defaults.bool(forKey: "compactMenuBar") }
    private var menuSize: MenuArtSize { MenuArtSize(rawValue: defaults.string(forKey: "menuArtSize") ?? "") ?? .small }
    private static let portraitSizes = [32, 44, 64]
    private var portraitSize: Int {
        let value = defaults.integer(forKey: "portraitSize")
        return Self.portraitSizes.contains(value) ? value : 44
    }
    private var interval: TimeInterval {
        let seconds = defaults.double(forKey: "refreshSeconds")
        return [30, 60, 120].contains(seconds) ? seconds : 60
    }
    private var windows: [WindowQuota] { [snapshot?.primary, snapshot?.secondary].compactMap { $0 } }
    private var selected: WindowQuota? {
        switch mode {
        case "minimum": return windows.min { $0.remaining < $1.remaining }
        case "secondary": return snapshot?.secondary ?? snapshot?.primary
        default: return snapshot?.primary ?? snapshot?.secondary
        }
    }
    private var stale: Bool {
        guard let last = lastUpdate, errorMessage == nil else { return true }
        if Date().timeIntervalSince(last) > 180 { return true }
        return selected?.reset.map { $0 > last && $0 <= Date() } ?? false
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let other = NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "app.codexbattery.macos")
            .first { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier && !$0.isTerminated }
        if let other {
            DistributedNotificationCenter.default().postNotificationName(reopen, object: nil, userInfo: nil, deliverImmediately: true)
            other.activate(options: [])
            NSApp.terminate(nil)
            return
        }

        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.autosaveName = "CodexBattery"
        item.isVisible = true
        item.button?.imagePosition = .imageOnly
        (item.button?.cell as? NSButtonCell)?.imageScaling = .scaleProportionallyDown
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        installMainMenu()
        observeEnvironment()
        DistributedNotificationCenter.default().addObserver(self, selector: #selector(showDashboard), name: reopen, object: nil)

        petSource.onChange = { [weak self] selection in self?.applyPet(selection) }
        petSource.start()

        client.onActivityChange = { [weak self] in self?.updateDashboard() }
        client.onUpdate = { [weak self] result in
            guard let self, !self.stopping else { return }
            self.snapshot = result
            self.lastUpdate = Date()
            self.usageError = nil
            self.failures = 0
            self.checkLowQuota()
            self.didChange()
            self.scheduleNextRead()
        }
        client.onError = { [weak self] message in
            guard let self, !self.stopping else { return }
            self.usageError = message
            self.failures = min(6, self.failures + 1)
            self.didChange()
            self.scheduleNextRead()
        }
        showDashboard()
        refresh()
        scheduleBlink()
    }

    private func applyPet(_ selection: SelectedPet) {
        guard !stopping else { return }
        let revision = UUID()
        petRevision = revision
        guard let data = selection.artwork else {
            petStatus = selection.statusText
            petName = nil
            PetArt.shared.clear()
            setIconPose(nil, artworkID: nil)
            scheduleBlink()
            updateDashboard()
            return
        }
        petDecoder.async { [weak self] in
            let frames = PetArt.decode(data, columns: selection.columns, rows: selection.rows)
            let artworkID = frames == nil ? nil : Data(SHA256.hash(data: data)).base64EncodedString()
            DispatchQueue.main.async { [weak self] in
                guard let self, !self.stopping, self.petRevision == revision else { return }
                if let frames {
                    PetArt.shared.install(frames)
                    self.petName = selection.name ?? "Pet"
                    self.petStatus = selection.statusText
                } else {
                    PetArt.shared.clear()
                    self.petName = nil
                    self.petStatus = { tr(.badPet) }
                }
                self.setIconPose(frames?[0], artworkID: artworkID)
                self.scheduleBlink()
                self.updateDashboard()
            }
        }
    }

    private func setIconPose(_ pose: CGImage?, artworkID: String?) {
        iconPose = pose
        iconArtworkID = artworkID
        updateApplicationIcon()
    }

    private func updateApplicationIcon(retry: Bool = false) {
        applicationIcon.update(pose: showPet ? iconPose : nil,
                               artworkID: showPet ? iconArtworkID : nil, retry: retry)
    }

    private func observe(_ center: NotificationCenter, _ name: Notification.Name, _ action: @escaping (AppDelegate) -> Void) {
        let token = center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
            if let self { action(self) }
        }
        observers.append((center, token))
    }

    private func observeEnvironment() {
        let ws = NSWorkspace.shared.notificationCenter
        observe(ws, NSWorkspace.willSleepNotification) { app in
            app.sleeping = true
            app.nextRead?.invalidate()
            app.client.stop()
            app.petSource.stop()
            app.scheduleBlink()
        }
        observe(ws, NSWorkspace.didWakeNotification) { app in
            app.sleeping = false
            if !app.screenAsleep { app.petSource.start() }
            app.scheduleBlink()
            app.refresh()
        }
        observe(ws, NSWorkspace.screensDidSleepNotification) { app in
            app.screenAsleep = true
            app.petSource.stop()
            app.scheduleBlink()
        }
        observe(ws, NSWorkspace.screensDidWakeNotification) { app in
            app.screenAsleep = false
            if !app.sleeping { app.petSource.start() }
            app.scheduleBlink()
        }
        observe(ws, NSWorkspace.accessibilityDisplayOptionsDidChangeNotification) { $0.scheduleBlink() }
        observe(.default, .NSProcessInfoPowerStateDidChange) { $0.scheduleBlink() }
        observe(.default, NSLocale.currentLocaleDidChangeNotification) { app in
            if InterfaceLanguage.preference == .system { app.refreshLanguage() }
        }
        observe(.default, NSApplication.didChangeScreenParametersNotification) { $0.didChange() }
        appearance = item.button?.observe(\.effectiveAppearance, options: [.new]) { [weak self] _, _ in
            DispatchQueue.main.async { self?.renderStatus() }
        }
    }

    private func timer(in seconds: TimeInterval, tolerance: TimeInterval, _ action: @escaping () -> Void) -> Timer {
        let timer = Timer(timeInterval: seconds, repeats: false) { _ in action() }
        timer.tolerance = tolerance
        RunLoop.main.add(timer, forMode: .common)
        return timer
    }
    private func scheduleNextRead() {
        nextRead?.invalidate()
        guard !sleeping, !stopping else { return }
        var delay = failures == 0 ? interval : min(300, interval * pow(2, Double(failures - 1)))
        if failures == 0, let reset = windows.compactMap(\.reset).filter({ $0 > Date() }).min() {
            delay = min(delay, max(1, reset.timeIntervalSinceNow + 0.5))
        }
        nextRead = timer(in: delay, tolerance: min(2, delay / 10)) { [weak self] in self?.refresh() }
    }
    @objc private func refresh() {
        guard !sleeping, !stopping, !client.isRefreshing else { return }
        nextRead?.invalidate()
        didChange()
        client.refresh()
    }

    private func scheduleBlink() {
        blink?.invalidate()
        frame = 0
        renderStatus()
        guard !stopping, !sleeping, !screenAsleep, !tracking, showPet, petName != nil, !compact, motion,
              !ProcessInfo.processInfo.isLowPowerModeEnabled, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        blink = timer(in: 7.5, tolerance: 1) { [weak self] in
            guard let self else { return }
            self.frame = 1
            self.renderStatus()
            self.blink = self.timer(in: 0.16, tolerance: 0.02) { [weak self] in self?.scheduleBlink() }
        }
    }
    private func checkLowQuota() {
        guard defaults.bool(forKey: "lowSound") else { return }
        var play = false
        for (key, quota) in [("primary", snapshot?.primary), ("secondary", snapshot?.secondary)] {
            guard let quota else { continue }
            let pref = "lastLowAlert." + key
            guard quota.remaining <= 20 else { defaults.removeObject(forKey: pref); continue }
            let period = quota.reset.map { String($0.timeIntervalSince1970) } ?? "unknown"
            if defaults.string(forKey: pref) != period {
                defaults.set(period, forKey: pref)
                play = true
            }
        }
        if play { NSSound(named: "Glass")?.play() }
    }

    private func didChange() {
        renderStatus()
        updateDashboard()
    }
    private func renderStatus() {
        guard !stopping, let button = item?.button else { return }
        let dark = button.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
        let availableHeight = button.bounds.height > 0 ? button.bounds.height : NSStatusBar.system.thickness
        let image = statusArt(selected?.remaining, stale: stale, style: colorStyle, dark: dark,
                              pet: showPet && !compact && petName != nil, frame: frame,
                              size: menuSize, maxHeight: max(1, availableHeight - 2))
        if button.image !== image { button.image = image }
        let detail: String
        if let errorMessage { detail = errorMessage }
        else if stale { detail = lastUpdate == nil ? tr(.readingQuota) : tr(.awaitingQuota) }
        else if let selected { detail = tr(.quotaDetail, selected.label, selected.remaining) }
        else { detail = tr(.emptyPeriod) }
        let tip = ["Codex · " + detail, showPet && !compact ? petName : nil].compactMap { $0 }.joined(separator: " · ")
        if button.toolTip != tip { button.toolTip = tip; button.setAccessibilityLabel(tip) }
    }
    private func updateDashboard() {
        guard !stopping, let panel, panel.window.isVisible else { return }
        panel.update(quotas: snapshot, lastUpdate: lastUpdate, error: errorMessage, stale: stale,
                     refreshing: client.isRefreshing, mode: mode, colorStyle: colorStyle,
                     petImage: showPet && petName != nil ? PetArt.shared.frame(0, tired: !stale && (selected?.remaining ?? 100) <= 20) : nil,
                     petSize: CGFloat(portraitSize))
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if item != nil { showDashboard() }
        return true
    }
    @objc private func showDashboard() {
        if panel == nil {
            let panel = DashboardController()
            self.panel = panel
            panel.window.delegate = self
            panel.onRefresh = { [weak self] in self?.refresh() }
            panel.onMode = { [weak self] mode in self?.setMode(mode) }
            panel.onSettings = { [weak self] anchor in
                guard let self else { return }
                let menu = self.settingsMenu()
                menu.popUp(positioning: nil, at: NSPoint(x: 0, y: anchor.bounds.minY - 3), in: anchor)
            }
            panel.onHide = { [weak self] in self?.panel?.window.close() }
            panel.onQuit = { NSApp.terminate(nil) }
        }
        NSApp.setActivationPolicy(.regular)
        guard let window = panel?.window else { return }
        if window.isMiniaturized { window.deminiaturize(nil) }
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        updateDashboard()
    }
    func windowWillClose(_ notification: Notification) { NSApp.setActivationPolicy(.accessory) }

    private func entry(_ title: String, _ action: Selector? = nil, key: String = "") -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = action == nil ? nil : self
        return item
    }
    private func submenu(_ title: String, _ menu: NSMenu) -> NSMenuItem {
        let parent = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        parent.submenu = menu
        return parent
    }
    private func choice(_ title: String, _ key: String, _ value: String, checked: Bool) -> NSMenuItem {
        let item = entry(title, #selector(configure(_:)))
        item.representedObject = [key, value]
        item.state = checked ? .on : .off
        return item
    }
    private func modeMenu() -> NSMenu {
        let menu = NSMenu()
        let choices: [(String, String)]
        if windows.count == 1, let only = windows.first {
            menu.addItem(entry(tr(.only, only.label)))
            return menu
        }
        choices = [(snapshot?.primary?.label ?? tr(.firstPeriod), "primary"),
                   (snapshot?.secondary?.label ?? tr(.secondPeriod), "secondary"), (tr(.lowestPeriod), "minimum")]
        for (label, value) in choices {
            menu.addItem(choice(label, "window", value, checked: mode == value))
        }
        return menu
    }
    private func settingsMenu() -> NSMenu {
        let menu = NSMenu()
        let languages = NSMenu()
        for language in InterfaceLanguage.allCases {
            languages.addItem(choice(language.menuTitle, "interfaceLanguage", language.rawValue,
                                     checked: InterfaceLanguage.preference == language))
        }
        menu.addItem(submenu(tr(.language), languages))
        menu.addItem(submenu(tr(.period), modeMenu()))
        let colors = NSMenu()
        for (name, value) in [(tr(.automaticColor), "auto"), (tr(.teal), "teal"), (tr(.monochrome), "mono")] {
            colors.addItem(choice(name, "colorStyle", value, checked: colorStyle == value))
        }
        menu.addItem(submenu(tr(.color), colors))
        let sizes = NSMenu()
        for (label, size) in [(tr(.small), MenuArtSize.small), (tr(.regular), .regular), (tr(.large), .large)] {
            sizes.addItem(choice(label, "menuArtSize", size.rawValue, checked: menuSize == size))
        }
        menu.addItem(submenu(tr(.iconSize), sizes))
        menu.addItem(choice(tr(.compact), "compactMenuBar", String(!compact), checked: compact))
        let pets = NSMenu()
        pets.addItem(entry(petStatus()))
        if applicationIcon.finderNeedsRetry { pets.addItem(entry(tr(.finderPending))) }
        pets.addItem(entry(tr(.sync), #selector(syncPet)))
        pets.addItem(.separator())
        pets.addItem(choice(tr(.showPet), "showPet", String(!showPet), checked: showPet))
        pets.addItem(choice(tr(.blink), "animatePet", String(!motion), checked: motion))
        menu.addItem(submenu("ChatGPT Pet", pets))
        let portraits = NSMenu()
        for (label, points) in zip([tr(.small), tr(.regular), tr(.large)], Self.portraitSizes) {
            portraits.addItem(choice(tr(.points, label, points), "portraitSize", String(points), checked: portraitSize == points))
        }
        menu.addItem(submenu(tr(.portraitSize), portraits))
        menu.addItem(.separator())
        let cadence = NSMenu()
        for seconds in [30, 60, 120] {
            cadence.addItem(choice(tr(.seconds, seconds), "refreshSeconds", String(seconds), checked: Int(interval) == seconds))
        }
        menu.addItem(submenu(tr(.cadence), cadence))
        let sound = defaults.bool(forKey: "lowSound")
        menu.addItem(choice(tr(.lowAlert), "lowSound", String(!sound), checked: sound))
        return menu
    }
    private func setMode(_ mode: String) {
        guard ["primary", "secondary", "minimum"].contains(mode) else { return }
        defaults.set(mode, forKey: "window")
        didChange()
    }
    @objc private func syncPet() {
        petSource.refresh()
        updateApplicationIcon(retry: true)
    }
    @objc private func configure(_ sender: NSMenuItem) {
        guard let pair = sender.representedObject as? [String], pair.count == 2 else { return }
        let key = pair[0], value = pair[1]
        switch key {
        case "interfaceLanguage":
            guard InterfaceLanguage(rawValue: value) != nil else { return }
            defaults.set(value, forKey: key)
            refreshLanguage()
        case "window": setMode(value)
        case "colorStyle": defaults.set(value, forKey: key)
        case "menuArtSize":
            guard MenuArtSize(rawValue: value) != nil else { return }
            defaults.set(value, forKey: key)
        case "portraitSize":
            guard let points = Int(value), Self.portraitSizes.contains(points) else { return }
            defaults.set(points, forKey: key)
        case "refreshSeconds":
            defaults.set(Int(value) ?? 60, forKey: key)
            if !client.isRefreshing { scheduleNextRead() }
        case "compactMenuBar", "showPet", "animatePet", "lowSound":
            defaults.set(value == "true", forKey: key)
            if key == "showPet" { updateApplicationIcon() }
            scheduleBlink()
        default: return
        }
        didChange()
    }
    private func refreshLanguage() {
        installMainMenu()
        panel?.refreshLanguage()
        didChange()
    }

    private func installMainMenu() {
        let bar = NSMenu()
        let app = NSMenu(title: "Codex Battery")
        app.addItem(entry(tr(.openWindow), #selector(showDashboard), key: "1"))
        app.addItem(entry(tr(.refreshQuota), #selector(refresh), key: "r"))
        let close = NSMenuItem(title: tr(.hide), action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        app.addItem(close)
        app.addItem(.separator())
        app.addItem(entry(tr(.quitApp), #selector(quit), key: "q"))
        bar.addItem(submenu("Codex Battery", app))
        NSApp.mainMenu = bar
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let header = entry(stale && lastUpdate != nil ? tr(.lastQuota) : tr(.title))
        menu.addItem(header)
        for q in windows {
            let row = entry("\(q.label)    \(q.remaining)%")
            if let reset = q.reset { row.toolTip = tr(.resetTime, InterfaceLanguage.date(reset, template: "yMdjm")) }
            menu.addItem(row)
        }
        if windows.isEmpty { menu.addItem(entry(client.isRefreshing ? tr(.reading) : tr(.empty))) }
        if errorMessage != nil { menu.addItem(entry(tr(.connectionIssue))) }
        menu.addItem(.separator())
        menu.addItem(entry(tr(.openWindowDots), #selector(showDashboard)))
        menu.addItem(entry(tr(.refreshNow), #selector(refresh)))
        menu.addItem(submenu(tr(.settings), settingsMenu()))
        menu.addItem(.separator())
        menu.addItem(entry(tr(.quit), #selector(quit)))
    }
    func menuWillOpen(_ menu: NSMenu) { tracking = true; scheduleBlink() }
    func menuDidClose(_ menu: NSMenu) { tracking = false; scheduleBlink() }
    @objc private func quit() { NSApp.terminate(nil) }
    func applicationWillTerminate(_ notification: Notification) {
        stopping = true
        nextRead?.invalidate(); blink?.invalidate()
        appearance?.invalidate()
        petSource.stop()
        applicationIcon.stop()
        for (center, token) in observers { center.removeObserver(token) }
        DistributedNotificationCenter.default().removeObserver(self)
        client.stop()
    }
}
