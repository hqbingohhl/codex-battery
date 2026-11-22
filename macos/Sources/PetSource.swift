import AppKit
import CryptoKit
import ImageIO

struct SelectedPet {
    let identifier: String?
    let name: String?
    var displayName: String { name ?? tr(.customPet) }
    let artwork: Data?
    let columns: Int
    let rows: Int
    let pending: CopyKey
    var statusText: () -> String {
        let name = self.name, ready = artwork != nil, pending = self.pending
        return { ready ? tr(.synced, name ?? tr(.customPet)) : tr(pending) }
    }
    var status: String { statusText() }
}

/// All file I/O runs on a serial queue. Most ticks only stat two small sets of metadata.
/// Reopening settings or waking the machine can request an immediate retry.
final class PetSource {
    var onChange: ((SelectedPet) -> Void)?
    private let queue = DispatchQueue(label: "CodexBattery.pet", qos: .utility)
    private let makeReader: () -> DesktopPetReader
    private var reader: DesktopPetReader?
    private var timer: Timer?
    private var busy = false
    private var requested = false

    init(makeReader: @escaping () -> DesktopPetReader = { DesktopPetReader() }) { self.makeReader = makeReader }

    func start() {
        guard reader == nil else { return }
        reader = makeReader()
        refresh()
        let timer = Timer(timeInterval: 6, repeats: true) { [weak self] _ in self?.check(force: false) }
        timer.tolerance = 1
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func refresh() { check(force: true) }

    func stop() {
        timer?.invalidate()
        timer = nil
        reader = nil
        requested = false
        busy = false
    }

    private func check(force: Bool) {
        guard let reader else { return }
        if busy {
            if force { requested = true }
            return
        }
        busy = true
        queue.async {
            let value = reader.read(force: force)
            DispatchQueue.main.async { [weak self] in
                guard let self, self.reader === reader else { return }
                self.busy = false
                if let value { self.onChange?(value) }
                if self.requested {
                    self.requested = false
                    self.refresh()
                }
            }
        }
    }
}

private enum PetImageSource: Equatable {
    case builtIn(String)
    case local(URL)
    case cloud(owner: String, url: String, fingerprint: String)
    case unavailable
}

private struct PetChoice: Equatable {
    var id: String?
    var name: String?
    var source = PetImageSource.unavailable
    var pending: CopyKey = .waitingPet
}

final class DesktopPetReader {
    private let desktop: URL
    private let webCache: URL
    private let ownCache: URL
    private let archive: URL
    private let fm = FileManager.default
    private var settingsStamp: String?
    private var invalidStamp: String?
    private var choice = PetChoice()
    private var renderedChoice: PetChoice?
    private var artworkStamp: String?
    private var delivered = false
    private var retryAt = Date.distantPast
    private static let imageLimit = 32 * 1024 * 1024
    private static let builtinNames = ["codex": "Codex", "dewey": "Dewey", "fireball": "Fireball", "hoots": "Hoots",
                                       "rocky": "Rocky", "seedy": "Seedy", "stacky": "Stacky", "bsod": "BSOD", "null-signal": "Null Signal"]

    init(home: URL = FileManager.default.homeDirectoryForCurrentUser, app: URL? = nil, codexDirectory: URL? = nil, artworkCache: URL? = nil) {
        let environment = ProcessInfo.processInfo.environment["CODEX_HOME"]
        desktop = codexDirectory ?? environment.flatMap { $0.hasPrefix("/") ? URL(fileURLWithPath: $0) : nil }
            ?? home.appendingPathComponent(".codex")
        let cache = home.appendingPathComponent("Library/Caches")
        webCache = cache.appendingPathComponent("Codex/Default/Cache/Cache_Data")
        ownCache = artworkCache ?? cache.appendingPathComponent("app.codexbattery.macos/selected-pet.plist")
        let application = app ?? NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.openai.codex")
            ?? URL(fileURLWithPath: "/Applications/ChatGPT.app")
        archive = application.appendingPathComponent("Contents/Resources/app.asar")
    }

