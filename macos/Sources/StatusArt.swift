import AppKit
import ImageIO

func quotaColor(_ percent: Int?) -> NSColor {
    guard let percent else { return .secondaryLabelColor }
    if percent <= 20 { return .systemRed }
    if percent <= 50 { return .systemOrange }
    return .systemGreen
}

private let batterySize = NSSize(width: 40, height: 20)
enum MenuArtSize: String, CaseIterable {
    case small, regular, large

    fileprivate var height: CGFloat {
        switch self {
        case .small: return 16
        case .regular: return 19
        case .large: return 22
        }
    }
}

/// AppKit requests status art on the main thread. A blink reuses both the gauge
/// and the previously rendered poses; limit distinct images as percentages change.
private final class MenuImages {
    static let shared = MenuImages()
    let cache: NSCache<NSString, NSImage> = {
        let cache = NSCache<NSString, NSImage>()
        cache.countLimit = 80
        return cache
    }()

    func image(key: String, draw: () -> NSImage) -> NSImage {
        if let existing = cache.object(forKey: key as NSString) { return existing }
        let result = draw()
        cache.setObject(result, forKey: key as NSString)
        return result
    }
}

func battery(_ percent: Int?, stale: Bool = false, style: String = "mono", dark: Bool = false) -> NSImage {
    let remaining = stale ? nil : percent.map { max(0, min(100, $0)) }
    let key = "g/\(remaining ?? -1)/\(style)/\(dark)"
    return MenuImages.shared.image(key: key) {
        let template = style == "mono"
        let mono = template || style == "neutral"
        let ink: NSColor = dark && !template ? .white : .black
        let tint = style == "teal" ? NSColor.systemTeal : quotaColor(remaining)
        let outline: NSColor
        if !mono, let remaining, remaining <= 20 { outline = tint } else { outline = ink.withAlphaComponent(0.72) }

        let image = NSImage(size: batterySize, flipped: false) { _ in
            let body = NSRect(x: 1, y: 2.5, width: 34.5, height: 15)
            let casing = NSBezierPath(roundedRect: body, xRadius: 3.6, yRadius: 3.6)
            // Stable text contrast on translucent menubars and coloured wallpapers.
            if !template {
                NSColor(white: dark ? 0.13 : 0.98, alpha: 0.87).setFill()
                casing.fill()
            }
            if let remaining, remaining > 0 {
                let track = body.insetBy(dx: 1.5, dy: 1.5)
                NSGraphicsContext.saveGraphicsState()
                NSBezierPath(roundedRect: track, xRadius: 2.1, yRadius: 2.1).addClip()
                (mono ? ink : tint).withAlphaComponent(mono ? 0.23 : (dark ? 0.39 : 0.52)).setFill()
                NSRect(x: track.minX, y: track.minY, width: track.width * CGFloat(remaining) / 100, height: track.height).fill()
                NSGraphicsContext.restoreGraphicsState()
            }
            outline.setStroke()
            casing.lineWidth = 1.25
            casing.stroke()
            outline.setFill()
            NSBezierPath(roundedRect: NSRect(x: 37.1, y: 7.2, width: 2.2, height: 5.6), xRadius: 1, yRadius: 1).fill()

            let label = (remaining.map { "\($0)%" } ?? "—") as NSString
            let attrs: [NSAttributedString.Key: Any] = [
                .font: NSFont.monospacedDigitSystemFont(ofSize: 10.5, weight: .bold),
                .foregroundColor: ink
            ]
            let textSize = label.size(withAttributes: attrs)
            label.draw(at: NSPoint(x: body.midX - textSize.width / 2, y: body.midY - textSize.height / 2), withAttributes: attrs)
            return true
        }
        image.isTemplate = template
        return image
    }
}

final class PetArt {
    static let shared = PetArt()
    private var frames: [Int: NSImage] = [:]

    private init() {}

    func clear() {
        frames.removeAll()
        MenuImages.shared.cache.removeAllObjects()
    }

