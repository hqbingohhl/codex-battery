import Foundation
import CoreFoundation

private func number(_ value: Any?) -> NSNumber? {
    guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
          n.doubleValue.isFinite else { return nil }
    return n
}

struct WindowQuota: Equatable {
    let used: Double
    let minutes: Int?
    let reset: Date?

    var remaining: Int { Int((100 - used).rounded(.down)) }
    init?(_ object: Any?) {
        guard let fields = object as? [String: Any], let percent = number(fields["usedPercent"]) else { return nil }
        used = min(100, max(0, percent.doubleValue))
        minutes = number(fields["windowDurationMins"]).flatMap { $0.intValue > 0 ? $0.intValue : nil }
        reset = number(fields["resetsAt"]).flatMap {
            $0.doubleValue > 0 ? Date(timeIntervalSince1970: $0.doubleValue) : nil
        }
    }
    var label: String {
        guard let m = minutes else { return tr(.unknownPeriod) }
        if m == 10080 { return tr(.weekly) }
        if m % 60 == 0 { return tr(.hourQuota, m / 60) }
        return tr(.minuteQuota, m)
    }
}

struct Quotas {
    let primary: WindowQuota?
    let secondary: WindowQuota?

    init(_ object: [String: Any]) {
        let fields: [String: Any]?
        if let map = object["rateLimitsByLimitId"] as? [String: Any] {
            fields = map["codex"] as? [String: Any]
        } else if let legacy = object["rateLimits"] as? [String: Any], Self.isCodex(legacy) {
            fields = legacy
        } else {
            fields = nil
        }
        primary = WindowQuota(fields?["primary"])
        secondary = WindowQuota(fields?["secondary"])
    }

    static func isCodex(_ fields: [String: Any]) -> Bool {
        // Older single-bucket servers omit the identifier.
        (fields["limitId"] as? String ?? "codex") == "codex"
    }

    // Sparse notifications may omit either window (or just its reset metadata).
    // Only a complete read is published to the UI, so both values share a freshness date.
    func isChanged(by fields: [String: Any]) -> Bool {
        guard Self.isCodex(fields) else { return false }
        for (key, old) in [("primary", primary), ("secondary", secondary)] {
            guard let new = WindowQuota(fields[key]) else { continue }
            guard let old = old else { return true }
            if new.used != old.used || (new.reset != nil && new.reset != old.reset) ||
                (new.minutes != nil && new.minutes != old.minutes) { return true }
        }
        return false
    }
}

final class UsageClient {
    var onUpdate: ((Quotas) -> Void)?
    var onError: ((InterfaceMessage) -> Void)?
    var onActivityChange: (() -> Void)?
    private(set) var isRefreshing = false {
        didSet { if oldValue != isRefreshing { onActivityChange?() } }
    }

    private var process: Process?
    private var input: FileHandle?
    private var output: FileHandle?
    private var buffer = Data()
    private var ready = false
    private var nextID = 2
    private var expectedID: Int?
    private var deadline: DispatchWorkItem?
    private var scheduledRead: DispatchWorkItem?
    private var generation = UUID()
    private var snapshot: Quotas?
    private var lastStarted: TimeInterval = 0
    private var useReadParams = true
    private var pendingNotice: [String: Any]?
    private let binaryOverride: URL?

    init(binary: URL? = nil) { binaryOverride = binary }

    private static func findCLI() -> URL? {
        let folders = ["/Applications", NSHomeDirectory() + "/Applications"]
        var candidates = folders.flatMap { dir in
            ["ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex",
             "Codex.app/Contents/Resources/codex",
             "ChatGPT.app/Contents/Resources/codex-cli/bin/codex"].map { dir + "/" + $0 }
        }
        candidates += ["/opt/homebrew/bin", "/usr/local/bin", NSHomeDirectory() + "/.npm-global/bin"].map { $0 + "/codex" }
        for part in (ProcessInfo.processInfo.environment["PATH"] ?? "").split(separator: ":") where part.first == "/" {
            candidates.append(String(part) + "/codex")
        }
        return candidates.first(where: FileManager.default.isExecutableFile(atPath:)).map(URL.init(fileURLWithPath:))
    }