    /// Returns a value only when the observable choice, its artwork, or its availability changes.
    func read(force: Bool = false) -> SelectedPet? {
        let state = desktop.appendingPathComponent(".codex-global-state.json")
        let config = desktop.appendingPathComponent("config.toml")
        var stamp = fileStamp(state) + "/" + fileStamp(config)
        if case .local(let sheet) = choice.source {
            stamp += "/" + fileStamp(sheet.deletingLastPathComponent().appendingPathComponent("pet.json"))
        }
        let waitingForRecord = choice.source == .unavailable && choice.id != nil
        if settingsStamp != stamp || (waitingForRecord && (force || Date() >= retryAt)) {
            // A partially written state file is retried next tick. It should not flash another avatar.
            if fm.fileExists(atPath: state.path) {
                if let raw = limitedData(state, limit: 32 * 1024 * 1024),
                   let json = (try? JSONSerialization.jsonObject(with: raw)) as? [String: Any] {
                    choice = selected(in: json, config: config)
                    invalidStamp = nil
                } else {
                    if invalidStamp != stamp { invalidStamp = stamp; return nil }
                    choice = PetChoice()
                }
            } else {
                choice = legacyChoice(config: config)
            }
            settingsStamp = stamp
            if waitingForRecord { retryAt = Date().addingTimeInterval(24) }
        }

        let revision: String
        switch choice.source {
        case .builtIn: revision = fileStamp(archive)
        case .local(let url): revision = fileStamp(url)
        default: revision = ""
        }
        let changed = choice != renderedChoice || artworkStamp != revision
        guard changed || (!delivered && (force || Date() >= retryAt)) else { return nil }
        let bytes: Data?
        switch choice.source {
        case .builtIn(let id): bytes = builtInArtwork(id)
        case .local(let url): bytes = limitedData(url, limit: Self.imageLimit)
        case .cloud(let owner, let url, let fingerprint):
            bytes = cloudArtwork(owner: owner, id: choice.id ?? "", url: url, fingerprint: fingerprint)
        case .unavailable: bytes = nil
        }
        let rows = bytes.flatMap(spriteRows)
        let ready = rows != nil
        let publish = changed || (!delivered && ready)
        delivered = ready || choice.source == .unavailable
        retryAt = Date().addingTimeInterval(24)
        renderedChoice = choice
        artworkStamp = revision
        guard publish else { return nil }
        return SelectedPet(identifier: choice.id, name: choice.name, artwork: ready ? bytes : nil,
                           columns: 8, rows: rows ?? 9, pending: choice.pending)
    }

    private func selected(in state: [String: Any], config: URL) -> PetChoice {
        guard let owner = state["electron-renderer-content-owner"] as? String,
              let atoms = state["electron-owner-atoms:" + owner] as? [String: Any]
        else {
            // Owner-scoped records belonging to another or signed-out account must not be reused.
            return state.keys.contains(where: { $0.hasPrefix("electron-owner-atoms:") }) ? PetChoice() : legacyChoice(config: config)
        }
        guard let selections = atoms["last-observed-cloud-pet-selection-v1"] as? [String: String], !selections.isEmpty
        else { return legacyChoice(config: config) }
        let cache = atoms["cloud-pet-artwork-cache"] as? [String: Any]
        let accountKey: String?
        if let account = cache?["accountId"] as? String, let user = cache?["userId"] as? String {
            accountKey = selections.keys.first { key in
                let pair = (try? JSONSerialization.jsonObject(with: Data(key.utf8))) as? [String]
                return pair == [account, user]
            }
        } else {
            accountKey = selections.count == 1 ? selections.keys.first : nil
        }
        guard let key = accountKey, let id = selections[key] else { return PetChoice() }
        guard id.hasPrefix("pet_") else { return namedChoice(id: id) }

        let entries = cache?["pets"] as? [[String: Any]] ?? []
        var result = PetChoice(id: id, pending: .waitCloudPet)
        guard let record = entries.first(where: { $0["id"] as? String == id }),
              let asset = record["spritesheetUrl"] as? String, let url = URLComponents(string: asset),
              url.scheme == "https", let host = url.host?.lowercased(),
              host == "oaiusercontent.com" || host.hasSuffix(".oaiusercontent.com"),
              url.user == nil && url.password == nil && url.query == nil && url.fragment == nil
        else { return result }
        let fingerprint = record["spritesheetFingerprint"] as? String ?? ""
        result.source = .cloud(owner: owner, url: asset, fingerprint: fingerprint)
        // A migrated local name is display metadata. Never substitute its image for a cloud ID.
        let migrations = atoms["migrated-cloud-pet-ids-v1"] as? [String: [String: String]]
        let aliases = migrations?[key] ?? [:]
        var alias = aliases.first { $0.value == id }?.key
        if alias == nil, !fingerprint.isEmpty {
            let peers = Set(entries.compactMap {
                $0["spritesheetFingerprint"] as? String == fingerprint ? $0["id"] as? String : nil
            })
            let candidates = aliases.filter { peers.contains($0.value) }
            if candidates.count == 1 { alias = candidates.first?.key }
        }
        if let alias, let local = localChoice(alias) { result.name = local.name }
        return result
    }

