// tools/frames-to-mp4.swift -- task 50: JPEG/PNG frames -> H.264 MP4 with
// AVFoundation (ffmpeg is not installed here). Compile once:
//   swiftc -O tools/frames-to-mp4.swift -o Logs/frames-to-mp4
// Usage:
//   frames-to-mp4 encode <frameDir> <out.mp4> <fps> <bitsPerSecond>
//        every *.jpg / *.png in frameDir, sorted by name; size from the first frame
//   frames-to-mp4 info <file.mp4>
//        prints "frames N  duration S  size WxH  fps F"
//   frames-to-mp4 frame <file.mp4> <seconds> <out.png>
//        writes the frame at that time (exact, no tolerance)
import AVFoundation
import CoreImage
import Foundation
import ImageIO
import UniformTypeIdentifiers

func fail(_ s: String) -> Never { FileHandle.standardError.write((s + "\n").data(using: .utf8)!); exit(1) }

func loadImage(_ url: URL) -> CGImage {
    guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
          let img = CGImageSourceCreateImageAtIndex(src, 0, nil) else { fail("cannot read \(url.path)") }
    return img
}

func encode(_ dir: String, _ out: String, _ fps: Int32, _ bps: Int) {
    let fm = FileManager.default
    let names = ((try? fm.contentsOfDirectory(atPath: dir)) ?? [])
        .filter { $0.hasSuffix(".jpg") || $0.hasSuffix(".png") }.sorted()
    if names.isEmpty { fail("no frames in \(dir)") }
    let first = loadImage(URL(fileURLWithPath: dir).appendingPathComponent(names[0]))
    let w = first.width, h = first.height
    let outURL = URL(fileURLWithPath: out)
    try? fm.removeItem(at: outURL)
    guard let writer = try? AVAssetWriter(outputURL: outURL, fileType: .mp4) else { fail("cannot open \(out)") }
    let settings: [String: Any] = [
        AVVideoCodecKey: AVVideoCodecType.h264,
        AVVideoWidthKey: w, AVVideoHeightKey: h,
        AVVideoCompressionPropertiesKey: [
            AVVideoAverageBitRateKey: bps,
            AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
            AVVideoMaxKeyFrameIntervalKey: Int(fps),
            AVVideoExpectedSourceFrameRateKey: Int(fps),
        ] as [String: Any],
    ]
    let input = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
    input.expectsMediaDataInRealTime = false
    let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: input, sourcePixelBufferAttributes: [
        kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
        kCVPixelBufferWidthKey as String: w, kCVPixelBufferHeightKey as String: h,
    ])
    writer.add(input)
    if !writer.startWriting() { fail("startWriting: \(writer.error?.localizedDescription ?? "?")") }
    writer.startSession(atSourceTime: .zero)
    let cs = CGColorSpace(name: CGColorSpace.sRGB)!
    for (i, n) in names.enumerated() {
        let img = i == 0 ? first : loadImage(URL(fileURLWithPath: dir).appendingPathComponent(n))
        if img.width != w || img.height != h { fail("\(n) is \(img.width)x\(img.height), expected \(w)x\(h)") }
        while !input.isReadyForMoreMediaData { Thread.sleep(forTimeInterval: 0.002) }
        guard let pool = adaptor.pixelBufferPool else { fail("no pixel buffer pool") }
        var pbOut: CVPixelBuffer?
        CVPixelBufferPoolCreatePixelBuffer(nil, pool, &pbOut)
        guard let pb = pbOut else { fail("no pixel buffer") }
        CVPixelBufferLockBaseAddress(pb, [])
        let ctx = CGContext(data: CVPixelBufferGetBaseAddress(pb), width: w, height: h, bitsPerComponent: 8,
                            bytesPerRow: CVPixelBufferGetBytesPerRow(pb), space: cs,
                            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        CVPixelBufferUnlockBaseAddress(pb, [])
        if !adaptor.append(pb, withPresentationTime: CMTime(value: CMTimeValue(i), timescale: fps)) {
            fail("append frame \(i): \(writer.error?.localizedDescription ?? "?")")
        }
    }
    input.markAsFinished()
    let sem = DispatchSemaphore(value: 0)
    writer.finishWriting { sem.signal() }
    sem.wait()
    if writer.status != .completed { fail("finish: \(writer.error?.localizedDescription ?? "?")") }
    print("wrote \(out): \(names.count) frames \(w)x\(h) @ \(fps) fps, \(bps / 1_000_000) Mbit/s")
}

func info(_ path: String) {
    let asset = AVURLAsset(url: URL(fileURLWithPath: path))
    guard let track = asset.tracks(withMediaType: .video).first else { fail("no video track in \(path)") }
    guard let reader = try? AVAssetReader(asset: asset) else { fail("cannot read \(path)") }
    let out = AVAssetReaderTrackOutput(track: track, outputSettings: nil)
    reader.add(out)
    reader.startReading()
    var n = 0
    while let sb = out.copyNextSampleBuffer() { if CMSampleBufferGetNumSamples(sb) > 0 { n += 1 } }
    let d = CMTimeGetSeconds(asset.duration)
    let sz = track.naturalSize
    print(String(format: "frames %d  duration %.3f  size %dx%d  fps %.2f", n, d, Int(sz.width), Int(sz.height), track.nominalFrameRate))
}

func frame(_ path: String, _ t: Double, _ out: String) {
    let asset = AVURLAsset(url: URL(fileURLWithPath: path))
    let gen = AVAssetImageGenerator(asset: asset)
    gen.requestedTimeToleranceBefore = .zero
    gen.requestedTimeToleranceAfter = .zero
    guard let img = try? gen.copyCGImage(at: CMTime(seconds: t, preferredTimescale: 600), actualTime: nil) else { fail("no frame at \(t)") }
    guard let dst = CGImageDestinationCreateWithURL(URL(fileURLWithPath: out) as CFURL, UTType.png.identifier as CFString, 1, nil) else { fail("cannot write \(out)") }
    CGImageDestinationAddImage(dst, img, nil)
    if !CGImageDestinationFinalize(dst) { fail("cannot write \(out)") }
    print("wrote \(out) (\(img.width)x\(img.height))")
}

let a = CommandLine.arguments
switch a.count > 1 ? a[1] : "" {
case "encode" where a.count == 6: encode(a[2], a[3], Int32(a[4])!, Int(a[5])!)
case "info" where a.count == 3: info(a[2])
case "frame" where a.count == 5: frame(a[2], Double(a[3])!, a[4])
default: fail("usage: frames-to-mp4 encode <dir> <out.mp4> <fps> <bps> | info <mp4> | frame <mp4> <sec> <out.png>")
}
