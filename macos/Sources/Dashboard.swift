import Cocoa

private func panelLabel(_ text: String, size: CGFloat, weight: NSFont.Weight = .regular,
                        color: NSColor = .labelColor) -> NSTextField {
    let label = NSTextField(labelWithString: text)
    label.font = .systemFont(ofSize: size, weight: weight)
    label.textColor = color
    label.lineBreakMode = .byTruncatingTail
    return label
}

private func panelStack(_ orientation: NSUserInterfaceLayoutOrientation, _ views: [NSView],
                        spacing: CGFloat, alignment: NSLayoutConstraint.Attribute) -> NSStackView {
    let stack = NSStackView(views: views)
    stack.orientation = orientation
    stack.alignment = alignment
    stack.distribution = .fill
    stack.spacing = spacing
    stack.detachesHiddenViews = true
    return stack
}

private func panelSpacer() -> NSView {
    let spacer = NSView()
    spacer.setContentHuggingPriority(.init(1), for: .horizontal)
    return spacer
}

private final class PanelDocument: NSView {
    override var isFlipped: Bool { true }
}

// Keep the bar visible on both light and dark vibrancy backgrounds. Text carries the
// percentage independently of its color. The text label also serves VoiceOver.
private final class CapacityBar: NSView {
    var fraction: CGFloat = 0
    var tint: NSColor = .systemGreen

    override func draw(_ dirtyRect: NSRect) {
        guard !bounds.isEmpty else { return }
        let radius = bounds.height / 2
        let path = NSBezierPath(roundedRect: bounds, xRadius: radius, yRadius: radius)
        NSColor.labelColor.withAlphaComponent(0.1).setFill()
        path.fill()
        guard fraction > 0 else { return }
        NSGraphicsContext.saveGraphicsState()
        path.addClip()
        tint.setFill()
        NSRect(x: bounds.minX, y: bounds.minY, width: bounds.width * fraction, height: bounds.height).fill()
        NSGraphicsContext.restoreGraphicsState()
    }
}

private final class QuotaCard: NSView {
    private let heading = panelLabel("", size: 13, weight: .semibold)
    private let value = panelLabel("—", size: 36, weight: .medium)
    private let unit = panelLabel(tr(.remaining), size: 13, color: .secondaryLabelColor)
    private let bar = CapacityBar()
    private let reset = NSTextField(wrappingLabelWithString: "")
    private let fallback: CopyKey

    init(_ name: CopyKey) {
        fallback = name
        super.init(frame: .zero)
        value.font = .monospacedDigitSystemFont(ofSize: 37, weight: .medium)
        reset.font = .systemFont(ofSize: 12)
        reset.textColor = .secondaryLabelColor
        unit.setAccessibilityElement(false)
        bar.setAccessibilityElement(false)
        bar.heightAnchor.constraint(equalToConstant: 6).isActive = true
        let amount = panelStack(.horizontal, [value, unit, panelSpacer()], spacing: 7, alignment: .centerY)
        let labels = panelStack(.vertical, [heading, amount, bar, reset], spacing: 10, alignment: .leading)
        labels.translatesAutoresizingMaskIntoConstraints = false
        addSubview(labels)
        NSLayoutConstraint.activate([
            labels.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 16),
            labels.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -16),
            labels.topAnchor.constraint(equalTo: topAnchor, constant: 15),
            labels.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -15),
            amount.widthAnchor.constraint(equalTo: labels.widthAnchor),
            bar.widthAnchor.constraint(equalTo: labels.widthAnchor),
            reset.widthAnchor.constraint(equalTo: labels.widthAnchor)
        ])
    }

    required init?(coder: NSCoder) { nil }

    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 12, yRadius: 12)
        NSColor.controlBackgroundColor.setFill()
        path.fill()
        NSColor.separatorColor.setStroke()
        path.lineWidth = 1
        path.stroke()
    }

    func update(_ quota: WindowQuota?, stale: Bool, style: String) {
        heading.stringValue = quota?.label ?? tr(fallback)
        value.stringValue = quota.map { "\($0.remaining)%" } ?? "—"
        unit.stringValue = quota == nil ? "" : stale ? tr(.lastRemaining) : tr(.remaining)
        value.textColor = stale || quota == nil ? .secondaryLabelColor : .labelColor
        value.setAccessibilityLabel("\(heading.stringValue), " + (quota.map { "\(unit.stringValue) \($0.remaining)%" } ?? tr(.noData)))
        bar.fraction = CGFloat(quota?.remaining ?? 0) / 100
        if stale || style == "mono" {
            bar.tint = .secondaryLabelColor
        } else if style == "teal" {
            bar.tint = .systemTeal
        } else {
            bar.tint = quotaColor(quota?.remaining)
        }
        bar.needsDisplay = true
        if let when = quota?.reset {
            let minutes = when.timeIntervalSinceNow / 60
            let until: String
            switch minutes {
            case ...0: until = tr(.resetDue)
            case ..<1: until = tr(.lessMinute)
            case ..<60: until = tr(.minutesUntil, Int(ceil(minutes)))
            case ..<1440: until = tr(.hoursUntil, Int(minutes) / 60, Int(minutes) % 60)
            case ..<525600: until = tr(.daysUntil, Int(minutes) / 1440, Int(minutes) / 60 % 24)
            default: until = ""
            }
            reset.stringValue = tr(.resets, InterfaceLanguage.date(when, template: "MMMdjm"))
            if !until.isEmpty { reset.stringValue += " · " + until }
        } else {
            reset.stringValue = quota == nil ? tr(.noWindow) : tr(.resetUnknown)
        }
        reset.toolTip = reset.stringValue
    }
}

