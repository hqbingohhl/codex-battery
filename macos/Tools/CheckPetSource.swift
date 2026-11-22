import AppKit

@main
enum CheckPetSource {
    static func main() throws {
        let fm = FileManager.default
        let fixture = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? fm.removeItem(at: fixture) }
        let desktop = fixture.appendingPathComponent(".codex")
        let stored = fixture.appendingPathComponent("own/selected.plist")
        let cache = fixture.appendingPathComponent("Library/Caches/Codex/Default/Cache/Cache_Data")
        let assets = fixture.appendingPathComponent("ChatGPT.app")
        let pet = desktop.appendingPathComponent("pets/alpha")
        for url in [pet, cache] { try fm.createDirectory(at: url, withIntermediateDirectories: true) }
        func reader() -> DesktopPetReader {
            DesktopPetReader(home: fixture, app: assets, codexDirectory: desktop, artworkCache: stored)
        }
        let state = desktop.appendingPathComponent(".codex-global-state.json")
        let firstURL = "https://assets.oaiusercontent.com/files/first/raw"
        let otherURL = "https://assets.oaiusercontent.com/files/other/raw"
        let key = "[\"test-account\",\"test-user\"]"
        func choose(_ id: String, url: String = firstURL, fingerprint: String = "a", owner: String = "owner") throws {
            let artwork: [String: Any] = ["accountId":"test-account", "userId":"test-user",
                                          "pets":[["id":id, "spritesheetUrl":url, "spritesheetFingerprint":fingerprint]]]
            let atoms: [String: Any] = ["last-observed-cloud-pet-selection-v1":[key:id], "cloud-pet-artwork-cache":artwork]
            let json: [String: Any] = ["electron-renderer-content-owner":owner, "electron-owner-atoms:owner":atoms]
            try JSONSerialization.data(withJSONObject: json).write(to: state, options: .atomic)
        }
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 96, pixelsHigh: 117,
                                      bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                      colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        let image = bitmap.representation(using: .png, properties: [:])!
        let local = pet.appendingPathComponent("sheet.png")
        try image.write(to: local)
        try Data(#"{"displayName":"Alpha","spritesheetPath":"sheet.png"}"#.utf8).write(to: pet.appendingPathComponent("pet.json"))
        func cacheEntry(for url: String, at file: URL, data: Data) throws {
            let key = Data(("1/0/_dk_app://- app://- " + url + "?temporary=fixture").utf8)
            func little(_ value: Int) -> [UInt8] { (0..<4).map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) } }
            var entry = Data([0x30,0x5c,0x72,0xa7,0x1b,0x6d,0xfb,0xfc] + little(5) + little(key.count) + Array(repeating: 0, count: 8))
            let eof = [UInt8]([0xd8,0x41,0x0d,0x97,0x45,0x6f,0xfa,0xf4])
            entry.append(key)
            entry.append(data)
            entry.append(contentsOf: eof + Array(repeating: 0, count: 16))
            entry.append(contentsOf: [1,2,3])
            entry.append(contentsOf: Array(repeating: 0, count: 32))
            entry.append(contentsOf: eof + little(3) + little(0) + little(3) + little(0))
            try entry.write(to: file, options: .atomic)
        }

        // Run the application's timer, with atomic replacement of the same observed file.
        try choose("custom:alpha")
        let monitor = PetSource(makeReader: reader)
        var observed: [SelectedPet] = []
        monitor.onChange = { observed.append($0) }
        monitor.start()
        func waitFor(_ predicate: () -> Bool) {
            let until = Date().addingTimeInterval(9)
            while !predicate(), Date() < until { RunLoop.main.run(until: Date().addingTimeInterval(0.05)) }
            precondition(predicate(), "monitor timed out")
        }
        waitFor { observed.last?.displayName == "Alpha" }
        precondition(observed.last?.artwork == image)
        try choose("default")
        waitFor { observed.last?.pending == .disabledPet }
        precondition(observed.last?.identifier == nil && observed.last?.artwork == nil)
        monitor.stop()

        let reread = reader()
        let first = cache.appendingPathComponent("first_0")
        try choose("pet_one")
        try cacheEntry(for: firstURL + "suffix", at: cache.appendingPathComponent("near_0"), data: image)
        precondition(reread.read()?.artwork == nil)
        try cacheEntry(for: firstURL, at: first, data: image)
        precondition(reread.read(force: true)?.artwork == image)
        precondition(reread.read(force: true) == nil)
        try fm.removeItem(at: first)
        precondition(reader().read()?.artwork == image, "authorized current image must survive normal Chromium cache eviction")
        try choose("pet_two", url: otherURL)
        precondition(reread.read()?.artwork == nil, "new pet reused stale image")
        try cacheEntry(for: firstURL, at: first, data: image)
        try choose("pet_one", fingerprint: "edited")
        precondition(reread.read()?.artwork == nil, "edited pet reused stale image")
        try choose("pet_one", owner: "different")
        precondition(reread.read()?.identifier == nil, "signed-out account reused a selection")

        if CommandLine.arguments.contains("--live") {
            let live = DesktopPetReader(artworkCache: fixture.appendingPathComponent("live.plist")).read()
            precondition(live?.artwork != nil)
            print("Active:", live!.status, live!.artwork!.count, "bytes,", live!.rows, "rows")
            try choose("dewey")
            let builtin = DesktopPetReader(home: fixture, codexDirectory: desktop).read()
            precondition(builtin?.displayName == "Dewey" && builtin?.rows == 11 && builtin?.artwork != nil)
        }
        print("Pet selection, live changes, isolated cache and revision checks passed.")
    }
}