    private func legacyChoice(config: URL) -> PetChoice {
        guard let data = limitedData(config, limit: 1024 * 1024), let text = String(data: data, encoding: .utf8)
        else { return PetChoice() }
        var desktopSection = false
        for line in text.components(separatedBy: .newlines) {
            let clean = line.trimmingCharacters(in: .whitespaces)
            if clean.hasPrefix("[") { desktopSection = clean == "[desktop]" }
            guard desktopSection, let eq = clean.firstIndex(of: "="),
                  clean[..<eq].trimmingCharacters(in: .whitespaces) == "selected-avatar-id" else { continue }
            let value = clean[clean.index(after: eq)...].drop(while: \.isWhitespace)
            guard let quote = value.first, quote == "\"" || quote == "'", let end = value.dropFirst().firstIndex(of: quote)
            else { break }
            return namedChoice(id: String(value[value.index(after: value.startIndex)..<end]))
        }
        return PetChoice()
    }

    private func namedChoice(id: String) -> PetChoice {
        if ["default", "none"].contains(id) { return PetChoice(pending: .disabledPet) }
        if let builtIn = Self.builtinNames[id] {
            return PetChoice(id: id, name: builtIn, source: .builtIn(id), pending: .waitBuiltinPet)
        }
        return localChoice(id) ?? PetChoice(id: id, pending: .waitCloudPet)
    }

    private func localChoice(_ id: String) -> PetChoice? {
        guard id.hasPrefix("custom:") else { return nil }
        let slug = String(id.dropFirst(7))
        guard isFilename(slug) else { return nil }
        let pets = desktop.appendingPathComponent("pets").resolvingSymlinksInPath()
        let folder = pets.appendingPathComponent(slug).resolvingSymlinksInPath()
        guard folder.deletingLastPathComponent().path == pets.path else { return nil }
        let manifest = folder.appendingPathComponent("pet.json").resolvingSymlinksInPath()
        guard manifest.deletingLastPathComponent().path == folder.path,
              let data = limitedData(manifest, limit: 64 * 1024),
              let info = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let path = info["spritesheetPath"] as? String, isFilename(path) else { return nil }
        let image = folder.appendingPathComponent(path).resolvingSymlinksInPath()
        guard image.deletingLastPathComponent().path == folder.path else { return nil }
        return PetChoice(id: id, name: info["displayName"] as? String ?? slug, source: .local(image), pending: .waitLocalPet)
    }

    private func isFilename(_ text: String) -> Bool {
        !text.isEmpty && text != "." && text != ".." && !text.contains("/") && !text.contains("\u{0}")
    }

    private func fileStamp(_ url: URL) -> String {
        guard let a = try? fm.attributesOfItem(atPath: url.path) else { return "-" }
        return "\((a[.modificationDate] as? Date)?.timeIntervalSinceReferenceDate ?? 0)/\(a[.size] ?? 0)/\(a[.systemFileNumber] ?? 0)"
    }

    private func limitedData(_ url: URL, limit: Int) -> Data? {
        guard let file = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? file.close() }
        guard let length = try? file.seekToEnd(), length > 0, length <= limit,
              (try? file.seek(toOffset: 0)) != nil else { return nil }
        return try? file.read(upToCount: Int(length))
    }

