#!/usr/bin/env swift

import Cocoa

// Output directories
let baseDir = CommandLine.arguments.count > 1
    ? CommandLine.arguments[1]
    : "../../Grimly/Resources/Assets.xcassets"
let appIconDir = "\(baseDir)/AppIcon.appiconset"
let menuBarDir = "\(baseDir)"

// macOS app icon sizes: (points, scale) -> pixels
let appIconSizes: [(points: Int, scale: Int)] = [
    (16, 1), (16, 2),
    (32, 1), (32, 2),
    (128, 1), (128, 2),
    (256, 1), (256, 2),
    (512, 1), (512, 2),
]

func drawFace(context g: CGContext, size: Int) {
    let s = CGFloat(size) / 32.0

    // Warm pink/salmon background
    g.setFillColor(CGColor(red: 210/255, green: 140/255, blue: 130/255, alpha: 1))
    g.fill(CGRect(x: 0, y: 0, width: size, height: size))

    let dark = CGColor(red: 40/255, green: 30/255, blue: 30/255, alpha: 1)
    let skin = CGColor(red: 240/255, green: 210/255, blue: 180/255, alpha: 1)

    let outlineWidth = max(2.2 * s, 1.5)
    let thinWidth = max(1.5 * s, 1.0)

    g.setLineCap(.round)
    g.setLineJoin(.round)

    // Note: CoreGraphics Y is flipped vs GDI+ — origin at bottom-left
    // We'll flip the context to match the Windows coordinate system (origin top-left)
    g.translateBy(x: 0, y: CGFloat(size))
    g.scaleBy(x: 1, y: -1)

    // Face oval
    let faceX = 14 * s, faceY = 15 * s
    let faceW = 16 * s, faceH = 18 * s
    let faceRect = CGRect(x: faceX - faceW/2, y: faceY - faceH/2, width: faceW, height: faceH)

    g.setFillColor(skin)
    g.fillEllipse(in: faceRect)
    g.setStrokeColor(dark)
    g.setLineWidth(outlineWidth)
    g.strokeEllipse(in: faceRect)

    // Hair - slicked back with pompadour
    // Back of hair
    g.setFillColor(dark)
    g.beginPath()
    g.addArc(center: CGPoint(x: 15 * s, y: 12 * s), radius: 9 * s,
             startAngle: .pi, endAngle: 0, clockwise: false)
    g.addLine(to: CGPoint(x: 24 * s, y: 12 * s))
    g.addLine(to: CGPoint(x: 22 * s, y: 8 * s))
    g.addLine(to: CGPoint(x: 20 * s, y: 6 * s))
    g.closePath()
    g.fillPath()

    // The signature tall hair tuft/pompadour
    g.beginPath()
    let tuftPoints: [CGPoint] = [
        CGPoint(x: 12 * s, y: 8 * s),
        CGPoint(x: 13 * s, y: 4 * s),
        CGPoint(x: 15 * s, y: 1.5 * s),
        CGPoint(x: 17 * s, y: 0.5 * s),
        CGPoint(x: 18 * s, y: 2 * s),
        CGPoint(x: 19 * s, y: 4 * s),
        CGPoint(x: 20 * s, y: 6 * s),
    ]
    // Approximate the GDI+ curve with a smooth path through points
    g.move(to: tuftPoints[0])
    // Use quadratic curves to approximate the smooth curve
    for i in 1..<tuftPoints.count {
        let prev = tuftPoints[i - 1]
        let curr = tuftPoints[i]
        let midX = (prev.x + curr.x) / 2
        let midY = (prev.y + curr.y) / 2
        if i == 1 {
            g.addLine(to: CGPoint(x: midX, y: midY))
        }
        g.addQuadCurve(to: curr, control: CGPoint(x: midX, y: midY))
    }
    g.addLine(to: CGPoint(x: 12 * s, y: 8 * s))
    g.closePath()
    g.fillPath()

    // Eyes - simple dots
    let eyeR = 1.2 * s
    g.setFillColor(dark)
    g.fillEllipse(in: CGRect(x: 11 * s - eyeR, y: 14 * s - eyeR, width: eyeR * 2, height: eyeR * 2))
    g.fillEllipse(in: CGRect(x: 17 * s - eyeR, y: 13.5 * s - eyeR, width: eyeR * 2, height: eyeR * 2))

    // Eyebrows
    g.setStrokeColor(dark)
    g.setLineWidth(thinWidth)

    // Left eyebrow
    g.beginPath()
    g.addArc(center: CGPoint(x: 11 * s, y: 12.5 * s), radius: 2.5 * s,
             startAngle: .pi + 0.35, endAngle: .pi + 0.35 + 1.4, clockwise: false)
    g.strokePath()

    // Right eyebrow
    g.beginPath()
    g.addArc(center: CGPoint(x: 17.5 * s, y: 12 * s), radius: 2.5 * s,
             startAngle: .pi + 1.05, endAngle: .pi + 1.05 + 1.4, clockwise: false)
    g.strokePath()

    // Nose
    g.beginPath()
    g.move(to: CGPoint(x: 14 * s, y: 15 * s))
    g.addLine(to: CGPoint(x: 13 * s, y: 18 * s))
    g.strokePath()

    // Mouth - grinning
    g.beginPath()
    g.addArc(center: CGPoint(x: 15 * s, y: 21 * s), radius: 5 * s,
             startAngle: -0.17, endAngle: -0.17 + 2.09, clockwise: false)
    g.strokePath()

    // Ear
    g.beginPath()
    g.addArc(center: CGPoint(x: 23 * s, y: 15 * s), radius: 2 * s,
             startAngle: -.pi/2 - 0.35, endAngle: -.pi/2 - 0.35 + 2.79, clockwise: false)
    g.strokePath()

    // Neck/shoulders
    g.setLineWidth(outlineWidth)
    g.beginPath()
    g.move(to: CGPoint(x: 10 * s, y: 24 * s))
    g.addLine(to: CGPoint(x: 8 * s, y: 30 * s))
    g.strokePath()

    g.beginPath()
    g.move(to: CGPoint(x: 18 * s, y: 24 * s))
    g.addLine(to: CGPoint(x: 22 * s, y: 30 * s))
    g.strokePath()

    // Collar hint
    if size >= 32 {
        g.setStrokeColor(CGColor(red: 180/255, green: 160/255, blue: 80/255, alpha: 1))
        g.setLineWidth(max(1.5 * s, 1))
        g.beginPath()
        g.move(to: CGPoint(x: 8 * s, y: 28 * s))
        g.addLine(to: CGPoint(x: 12 * s, y: 25 * s))
        g.strokePath()

        g.beginPath()
        g.move(to: CGPoint(x: 22 * s, y: 28 * s))
        g.addLine(to: CGPoint(x: 18 * s, y: 25 * s))
        g.strokePath()
    }
}