    /// Decode on the source's worker queue. CGImage crops and alpha scans are
    /// independent of AppKit; only install the completed set on the main thread.
    static func decode(_ data: Data, columns: Int, rows: Int) -> [Int: CGImage]? {
        guard (1...64).contains(columns), (1...64).contains(rows),
              data.count <= 32 * 1024 * 1024,
              let source = CGImageSourceCreateWithData(data as CFData, nil),
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = properties[kCGImagePropertyPixelWidth] as? Int,
              let height = properties[kCGImagePropertyPixelHeight] as? Int,
              width > 0, height > 0, width <= 8192, height <= 8192, width * height <= 20_000_000,
              width % columns == 0, height % rows == 0,
              let sheet = CGImageSourceCreateImageAtIndex(source, 0, [kCGImageSourceShouldCacheImmediately: true] as CFDictionary)
        else { return nil }

        let w = width / columns, h = height / rows
        let color = CGColorSpace(name: CGColorSpace.sRGB)!
        let format = CGBitmapInfo.byteOrder32Big.rawValue | CGImageAlphaInfo.premultipliedLast.rawValue
        var poses: [Int: CGImage] = [:]
        var bounds = CGRect.null
        // Fit the union of visible pixels so small, off-centre pets use the
        // available space and blinking never shifts the pet or cuts a limb off.
        for (row, count) in [(0, 6), (5, 3)] where row < rows {
            for column in 0..<min(count, columns) {
                let cell = CGRect(x: column * w, y: row * h, width: w, height: h)
                guard let crop = sheet.cropping(to: cell),
                      let context = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                              space: color, bitmapInfo: format),
                      let pixels = context.data?.assumingMemoryBound(to: UInt8.self) else { continue }
                context.draw(crop, in: CGRect(x: 0, y: 0, width: w, height: h))
                var left = w, top = h, right = -1, bottom = -1
                for y in 0..<h {
                    let scanline = pixels.advanced(by: y * context.bytesPerRow)
                    for x in 0..<w where scanline[x * 4 + 3] != 0 {
                        left = min(left, x)
                        right = max(right, x)
                        top = min(top, y)
                        bottom = y
                    }
                }
                if right >= left, let copied = context.makeImage() {
                    poses[(row == 0 ? 0 : 16) + column] = copied
                    bounds = bounds.union(CGRect(x: left, y: top, width: right - left + 1, height: bottom - top + 1))
                }
            }
        }
        guard poses[0] != nil else { return nil }
        let result = poses.compactMapValues { $0.cropping(to: bounds) }
        return result[0] == nil ? nil : result
    }

    func install(_ poses: [Int: CGImage]) {
        clear()
        frames = poses.mapValues { NSImage(cgImage: $0, size: NSSize(width: $0.width, height: $0.height)) }
    }

    @discardableResult
    func load(_ data: Data, columns: Int, rows: Int) -> Bool {
        guard let poses = Self.decode(data, columns: columns, rows: rows) else { return false }
        install(poses)
        return true
    }

    func frame(_ index: Int = 0, tired: Bool = false) -> NSImage? {
        if tired, let tiredPose = frames[16 + max(0, index) % 3] ?? frames[16] { return tiredPose }
        return frames[max(0, index) % 6] ?? frames[0]
    }
}

func statusArt(_ percent: Int?, stale: Bool, style: String, dark: Bool, pet: Bool, frame: Int,
               size: MenuArtSize = .regular, maxHeight: CGFloat = 22) -> NSImage {
    let remaining = stale ? nil : percent.map { max(0, min(100, $0)) }
    let tired = remaining.map { $0 <= 20 } ?? false
    let pose = max(0, frame) % (tired ? 3 : 6)
    let avatar = pet ? PetArt.shared.frame(pose, tired: tired) : nil
    let height = max(1, floor(min(maxHeight, size.height)))
    let key = "status/\(remaining ?? -1)/\(style)/\(dark)/\(avatar != nil)/\(pose)/\(height)"
    return MenuImages.shared.image(key: key) {
        let gauge = battery(remaining, style: style == "mono" && avatar != nil ? "neutral" : style, dark: dark)
        let gaugeWidth = ceil(batterySize.width * height / batterySize.height)
        let avatarSlot = avatar == nil ? 0 : height + 1
        let image = NSImage(size: NSSize(width: gaugeWidth + avatarSlot, height: height), flipped: false) { _ in
            NSGraphicsContext.current?.imageInterpolation = .high
            if let avatar {
                let edge = max(1, height - 1)
                let scale = min(edge / avatar.size.width, edge / avatar.size.height)
                let w = avatar.size.width * scale, h = avatar.size.height * scale
                avatar.draw(in: NSRect(x: (height - w) / 2, y: (height - h) / 2, width: w, height: h))
            }
            gauge.draw(in: NSRect(x: avatarSlot, y: 0, width: gaugeWidth, height: height))
            return true
        }
        image.isTemplate = avatar == nil && style == "mono"
        return image
    }
}