final class DashboardController: NSObject {
    let window: NSWindow
    var onRefresh: (() -> Void)?
    var onMode: ((String) -> Void)?
    var onSettings: ((NSView) -> Void)?
    var onHide: (() -> Void)?
    var onQuit: (() -> Void)?

    private let title = panelLabel("", size: 21, weight: .semibold)
    private let modeLabel = panelLabel("", size: 13, weight: .medium)
    private let settings = NSButton(title: "", target: nil, action: nil)
    private let hide = NSButton(title: "", target: nil, action: nil)
    private let quit = NSButton(title: "", target: nil, action: nil)
    private let portrait = NSImageView()
    private let status = panelLabel(tr(.readingQuotaDots), size: 12, color: .secondaryLabelColor)
    private let errorText = NSTextField(wrappingLabelWithString: "")
    private let refresh = NSButton(title: tr(.refresh), target: nil, action: nil)
    private let primary = QuotaCard(.availableQuota)
    private let secondary = QuotaCard(.availableQuota)
    private let onlyWindow = panelLabel("", size: 13, color: .secondaryLabelColor)
    private let modeSelector = NSSegmentedControl(labels: [tr(.firstPeriod), tr(.secondPeriod), tr(.lowest)],
                                                 trackingMode: .selectOne, target: nil, action: nil)
    private let cards = NSStackView()
    private let controls = NSStackView()
    private let controlsSpacer = panelSpacer()
    private var stackedCardWidths: [NSLayoutConstraint] = []
    private var portraitSides: [NSLayoutConstraint] = []
    private var delayedFit: DispatchWorkItem?
    private let modes = ["primary", "secondary", "minimum"]