    func refresh() {
        guard !isRefreshing else { return }
        scheduledRead?.cancel()
        scheduledRead = nil
        if process?.isRunning == true, ready {
            request()
            return
        }
        stop()
        guard let binary = binaryOverride ?? Self.findCLI() else {
            onError?(InterfaceMessage(.missingCLI))
            return
        }

        let p = Process(), stdin = Pipe(), stdout = Pipe()
        p.executableURL = binary
        p.arguments = ["app-server"]
        p.currentDirectoryURL = FileManager.default.homeDirectoryForCurrentUser
        p.standardInput = stdin
        p.standardOutput = stdout
        p.standardError = FileHandle.nullDevice
        process = p
        input = stdin.fileHandleForWriting
        output = stdout.fileHandleForReading
        // A CLI update or crash can close stdin before our next write.
        _ = fcntl(stdin.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1)
        let token = generation
        output?.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            DispatchQueue.main.async {
                guard let self = self, self.generation == token else { return }
                if data.isEmpty {
                    self.fail(InterfaceMessage(.closed))
                    return
                }
                self.buffer.append(data)
                while let newline = self.buffer.firstIndex(of: 10) {
                    let line = self.buffer.prefix(upTo: newline)
                    self.buffer.removeSubrange(...newline)
                    self.receive(Data(line))
                }
                if self.buffer.count > 4 * 1024 * 1024 { self.fail(InterfaceMessage(.invalidData)) }
            }
        }
        do {
            try p.run()
            beginRequest(id: 1)
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1"
            send(["id": 1, "method": "initialize", "params": ["clientInfo": ["name": "codex_battery", "title": "Codex Battery", "version": version]]])
        } catch { fail(InterfaceMessage(.launchError, error.localizedDescription)) }
    }

    @discardableResult private func send(_ object: [String: Any]) -> Bool {
        do {
            guard let input = input else { return false }
            var data = try JSONSerialization.data(withJSONObject: object)
            data.append(10)
            try input.write(contentsOf: data)
            return true
        } catch {
            fail(InterfaceMessage(.cannotConnect))
            return false
        }
    }

    private func beginRequest(id: Int) {
        deadline?.cancel()
        expectedID = id
        isRefreshing = true
        let token = generation
        let timeout = DispatchWorkItem { [weak self] in
            guard let self = self, self.generation == token, self.expectedID == id else { return }
            self.fail(InterfaceMessage(.timedOut))
        }
        deadline = timeout
        DispatchQueue.main.asyncAfter(deadline: .now() + 25, execute: timeout)
    }

    private func request() {
        let id = nextID
        nextID += 1
        beginRequest(id: id)
        lastStarted = ProcessInfo.processInfo.systemUptime
        var rpc: [String: Any] = ["id": id, "method": "account/rateLimits/read"]
        if useReadParams { rpc["params"] = ["excludeResetCreditDetails": true] }
        send(rpc)
    }

    private func receive(_ line: Data) {
        guard let rpc = (try? JSONSerialization.jsonObject(with: line)) as? [String: Any] else { return }
        if let method = rpc["method"] as? String {
            if method == "account/rateLimits/updated", let params = rpc["params"] as? [String: Any],
               let fields = params["rateLimits"] as? [String: Any], Quotas.isCodex(fields) {
                if isRefreshing { pendingNotice = fields }
                else if snapshot?.isChanged(by: fields) ?? true { scheduleRead() }
            }
            return
        }
        guard let id = rpc["id"] as? Int, id == expectedID else { return }
        deadline?.cancel()
        if let error = rpc["error"] as? [String: Any] {
            if id != 1, useReadParams, error["code"] as? Int == -32602 {
                // The initial read-param-less API is still used by older CLI releases.
                useReadParams = false
                request()
            } else {
                let hint = (error["message"] as? String ?? "").lowercased()
                fail(hint.contains("auth") || hint.contains("401") || hint.contains("login")
                     ? InterfaceMessage(.login)
                     : InterfaceMessage(.unavailable))
            }
            return
        }
        guard let result = rpc["result"] as? [String: Any] else {
            fail(InterfaceMessage(.invalidData))
            return
        }
        if id == 1 {
            ready = true
            guard send(["method": "initialized", "params": [:]]) else { return }
            request()
            return
        }
        guard result["rateLimits"] is [String: Any] || result["rateLimitsByLimitId"] is [String: Any] else {
            fail(InterfaceMessage(.missingQuota))
            return
        }
        let data = Quotas(result)
        snapshot = data
        expectedID = nil
        isRefreshing = false
        onUpdate?(data)
        if let notice = pendingNotice, data.isChanged(by: notice) { scheduleRead() }
        pendingNotice = nil
    }

    private func scheduleRead() {
        guard scheduledRead == nil, ready else { return }
        let token = generation
        let task = DispatchWorkItem { [weak self] in
            guard let self = self, self.generation == token else { return }
            self.scheduledRead = nil
            self.refresh()
        }
        scheduledRead = task
        let wait = max(0.5, 5 - (ProcessInfo.processInfo.systemUptime - lastStarted))
        DispatchQueue.main.asyncAfter(deadline: .now() + wait, execute: task)
    }

    private func fail(_ message: InterfaceMessage) {
        stop()
        onError?(message)
    }
    func stop() {
        generation = UUID()
        deadline?.cancel(); deadline = nil
        scheduledRead?.cancel(); scheduledRead = nil
        output?.readabilityHandler = nil
        try? input?.close()
        if process?.isRunning == true { process?.terminate() }
        process = nil
        input = nil
        output = nil
        buffer.removeAll()
        snapshot = nil
        pendingNotice = nil
        expectedID = nil
        ready = false
        isRefreshing = false
        useReadParams = true
    }
    deinit { stop() }
}
