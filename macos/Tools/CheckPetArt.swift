import AppKit
import ImageIO

@main
enum CheckPetArt {
    // Distinct marks at opposite edges catch wrong row selection, head-only
    // crops, vertical mirroring and clipped silhouettes on either layout.
    static func sheet(rows: Int) -> Data {
        let width = 16 * 8, height = 20 * rows
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        func mark(_ x: Int, _ y: Int, _ channel: Int) {
            let i = (y * width + x) * 4
            pixels[i + channel] = 255
            pixels[i + 3] = 255
        }
        for y in 2..<6 {
            for x in 4..<9 { mark(x, y, 0) }
        }
        for y in 11..<14 {
            for x in 9..<12 { mark(x, y, 2) }
        }
        // Second idle frame and first tired frame extend the common bounds.
        mark(16 + 3, 12, 1)
        mark(12, 5 * 20 + 15, 1)
        let image = CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32,
                            bytesPerRow: 4 * width, space: CGColorSpaceCreateDeviceRGB(),
                            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.last.rawValue),
                            provider: CGDataProvider(data: Data(pixels) as CFData)!, decode: nil,
                            shouldInterpolate: false, intent: .defaultIntent)!
        let png = NSMutableData()
        let dest = CGImageDestinationCreateWithData(png, "public.png" as CFString, 1, nil)!
        CGImageDestinationAddImage(dest, image, nil)
        precondition(CGImageDestinationFinalize(dest))
        return png as Data
    }

    static func main() throws {
        let pet = PetArt.shared
        for rows in [9, 11] {
            precondition(pet.load(sheet(rows: rows), columns: 8, rows: rows))
            let portrait = pet.frame()!
            precondition(portrait.size == NSSize(width: 10, height: 14))
            let rep = NSBitmapImageRep(cgImage: portrait.cgImage(forProposedRect: nil, context: nil, hints: nil)!)
            var red = 0, blue = 0
            for y in 0..<rep.pixelsHigh {
                for x in 0..<rep.pixelsWide {
                    let p = rep.colorAt(x: x, y: y)!.usingColorSpace(.sRGB)!
                    if p.alphaComponent > 0.9 {
                        if p.redComponent > 0.9 { red += 1 }
                        if p.blueComponent > 0.9 { blue += 1 }
                    }
                }
            }
            precondition(red == 20 && blue == 9)
            precondition(pet.frame(1)?.size == portrait.size && pet.frame(0, tired: true)?.size == portrait.size)
            precondition(!pet.load(Data(), columns: 8, rows: rows))
            precondition(pet.frame() === portrait)
        }
        let art = sheet(rows: 9)
        precondition(pet.load(art, columns: 8, rows: 9))
        for maxHeight: CGFloat in [16, 18, 22, 24] {
            for size in MenuArtSize.allCases {
                for value: Int? in [nil, 0, 10, 100] {
                    let battery = statusArt(value, stale: false, style: "auto", dark: false, pet: false, frame: 0, size: size, maxHeight: maxHeight)
                    let image = statusArt(value, stale: false, style: "auto", dark: false, pet: true, frame: 1, size: size, maxHeight: maxHeight)
                    precondition(image.size.height <= maxHeight && image.size.height == battery.size.height)
                    precondition(image.size.width > battery.size.width)
                    precondition(image.tiffRepresentation != nil)
                }
            }
        }
        let previous = statusArt(75, stale: false, style: "auto", dark: false, pet: true, frame: 0)
        pet.clear()
        precondition(pet.frame() == nil)
        precondition(statusArt(75, stale: false, style: "auto", dark: false, pet: true, frame: 0).size.width < previous.size.width)
        precondition(pet.load(art, columns: 8, rows: 9))
        precondition(statusArt(75, stale: false, style: "auto", dark: false, pet: true, frame: 0) !== previous)
        print("Pet v1/v2 silhouettes, asset replacement and menu sizing passed.")
    }
}
