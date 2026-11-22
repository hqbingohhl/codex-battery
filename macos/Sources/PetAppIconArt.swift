import AppKit

enum PetAppIconArt {
    private static let srgb = CGColorSpace(name: CGColorSpace.sRGB)!

    /// The pose is a single, alpha-trimmed idle frame from PetArt.
    static func render(pose: CGImage?, pixels: Int) -> CGImage? {
        guard (16...1024).contains(pixels),
              let ctx = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8,
                                  bytesPerRow: 0, space: srgb,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let scale = CGFloat(pixels) / 256
        ctx.scaleBy(x: scale, y: scale)
        ctx.interpolationQuality = .high
        let tiny = pixels <= 32
        let tile = rounded(CGRect(x: 19, y: 19, width: 218, height: 218), 48)

        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -3 * scale), blur: 6 * scale,
                      color: color(0.04, 0.15, 0.14, 0.28))
        ctx.setFillColor(color(0.38, 0.61, 0.52))
        ctx.addPath(tile)
        ctx.fillPath()
        ctx.restoreGState()

        ctx.saveGState()
        ctx.addPath(tile)
        ctx.clip()
        let colors = [color(0.97, 0.99, 0.89), color(0.77, 0.92, 0.77), color(0.45, 0.72, 0.66)]
        if let gradient = CGGradient(colorsSpace: srgb, colors: colors as CFArray, locations: [0, 0.55, 1]) {
            ctx.drawLinearGradient(gradient, start: CGPoint(x: 103, y: 227), end: CGPoint(x: 154, y: 20),
                                   options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        }
        if !tiny {
            // A thin inset highlight keeps the edge defined against both Finder appearances.
            ctx.setLineWidth(1.4)
            ctx.setStrokeColor(color(1, 1, 0.96, 0.40))
            ctx.addPath(rounded(CGRect(x: 20.3, y: 20.3, width: 215.4, height: 215.4), 47))
            ctx.strokePath()
        }

        if let pose {
            let space = CGRect(x: 32, y: 73, width: 192, height: 152)
            let ratio = min(space.width / CGFloat(pose.width), space.height / CGFloat(pose.height))
            let size = CGSize(width: CGFloat(pose.width) * ratio, height: CGFloat(pose.height) * ratio)
            let target = CGRect(x: space.midX - size.width / 2, y: space.midY - size.height / 2,
                                width: size.width, height: size.height)
            ctx.saveGState()
            if !tiny {
                ctx.setShadow(offset: CGSize(width: 0, height: -1.5 * scale), blur: 2 * scale,
                              color: color(0.08, 0.23, 0.17, 0.22))
            }
            ctx.draw(pose, in: target)
            ctx.restoreGState()
        }
        // The terminal is part of the silhouette even at 16 px. At that size a
        // single luminous cell stays legible where several small bars would merge.
        let box: CGRect
        if pose == nil { box = CGRect(x: 46, y: 91, width: 164, height: 74) }
        else if tiny { box = CGRect(x: 65, y: 33, width: 126, height: 35) }
        else { box = CGRect(x: 72, y: 35, width: 112, height: 31) }
        drawBattery(in: box, context: ctx, tiny: tiny)
        ctx.restoreGState()
        return ctx.makeImage()
    }

    private static func drawBattery(in box: CGRect, context ctx: CGContext, tiny: Bool) {
        let height = box.height
        let body = CGRect(x: box.minX, y: box.minY, width: box.width - height * 0.20, height: height)
        let cap = CGRect(x: body.maxX - height * 0.07, y: box.midY - height * 0.21,
                         width: height * 0.27, height: height * 0.42)
        ctx.setFillColor(color(0.09, 0.25, 0.23))
        ctx.addPath(rounded(cap, height * 0.08))
        ctx.fillPath()
        ctx.addPath(rounded(body, height * 0.29))
        ctx.fillPath()
        let inner = body.insetBy(dx: height * 0.16, dy: height * 0.16)
        ctx.setFillColor(color(0.26, 0.42, 0.36))
        ctx.addPath(rounded(inner, height * 0.15))
        ctx.fillPath()
        ctx.setFillColor(color(0.77, 0.97, 0.56))
        let charge = CGRect(x: inner.minX, y: inner.minY, width: inner.width * 0.73, height: inner.height)
        ctx.addPath(rounded(charge, height * 0.14))
        ctx.fillPath()
        if !tiny {
            ctx.setFillColor(color(0.09, 0.25, 0.23))
            for division in [1, 2] {
                let x = charge.minX + charge.width * CGFloat(division) / 3
                ctx.fill(CGRect(x: x - height * 0.024, y: charge.minY, width: height * 0.048, height: charge.height))
            }
        }
    }

    private static func color(_ red: CGFloat, _ green: CGFloat, _ blue: CGFloat, _ alpha: CGFloat = 1) -> CGColor {
        CGColor(srgbRed: red, green: green, blue: blue, alpha: alpha)
    }

    private static func rounded(_ rect: CGRect, _ radius: CGFloat) -> CGPath {
        CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius, transform: nil)
    }
}