    private func spriteRows(_ data: Data) -> Int? {
        guard let src = CGImageSourceCreateWithData(data as CFData, nil),
              CGImageSourceGetStatus(src) == .statusComplete,
              let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any],
              let w = props[kCGImagePropertyPixelWidth] as? Int, let h = props[kCGImagePropertyPixelHeight] as? Int,
              w > 0, h > 0, w <= 4096, h <= 4096 else { return nil }
        return [9, 11].first { rows in w.isMultiple(of: 8) && h.isMultiple(of: rows) && w * 208 * rows == h * 192 * 8 }
    }

    private func cloudArtwork(owner: String, id: String, url: String, fingerprint: String) -> Data? {
        let assetKey = SHA256.hash(data: Data([owner, id, url].joined(separator: "\n").utf8))
            .map { String(format: "%02x", $0) }.joined()
        // A cloud ID and its artwork each have independent revisions. If ChatGPT replaces the image at
        // the same path, an older Chromium entry must not resurrect the previous locally observed bytes.
        var superseded: Data?
        if let data = limitedData(ownCache, limit: Self.imageLimit + 4096),
           let record = (try? PropertyListSerialization.propertyList(from: data, format: nil)) as? [String: Any],
           record["asset"] as? String == assetKey, let bytes = record["image"] as? Data {
            if record["revision"] as? String == fingerprint {
                if spriteRows(bytes) != nil { return bytes }
            } else { superseded = bytes }
        }
        guard let entries = try? fm.contentsOfDirectory(at: webCache, includingPropertiesForKeys: nil) else { return nil }
        for entry in entries where entry.lastPathComponent.hasSuffix("_0") {
            guard let bytes = imageFromCacheEntry(entry, matching: url), bytes != superseded, spriteRows(bytes) != nil else { continue }
            let record: [String: Any] = ["asset": assetKey, "revision": fingerprint, "image": bytes]
            if let record = try? PropertyListSerialization.data(fromPropertyList: record, format: .binary, options: 0) {
                try? fm.createDirectory(at: ownCache.deletingLastPathComponent(), withIntermediateDirectories: true)
                try? record.write(to: ownCache, options: .atomic)
            }
            return bytes
        }
        return nil
    }

    /// Chromium simple-cache v5 stores stream 1 directly after its key. Inspect the key prefix
    /// only. Response bodies are read solely on an exact match to the selected image's origin/path.
    private func imageFromCacheEntry(_ url: URL, matching asset: String) -> Data? {
        guard let f = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? f.close() }
        guard let header = try? f.read(upToCount: 24), header.count == 24,
              Array(header.prefix(8)) == [0x30,0x5c,0x72,0xa7,0x1b,0x6d,0xfb,0xfc],
              header.uint32LE(8) == 5 else { return nil }
        let length = header.uint32LE(12)
        guard length > 0, length < 65536,
              let key = try? f.read(upToCount: min(length, asset.utf8.count + 256)),
              let range = key.range(of: Data(asset.utf8)),
              range.lowerBound > 0, key[range.lowerBound - 1] == 32 || key[range.lowerBound - 1] == 47,
              range.upperBound == length || (range.upperBound < key.count && key[range.upperBound] == 63),
              let total = try? f.seekToEnd(), total >= 48, total <= UInt64(Self.imageLimit + 2 * 1024 * 1024),
              (try? f.seek(toOffset: total - 24)) != nil, let last = try? f.read(upToCount: 24), last.count == 24
        else { return nil }
        // The last EOF record gives the HTTP metadata length. Skip that stream entirely.
        let eofMagic: [UInt8] = [0xd8,0x41,0x0d,0x97,0x45,0x6f,0xfa,0xf4]
        guard last.starts(with: eofMagic) else { return nil }
        let digestSize = (last.uint32LE(8) & 2) == 0 ? 0 : 32
        let end = Int(total) - 24 - digestSize - last.uint32LE(16) - 24
        let start = 24 + length
        let imageSize = end - start
        guard imageSize > 0, imageSize <= Self.imageLimit,
              (try? f.seek(toOffset: UInt64(end))) != nil, let marker = try? f.read(upToCount: 8),
              marker.elementsEqual(eofMagic), (try? f.seek(toOffset: UInt64(start))) != nil else { return nil }
        return try? f.read(upToCount: imageSize)
    }

    /// Read the named sprite from ChatGPT's Electron ASAR index. No extraction or copying of the app is needed.
    private func builtInArtwork(_ id: String) -> Data? {
        guard let file = try? FileHandle(forReadingFrom: archive) else { return nil }
        defer { try? file.close() }
        guard let header = try? file.read(upToCount: 16), header.count == 16, header.uint32LE(0) == 4 else { return nil }
        let pickleSize = header.uint32LE(4), jsonSize = header.uint32LE(12)
        guard jsonSize > 0, pickleSize <= 32 * 1024 * 1024, jsonSize <= pickleSize - 8,
              let json = try? file.read(upToCount: jsonSize), json.count == jsonSize,
              let root = (try? JSONSerialization.jsonObject(with: json)) as? [String: Any] else { return nil }
        var parent = root
        for part in ["webview", "assets"] {
            guard let entries = parent["files"] as? [String: Any], let folder = entries[part] as? [String: Any] else { return nil }
            parent = folder
        }
        guard let entries = parent["files"] as? [String: Any],
              let key = entries.keys.first(where: { $0.hasPrefix(id + "-spritesheet-v") && $0.hasSuffix(".webp") }),
              let meta = entries[key] as? [String: Any], let offsetText = meta["offset"] as? String,
              let offset = Int(offsetText), offset >= 0, offset <= Int.max - 8 - pickleSize,
              let length = meta["size"] as? Int, length > 0, length <= Self.imageLimit,
              (try? file.seek(toOffset: UInt64(8 + pickleSize + offset))) != nil else { return nil }
        return try? file.read(upToCount: length)
    }
}

private extension Data {
    func uint32LE(_ ix: Int) -> Int { (0..<4).reduce(0) { $0 | (Int(self[ix + $1]) << ($1 * 8)) } }
}
