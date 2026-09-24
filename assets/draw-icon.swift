import AppKit
import CoreGraphics

// The brand, sampled from the design sheet.
func rgb(_ hex: UInt32) -> CGColor {
    CGColor(red: CGFloat((hex >> 16) & 0xFF)/255, green: CGFloat((hex >> 8) & 0xFF)/255,
            blue: CGFloat(hex & 0xFF)/255, alpha: 1)
}
let plum   = rgb(0x381F51)   // the dark ink: the tile, and the overlap on a pale ground
let purple = rgb(0x694293)   // the left bubble on a pale ground
let lilac  = rgb(0xBB9FDF)   // the left bubble on the dark tile
let salmon = rgb(0xE9A186)   // the right bubble, either way
let cream  = rgb(0xFCF3EE)   // the page, and the overlap on the dark tile

/// One speech bubble: a circle and a tail, filled in one colour so they read as one
/// shape.
///
/// The tail is a sliver rather than a wedge. Its outer edge leaves the circle almost
/// radially, so it is short; its inner edge runs all the way back to the bottom of the
/// circle. Drawn symmetrically about its own direction it becomes a sail, and drawn
/// with tangent edges it becomes a bigger one — the asymmetry is the whole look.
///
/// `lean` is -1 for the left bubble, whose tail goes to the bottom left, and +1 for the
/// right, which is its mirror.
func bubble(_ ctx: CGContext, cx: CGFloat, cy: CGFloat, r: CGFloat, lean: CGFloat, _ colour: CGColor) {
    ctx.setFillColor(colour)
    ctx.fillEllipse(in: CGRect(x: cx - r, y: cy - r, width: r*2, height: r*2))

    // Degrees from east, anticlockwise. The point sits 1.65 radii out, which puts its
    // tip level with the outside of the circle and a third of a radius below it.
    func at(_ deg: CGFloat, _ dist: CGFloat) -> CGPoint {
        let rad = (lean < 0 ? deg : 180 - deg) * .pi / 180
        return CGPoint(x: cx + cos(rad) * r * dist, y: cy + sin(rad) * r * dist)
    }
    ctx.move(to: at(234, 1.65))   // the point
    ctx.addLine(to: at(236, 1))   // out of the circle's edge, near enough radially
    ctx.addLine(to: at(281, 1))   // and back to its underside
    ctx.closePath()
    ctx.fillPath()
}

/// Two bubbles leaning apart, and the lens where they meet in a third colour.
///
/// The overlap is the point of the mark, so it is drawn rather than blended: on a pale
/// ground it goes dark and on the dark tile it goes cream, and no blend mode does both.
func mark(_ ctx: CGContext, in box: CGRect, left: CGColor, right: CGColor, over: CGColor) {
    let r = box.width * 0.361
    let cy = box.minY + box.height * 0.572
    let lx = box.minX + box.width * 0.361, rx = box.minX + box.width * 0.639

    bubble(ctx, cx: lx, cy: cy, r: r, lean: -1, left)
    bubble(ctx, cx: rx, cy: cy, r: r, lean: 1, right)

    ctx.saveGState()
    ctx.addEllipse(in: CGRect(x: lx - r, y: cy - r, width: r*2, height: r*2))
    ctx.clip()
    ctx.setFillColor(over)
    ctx.fillEllipse(in: CGRect(x: rx - r, y: cy - r, width: r*2, height: r*2))
    ctx.restoreGState()
}

/// The mark is 1.0 wide and this tall, tails included.
let markRatio: CGFloat = 0.843

func drawIcon(size s: CGFloat) -> CGImage {
    let ctx = CGContext(data: nil, width: Int(s), height: Int(s), bitsPerComponent: 8,
                        bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    ctx.setAllowsAntialiasing(true)

    let tile = CGRect(x: s*0.05, y: s*0.05, width: s*0.90, height: s*0.90)
    ctx.setFillColor(plum)
    ctx.addPath(CGPath(roundedRect: tile, cornerWidth: s*0.225, cornerHeight: s*0.225, transform: nil))
    ctx.fillPath()

    let w = s*0.62
    mark(ctx, in: CGRect(x: (s-w)/2, y: s*0.505 - w*markRatio/2, width: w, height: w*markRatio),
         left: lilac, right: salmon, over: cream)
    return ctx.makeImage()!
}

/// The mark on its own, transparent behind it, for a page or a README.
func drawMark(width w: CGFloat) -> CGImage {
    let h = w * markRatio
    let ctx = CGContext(data: nil, width: Int(w), height: Int(h), bitsPerComponent: 8,
                        bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    ctx.setAllowsAntialiasing(true)
    mark(ctx, in: CGRect(x: 0, y: 0, width: w, height: h),
         left: purple, right: salmon, over: plum)
    return ctx.makeImage()!
}

func write(_ img: CGImage, _ path: String) {
    try! NSBitmapImageRep(cgImage: img).representation(using: .png, properties: [:])!
        .write(to: URL(fileURLWithPath: path))
}

let out = CommandLine.arguments[1]
for size in [16, 32, 48, 64, 128, 256, 512, 1024] {
    write(drawIcon(size: CGFloat(size)), "\(out)/icon-\(size).png")
}
write(drawMark(width: 1024), "\(out)/mark.png")
print("done")
