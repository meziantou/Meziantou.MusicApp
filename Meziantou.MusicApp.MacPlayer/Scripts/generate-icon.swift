// Generates Sources/MusicPlayerMac/Resources/AppIcon.icns from the web player's icon design
// (Meziantou.MusicApp.WebPlayer/public/pwa-512x512.svg), laid out on the macOS icon grid.
// Usage: swift Scripts/generate-icon.swift

import AppKit
import CoreGraphics

let background = CGColor(red: 0x1a / 255.0, green: 0x1a / 255.0, blue: 0x2e / 255.0, alpha: 1)
let backgroundTop = CGColor(red: 0x24 / 255.0, green: 0x24 / 255.0, blue: 0x40 / 255.0, alpha: 1)
let accent = CGColor(red: 0x7c / 255.0, green: 0x5c / 255.0, blue: 0xff / 255.0, alpha: 1)

/// Draws the icon in a 1024×1024 coordinate space (y pointing down, like the SVG).
func drawIcon(in context: CGContext) {
    // macOS icon grid: an 824×824 rounded tile centered in the 1024×1024 canvas
    let tile = CGRect(x: 100, y: 100, width: 824, height: 824)
    let tilePath = CGPath(roundedRect: tile, cornerWidth: 185, cornerHeight: 185, transform: nil)

    context.saveGState()
    context.setShadow(offset: CGSize(width: 0, height: 10), blur: 24, color: CGColor(gray: 0, alpha: 0.35))
    context.addPath(tilePath)
    context.setFillColor(background)
    context.fillPath()
    context.restoreGState()

    // Subtle top-to-bottom gradient for depth
    context.saveGState()
    context.addPath(tilePath)
    context.clip()
    let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: [backgroundTop, background] as CFArray, locations: [0, 1])!
    context.drawLinearGradient(gradient, start: CGPoint(x: 512, y: tile.minY), end: CGPoint(x: 512, y: tile.maxY), options: [])
    context.restoreGState()

    // The note from the 512×512 SVG, scaled to the tile
    let scale = tile.width / 512
    context.saveGState()
    context.translateBy(x: tile.minX, y: tile.minY)
    context.scaleBy(x: scale, y: scale)
    context.setFillColor(accent)
    context.fillEllipse(in: CGRect(x: 256 - 85, y: 310 - 85, width: 170, height: 170))
    context.addPath(CGPath(roundedRect: CGRect(x: 309, y: 96, width: 43, height: 214), cornerWidth: 10, cornerHeight: 10, transform: nil))
    context.addPath(CGPath(roundedRect: CGRect(x: 288, y: 96, width: 85, height: 43), cornerWidth: 10, cornerHeight: 10, transform: nil))
    context.fillPath()
    context.restoreGState()
}

func renderPNG(pixels: Int, to url: URL) throws {
    let context = CGContext(
        data: nil,
        width: pixels,
        height: pixels,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: CGColorSpace(name: CGColorSpace.sRGB)!,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    context.interpolationQuality = .high
    // Flip to a top-left origin and scale the 1024 design to the requested size
    context.translateBy(x: 0, y: CGFloat(pixels))
    context.scaleBy(x: CGFloat(pixels) / 1024, y: -CGFloat(pixels) / 1024)
    drawIcon(in: context)

    let representation = NSBitmapImageRep(cgImage: context.makeImage()!)
    try representation.representation(using: .png, properties: [:])!.write(to: url)
}

let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("AppIcon.iconset", isDirectory: true)
try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)

for points in [16, 32, 128, 256, 512] {
    try renderPNG(pixels: points, to: iconset.appendingPathComponent("icon_\(points)x\(points).png"))
    try renderPNG(pixels: points * 2, to: iconset.appendingPathComponent("icon_\(points)x\(points)@2x.png"))
}

let output = root.appendingPathComponent("Sources/MusicPlayerMac/Resources/AppIcon.icns")
let process = Process()
process.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
process.arguments = ["--convert", "icns", "--output", output.path, iconset.path]
try process.run()
process.waitUntilExit()
guard process.terminationStatus == 0 else {
    fatalError("iconutil failed")
}

try? FileManager.default.removeItem(at: iconset)
print("Generated \(output.path)")
