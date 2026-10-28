import Foundation
import ScreenCaptureKit
import CoreMedia
import AudioToolbox

// Build on macOS 14+ with the Apple SDK. stdout carries only 16 kHz mono PCM16.
final class AudioSink: NSObject, SCStreamOutput {
    func stream(_ stream: SCStream, didOutputSampleBuffer sample: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .audio, sample.isValid,
              let description = CMSampleBufferGetFormatDescription(sample),
              let format = CMAudioFormatDescriptionGetStreamBasicDescription(description),
              let block = CMSampleBufferGetDataBuffer(sample) else { return }
        guard format.pointee.mSampleRate == 16000, format.pointee.mChannelsPerFrame == 1 else { return }
        var length = 0
        var pointer: UnsafeMutablePointer<Int8>?
        guard CMBlockBufferGetDataPointer(block, atOffset: 0, lengthAtOffsetOut: nil, totalLengthOut: &length, dataPointerOut: &pointer) == noErr,
              let pointer else { return }
        if format.pointee.mBitsPerChannel == 32 && format.pointee.mFormatFlags & kAudioFormatFlagIsFloat != 0 {
            let samples = UnsafeRawPointer(pointer).assumingMemoryBound(to: Float.self)
            var result = [Int16](repeating: 0, count: length / 4)
            for i in result.indices { result[i] = Int16(max(-1, min(1, samples[i])) * 32767).littleEndian }
            result.withUnsafeBytes { FileHandle.standardOutput.write(Data($0)) }
        } else if format.pointee.mBitsPerChannel == 16 {
            FileHandle.standardOutput.write(Data(bytes: pointer, count: length))
        }
    }
}

@main struct CaptureMain {
    static func main() async {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: false)
            guard let display = content.displays.first else { throw NSError(domain: "CatchLessons", code: 1, userInfo: [NSLocalizedDescriptionKey: "No display available"]) }
            let configuration = SCStreamConfiguration()
            configuration.width = 2; configuration.height = 2
            configuration.capturesAudio = true; configuration.excludesCurrentProcessAudio = true
            configuration.sampleRate = 16000; configuration.channelCount = 1
            let filter = SCContentFilter(display: display, excludingWindows: [])
            let stream = SCStream(filter: filter, configuration: configuration, delegate: nil)
            let sink = AudioSink()
            try stream.addStreamOutput(sink, type: .audio, sampleHandlerQueue: DispatchQueue(label: "caption.audio"))
            try await stream.startCapture()
            FileHandle.standardError.write(Data("CAPTION_READY\n".utf8))
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                DispatchQueue.global().async { _ = readLine(); continuation.resume() }
            }
            try await stream.stopCapture()
        } catch {
            FileHandle.standardError.write(Data("ERROR: Screen Recording 권한을 확인하세요. \(error.localizedDescription)\n".utf8))
            exit(1)
        }
    }
}