    override init() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 395),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
        super.init()
        window.title = "Codex Battery"
        window.isReleasedWhenClosed = false
        window.contentMinSize = NSSize(width: 360, height: 320)
        buildContent()
        window.center()
        window.setFrameAutosaveName("CodexBatteryDashboardV2")
        fitOnScreen()
        adaptLayout()
        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(adaptLayout), name: NSWindow.didResizeNotification, object: window)
        center.addObserver(self, selector: #selector(deferScreenFit), name: NSWindow.didMoveNotification, object: window)
        center.addObserver(self, selector: #selector(fitOnScreen), name: NSApplication.didChangeScreenParametersNotification, object: nil)
    }

    @objc private func deferScreenFit() {
        delayedFit?.cancel()
        let work = DispatchWorkItem { [weak self] in
            if NSEvent.pressedMouseButtons == 0 { self?.fitOnScreen() }
            else { self?.deferScreenFit() }
        }
        delayedFit = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25, execute: work)
    }

    @objc private func fitOnScreen() {
        let frame = window.frame
        let overlaps = NSScreen.screens.map { screen -> (NSScreen, CGFloat) in
            let overlap = frame.intersection(screen.visibleFrame)
            return (screen, overlap.isNull ? 0 : overlap.width * overlap.height)
        }
        let visible = overlaps.filter { $0.1 > 0 }.max { $0.1 < $1.1 }?.0 ?? NSScreen.main
        guard let bounds = visible?.visibleFrame, !bounds.isEmpty else { return }
        var fitted = frame
        fitted.size.width = min(frame.width, bounds.width)
        fitted.size.height = min(frame.height, bounds.height)
        fitted.origin.x = min(max(frame.minX, bounds.minX), bounds.maxX - fitted.width)
        fitted.origin.y = min(max(frame.minY, bounds.minY), bounds.maxY - fitted.height)
        if fitted != frame { window.setFrame(fitted, display: window.isVisible) }
    }

    @objc private func adaptLayout() {
        let narrow = (window.contentView?.bounds.width ?? 560) < 548
        let orientation: NSUserInterfaceLayoutOrientation = narrow ? .vertical : .horizontal
        guard cards.orientation != orientation else { return }
        if !narrow { NSLayoutConstraint.deactivate(stackedCardWidths) }
        cards.orientation = orientation
        cards.alignment = narrow ? .leading : .top
        cards.distribution = narrow ? .fill : .fillEqually
        if narrow { NSLayoutConstraint.activate(stackedCardWidths) }
        controls.orientation = orientation
        controls.alignment = narrow ? .leading : .centerY
        controlsSpacer.isHidden = narrow
        controls.spacing = narrow ? 5 : 8
    }

    private func buildContent() {
        let root = NSView()
        window.contentView = root
        let scroll = NSScrollView()
        scroll.drawsBackground = false
        scroll.borderType = .noBorder
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(scroll)
        let document = PanelDocument()
        document.translatesAutoresizingMaskIntoConstraints = false
        scroll.documentView = document
        let clip = scroll.contentView
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: root.topAnchor),
            document.leadingAnchor.constraint(equalTo: clip.leadingAnchor),
            document.topAnchor.constraint(equalTo: clip.topAnchor),
            document.widthAnchor.constraint(equalTo: clip.widthAnchor)
        ])
        portrait.imageScaling = .scaleProportionallyUpOrDown
        portrait.setAccessibilityElement(false)
        portrait.translatesAutoresizingMaskIntoConstraints = false
        portraitSides = [
            portrait.widthAnchor.constraint(equalToConstant: 44),
            portrait.heightAnchor.constraint(equalToConstant: 44)
        ]
        NSLayoutConstraint.activate(portraitSides)

        refreshLanguage()
        let heading = panelStack(.vertical, [title, status], spacing: 4, alignment: .leading)
        status.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        refresh.image = NSImage(systemSymbolName: "arrow.clockwise", accessibilityDescription: nil)
        refresh.imagePosition = .imageLeading
        refresh.bezelStyle = .rounded
        refresh.target = self
        refresh.action = #selector(handleRefresh)
        refresh.keyEquivalent = "r"
        refresh.keyEquivalentModifierMask = .command
        let header = panelStack(.horizontal, [portrait, heading, refresh],
                                spacing: 10, alignment: .centerY)
        title.widthAnchor.constraint(equalTo: heading.widthAnchor).isActive = true
        status.widthAnchor.constraint(equalTo: heading.widthAnchor).isActive = true
        heading.setContentHuggingPriority(.defaultLow, for: .horizontal)
        refresh.setContentHuggingPriority(.required, for: .horizontal)

        cards.orientation = .horizontal
        cards.spacing = 12
        cards.alignment = .top
        cards.distribution = .fillEqually
        cards.detachesHiddenViews = true
        for card in [primary, secondary] {
            cards.addArrangedSubview(card)
            stackedCardWidths.append(card.widthAnchor.constraint(equalTo: cards.widthAnchor))
        }

        errorText.font = .systemFont(ofSize: 12)
        errorText.textColor = .secondaryLabelColor
        errorText.isSelectable = true
        modeSelector.target = self
        modeSelector.action = #selector(handleMode)
        controls.orientation = .horizontal
        controls.alignment = .centerY
        controls.spacing = 8
        controls.detachesHiddenViews = true
        [modeLabel, controlsSpacer, onlyWindow, modeSelector].forEach { controls.addArrangedSubview($0) }

        settings.target = self
        settings.action = #selector(handleSettings(_:))
        settings.image = NSImage(systemSymbolName: "gearshape", accessibilityDescription: nil)
        settings.imagePosition = .imageLeading
        settings.bezelStyle = .rounded
        hide.target = self
        hide.action = #selector(handleHide)
        hide.bezelStyle = .rounded
        quit.target = self
        quit.action = #selector(handleQuit)
        quit.bezelStyle = .rounded
        quit.keyEquivalent = "q"
        quit.keyEquivalentModifierMask = .command
        let footer = panelStack(.horizontal, [settings, panelSpacer(), quit, hide], spacing: 8, alignment: .centerY)
        let divider = NSBox()
        divider.boxType = .separator
        let body = panelStack(.vertical, [header, cards, controls, errorText], spacing: 16, alignment: .leading)
        body.translatesAutoresizingMaskIntoConstraints = false
        document.addSubview(body)
        for view in [header, cards, controls, errorText] {
            view.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
        }
        for view in [divider, footer] {
            view.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(view)
            NSLayoutConstraint.activate([
                view.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 22),
                view.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -22)
            ])
        }
        NSLayoutConstraint.activate([
            body.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 22),
            body.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -22),
            body.topAnchor.constraint(equalTo: document.topAnchor, constant: 18),
            body.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -15),
            scroll.bottomAnchor.constraint(equalTo: divider.topAnchor),
            divider.bottomAnchor.constraint(equalTo: footer.topAnchor, constant: -12),
            footer.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16)
        ])
    }

    func refreshLanguage() {
        title.stringValue = tr(.title)
        modeLabel.stringValue = tr(.menuDisplay)
        settings.title = tr(.settingsDots)
        hide.title = tr(.hide)
        quit.title = tr(.quit)
        modeSelector.setLabel(tr(.lowest), forSegment: 2)
        refresh.setAccessibilityLabel(tr(.refreshAccess))
        modeSelector.setAccessibilityLabel(tr(.modeAccess))
    }

    func update(quotas: Quotas?, lastUpdate: Date?, error: String?, stale: Bool,
                refreshing: Bool, mode: String, colorStyle: String, petImage: NSImage?, petSize: CGFloat = 44) {
        primary.update(quotas?.primary, stale: stale, style: colorStyle)
        secondary.update(quotas?.secondary, stale: stale, style: colorStyle)
        // A primary window can itself be weekly, and either slot can be absent.
        // The empty state uses a single neutral placeholder until a window arrives.
        let first = quotas?.primary
        let second = quotas?.secondary
        primary.isHidden = first == nil && second != nil
        secondary.isHidden = second == nil
        let bothAvailable = first != nil && second != nil
        modeSelector.isHidden = !bothAvailable
        onlyWindow.isHidden = bothAvailable
        onlyWindow.stringValue = (first ?? second)?.label ?? tr(.empty)
        portrait.image = petImage
        portrait.isHidden = petImage == nil
        let side = min(72, max(24, petSize))
        portraitSides.forEach { $0.constant = side }
        modeSelector.selectedSegment = modes.firstIndex(of: mode) ?? 0
        modeSelector.setLabel(first?.label ?? tr(.firstPeriod), forSegment: 0)
        modeSelector.setLabel(second?.label ?? tr(.secondPeriod), forSegment: 1)
        refresh.isEnabled = !refreshing
        refresh.title = refreshing ? tr(.refreshingDots) : tr(.refresh)
        let time = lastUpdate.map { InterfaceLanguage.date($0, template: "jmmss") }
        let last = time.map { tr(.lastUpdate, $0) }
        if refreshing {
            status.stringValue = [tr(.refreshing), last].compactMap { $0 }.joined(separator: " · ")
        } else if error != nil || stale {
            status.stringValue = [error != nil ? tr(.failed) : (last == nil ? tr(.waiting) : tr(.expired)), last]
                .compactMap { $0 }.joined(separator: " · ")
        } else if quotas?.primary == nil && quotas?.secondary == nil {
            status.stringValue = tr(.noAccountQuota)
        } else {
            status.stringValue = time.map { tr(.updateTime, $0) } ?? tr(.updated)
        }
        let issue = error?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        errorText.stringValue = issue
        errorText.toolTip = issue.isEmpty ? nil : issue
        errorText.isHidden = issue.isEmpty
    }

    @objc private func handleRefresh() { onRefresh?() }
    @objc private func handleMode() {
        let i = modeSelector.selectedSegment
        guard modes.indices.contains(i) else { return }
        onMode?(modes[i])
    }
    @objc private func handleSettings(_ button: NSButton) { onSettings?(button) }
    @objc private func handleHide() { onHide?() }
    @objc private func handleQuit() { onQuit?() }
}