func generatePNG(pixelSize: Int, drawFunc: (CGContext, Int) -> Void) -> Data? {
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    guard let context = CGContext(
        data: nil,
        width: pixelSize,
        height: pixelSize,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else { return nil }

    drawFunc(context, pixelSize)

    guard let cgImage = context.makeImage() else { return nil }
    let rep = NSBitmapImageRep(cgImage: cgImage)
    return rep.representation(using: .png, properties: [:])
}

func savePNG(pixelSize: Int, to path: String, drawFunc: (CGContext, Int) -> Void) {
    guard let data = generatePNG(pixelSize: pixelSize, drawFunc: drawFunc) else {
        print("Failed to create PNG for \(path)")
        return
    }
    do {
        try data.write(to: URL(fileURLWithPath: path))
        print("Generated: \(path) (\(pixelSize)x\(pixelSize)px)")
    } catch {
        print("Error writing \(path): \(error)")
    }
}

// Generate menu bar icon (template image - 18x18 @1x, 36x36 @2x)
func drawMenuBarIcon(context g: CGContext, size: Int) {
    let s = CGFloat(size) / 18.0

    g.translateBy(x: 0, y: CGFloat(size))
    g.scaleBy(x: 1, y: -1)

    let dark = CGColor(red: 0, green: 0, blue: 0, alpha: 1)

    g.setLineCap(.round)
    g.setLineJoin(.round)
    g.setStrokeColor(dark)
    g.setFillColor(dark)

    let lineW = max(1.3 * s, 1)
    g.setLineWidth(lineW)

    // Simplified face silhouette for menu bar
    // Head circle
    let headR = 5 * s
    let cx = 9 * s, cy = 8 * s
    g.strokeEllipse(in: CGRect(x: cx - headR, y: cy - headR, width: headR * 2, height: headR * 2))

    // Pompadour tuft
    g.beginPath()
    g.move(to: CGPoint(x: 6 * s, y: 4 * s))
    g.addQuadCurve(to: CGPoint(x: 10 * s, y: 1 * s), control: CGPoint(x: 7.5 * s, y: 1.5 * s))
    g.addQuadCurve(to: CGPoint(x: 12 * s, y: 3.5 * s), control: CGPoint(x: 11.5 * s, y: 1 * s))
    g.strokePath()

    // Eyes
    let eyeR = 0.8 * s
    g.fillEllipse(in: CGRect(x: 7 * s - eyeR, y: 7.5 * s - eyeR, width: eyeR * 2, height: eyeR * 2))
    g.fillEllipse(in: CGRect(x: 11 * s - eyeR, y: 7.5 * s - eyeR, width: eyeR * 2, height: eyeR * 2))

    // Smile
    g.beginPath()
    g.addArc(center: CGPoint(x: 9 * s, y: 10 * s), radius: 2.5 * s,
             startAngle: 0.2, endAngle: .pi - 0.2, clockwise: false)
    g.strokePath()

    // Shoulders
    g.beginPath()
    g.move(to: CGPoint(x: 4 * s, y: 13 * s))
    g.addQuadCurve(to: CGPoint(x: 14 * s, y: 13 * s), control: CGPoint(x: 9 * s, y: 17 * s))
    g.strokePath()
}

// MARK: - Main

// Create directories
try? FileManager.default.createDirectory(atPath: appIconDir, withIntermediateDirectories: true)

// Generate app icons
var contentsImages: [[String: Any]] = []

for entry in appIconSizes {
    let pixels = entry.points * entry.scale
    let filename = "icon_\(entry.points)x\(entry.points)@\(entry.scale)x.png"
    savePNG(pixelSize: pixels, to: "\(appIconDir)/\(filename)", drawFunc: drawFace)

    contentsImages.append([
        "filename": filename,
        "idiom": "mac",
        "scale": "\(entry.scale)x",
        "size": "\(entry.points)x\(entry.points)"
    ])
}

// Write Contents.json for AppIcon
let contentsJSON: [String: Any] = [
    "images": contentsImages,
    "info": ["author": "xcode", "version": 1]
]
let jsonData = try! JSONSerialization.data(withJSONObject: contentsJSON, options: .prettyPrinted)
try! jsonData.write(to: URL(fileURLWithPath: "\(appIconDir)/Contents.json"))
print("Generated: \(appIconDir)/Contents.json")

// Generate menu bar icons
let menuBarDir2 = "\(baseDir)/MenuBarIcon.imageset"
try? FileManager.default.createDirectory(atPath: menuBarDir2, withIntermediateDirectories: true)
savePNG(pixelSize: 18, to: "\(menuBarDir2)/MenuBarIcon.png", drawFunc: drawMenuBarIcon)
savePNG(pixelSize: 36, to: "\(menuBarDir2)/MenuBarIcon@2x.png", drawFunc: drawMenuBarIcon)

print("\nDone! All icons generated.")
