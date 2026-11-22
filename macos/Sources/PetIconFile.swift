import AppKit

enum PetIconFile {
    // Standard and high-resolution icon slots share the same pixel artwork at a given size.
    private static let slots = [("icp4", 16), ("icp5", 32), ("ic07", 128), ("ic08", 256),
                                ("ic09", 512), ("ic10", 1024), ("ic11", 32), ("ic12", 64),
                                ("ic13", 256), ("ic14", 512)]

    private static func chunk(_ tag: String, _ payload: Data) -> Data {
        var length = UInt32(payload.count + 8).bigEndian
        var chunk = Data(tag.utf8)
        withUnsafeBytes(of: &length) { chunk.append(contentsOf: $0) }
        chunk.append(payload)
        return chunk
    }

    private static func renderPNG(pose: CGImage?, pixels: Int) throws -> Data {
        guard let image = PetAppIconArt.render(pose: pose, pixels: pixels),
              let data = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else {
            throw CocoaError(.fileWriteUnknown)
        }
        return data
    }

    static func encode(pose: CGImage?) throws -> Data {
        var chunks = Data()
        var encoded: [Int: Data] = [:]
        for (tag, pixels) in slots {
            let png = try encoded[pixels] ?? renderPNG(pose: pose, pixels: pixels)
            encoded[pixels] = png
            chunks.append(chunk(tag, png))
        }
        return chunk("icns", chunks)
    }
}
