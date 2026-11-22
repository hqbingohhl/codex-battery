import Cocoa

if CommandLine.arguments.contains("--self-test") {
    precondition(WindowQuota(["usedPercent": 22.5])?.remaining == 77)
    precondition(WindowQuota(["usedPercent": 110])?.remaining == 0)
    precondition(WindowQuota(["usedPercent": -1])?.remaining == 100)
    precondition(WindowQuota([:]) == nil)
    let q = Quotas(["rateLimitsByLimitId": ["codex": ["primary": ["usedPercent": 2]]], "rateLimits": ["primary": ["usedPercent": 99]]])
    precondition(q.primary?.remaining == 98)
    precondition(Quotas(["rateLimitsByLimitId": [:]]).primary == nil)
    PetArt.shared.clear()
    precondition(quotaColor(10) == NSColor.systemRed)
    let preview = NSImage(size: NSSize(width: 520, height: 230), flipped: false) { _ in
        NSColor.white.setFill(); NSRect(x: 0, y: 0, width: 520, height: 230).fill()
        NSColor(calibratedWhite: 0.12, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: 520, height: 105).fill()
        for dark in [false, true] {
            let y: Double = dark ? 45 : 160
            let text = dark ? NSColor.white : NSColor.black
            ((dark ? "Dark menu bar" : "Light menu bar") as NSString).draw(at: NSPoint(x: 20, y: y + 40), withAttributes: [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: text])
            for (i, n) in [98, 50, 9].enumerated() {
                let img = statusArt(n, stale: false, style: "auto", dark: dark, pet: true, frame: 0)
                img.draw(in: NSRect(x: Double(22 + i * 130), y: y, width: 100.5, height: 33))
            }
            statusArt(nil, stale: true, style: "auto", dark: dark, pet: true, frame: 0).draw(in: NSRect(x: 412, y: y, width: 100.5, height: 33))
        }
        return true
    }
    if CommandLine.arguments.count > 2, let tiff = preview.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff), let png = rep.representation(using: .png, properties: [:]) {
        try png.write(to: URL(fileURLWithPath: CommandLine.arguments[2]))
    }
    print("Quota parsing and battery rendering checks passed")
} else if CommandLine.arguments.contains("--probe-pet") {
    let source = PetSource()
    source.onChange = { pet in
        guard let data = pet.artwork else {
            print(pet.status)
            exit(2)
        }
        guard PetArt.decode(data, columns: pet.columns, rows: pet.rows) != nil else {
            fputs(tr(.petDecode) + "\n", stderr)
            exit(1)
        }
        print("\(pet.status) | \(pet.identifier ?? "") | \(pet.columns) × \(pet.rows)")
        exit(0)
    }
    source.start()
    DispatchQueue.main.asyncAfter(deadline: .now() + 20) { exit(3) }
    RunLoop.main.run()
} else if CommandLine.arguments.contains("--probe") {
    let client = UsageClient()
    client.onUpdate = { data in
        for (slot, quota) in [("primary", data.primary), ("secondary", data.secondary)] {
            if let quota { print("\(slot): " + tr(.quotaDetail, quota.label, quota.remaining)) }
        }
        client.stop()
        exit(data.primary == nil && data.secondary == nil ? 2 : 0)
    }
    client.onError = { error in
        fputs(error.text + "\n", stderr)
        client.stop()
        exit(1)
    }
    client.refresh()
    RunLoop.main.run()
} else {
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    let delegate = AppDelegate()
    app.delegate = delegate
    app.run()
}
