// Run from project root: swift macos/generate-icon.swift
import AppKit
import CoreGraphics

func generateIcon(size: Int) -> NSImage {
    let img = NSImage(size: NSSize(width: size, height: size))
    img.lockFocus()
    let ctx = NSGraphicsContext.current!.cgContext
    let rect = CGRect(x: 0, y: 0, width: size, height: size)

    // Rounded rect background
    let radius = CGFloat(size) * 0.22
    let path = CGPath(roundedRect: rect.insetBy(dx: 1, dy: 1), cornerWidth: radius, cornerHeight: radius, transform: nil)
    ctx.addPath(path)
    ctx.clip()

    // Blue-to-indigo gradient
    let colors = [NSColor(red: 0.0, green: 0.48, blue: 1.0, alpha: 1).cgColor,
                  NSColor(red: 0.35, green: 0.24, blue: 0.9, alpha: 1).cgColor]
    let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(grad, start: CGPoint(x: 0, y: CGFloat(size)),
                           end: CGPoint(x: CGFloat(size), y: 0), options: [])

    // Arrow down symbol
    let symbolSize = CGFloat(size) * 0.52
    let symbolOrigin = CGPoint(x: (CGFloat(size) - symbolSize) / 2,
                               y: (CGFloat(size) - symbolSize) / 2)
    let symbolConf = NSImage.SymbolConfiguration(pointSize: symbolSize, weight: .medium)
    if let symbol = NSImage(systemSymbolName: "arrow.down.circle.fill", accessibilityDescription: nil)?
        .withSymbolConfiguration(symbolConf) {
        symbol.draw(in: NSRect(origin: symbolOrigin, size: NSSize(width: symbolSize, height: symbolSize)),
                    from: .zero, operation: .sourceOver, fraction: 0.95)
    }

    img.unlockFocus()
    return img
}

let sizes = [16, 32, 64, 128, 256, 512, 1024]
let outDir = "macos/AnyDownloader/Assets.xcassets/AppIcon.appiconset"
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

for size in sizes {
    let img = generateIcon(size: size)
    guard let tiff = img.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else {
        print("Failed to generate \(size)x\(size)")
        continue
    }
    try? png.write(to: URL(fileURLWithPath: "\(outDir)/icon_\(size)x\(size).png"))
    print("Generated \(size)x\(size)")
}
print("Done.")
