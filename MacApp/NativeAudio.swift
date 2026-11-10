import AppKit
import AVFoundation
import CoreMedia
import CoreGraphics
import ScreenCaptureKit

public typealias SamplesCallback = @convention(c) (Int64, UnsafePointer<Float>, Int32) -> Void
public typealias StatusCallback = @convention(c) (Int64, Int32, UnsafePointer<CChar>) -> Void

@MainActor private var activeCapture: AudioCapture?

@_cdecl("lt_start_audio")
public func startAudio(_ id: Int64, _ microphone: Int32, _ samples: @escaping SamplesCallback, _ status: @escaping StatusCallback) {
    Task { @MainActor in
        guard activeCapture == nil else {
            "Audio capture is already running.".withCString { status(id, -1, $0) }
            return
        }
        let capture = AudioCapture(id: id, samples: samples, status: status)
        activeCapture = capture
        do {
            if microphone == 1 { try await capture.startMicrophone() }
            else { try await capture.startSystemAudio() }
            guard activeCapture === capture, !capture.stopping else { return }
            capture.watchSleep()
            capture.report(1, "")
        } catch {
            await finish(capture, code: -1, message: error.localizedDescription)
        }
    }
}

@_cdecl("lt_stop_audio")
public func stopAudio(_ id: Int64) {
    Task { @MainActor in
        guard let capture = activeCapture, capture.id == id else { return }
        await finish(capture, code: 0, message: "")
    }
}

@MainActor private func finish(_ capture: AudioCapture, code: Int32, message: String) async {
    guard activeCapture === capture, !capture.stopping else { return }
    capture.stopping = true
    await capture.stop()
    activeCapture = nil
    capture.report(code, message)
}

private struct CaptureError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

private final class AudioCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    let id: Int64
    private let samples: SamplesCallback
    private let status: StatusCallback
    private let audioQueue = DispatchQueue(label: "live-translator.system-audio")
    private let outputFormat = AVAudioFormat(standardFormatWithSampleRate: 16_000, channels: 1)!
    private var converter: AVAudioConverter?
    @MainActor var stopping = false
    @MainActor private var engine: AVAudioEngine?
    @MainActor private var stream: SCStream?
    @MainActor private var sleepObserver: NSObjectProtocol?
    @MainActor private var deviceObserver: NSObjectProtocol?

    init(id: Int64, samples: @escaping SamplesCallback, status: @escaping StatusCallback) {
        self.id = id
        self.samples = samples
        self.status = status
    }

    @MainActor func startMicrophone() async throws {
        guard await AVCaptureDevice.requestAccess(for: .audio) else {
            throw CaptureError(message: "Allow microphone access in System Settings → Privacy & Security → Microphone.")
        }
        guard !stopping else { return }
        let source = AVAudioEngine()
        let input = source.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else {
            throw CaptureError(message: "No usable microphone input is available.")
        }
        input.installTap(onBus: 0, bufferSize: 2048, format: format) { [weak self] buffer, _ in self?.emit(buffer) }
        engine = source
        try source.start()
        deviceObserver = NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: source, queue: .main) { [weak self] _ in
            self?.fail("The audio input changed. Start audio again to use the new input.")
        }
    }

    @MainActor func startSystemAudio() async throws {
        guard CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess() else {
            throw CaptureError(message: "Allow screen and system audio access in System Settings → Privacy & Security, then reopen the app.")
        }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard !stopping else { return }
        guard let display = content.displays.first(where: { $0.displayID == CGMainDisplayID() }) ?? content.displays.first else {
            throw CaptureError(message: "No display is available for system audio capture.")
        }
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.capturesAudio = true
        configuration.excludesCurrentProcessAudio = true
        configuration.sampleRate = 16_000
        configuration.channelCount = 1
        configuration.width = 16
        configuration.height = 16
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        let source = SCStream(filter: filter, configuration: configuration, delegate: self)
        try source.addStreamOutput(self, type: .audio, sampleHandlerQueue: audioQueue)
        stream = source
        try await source.startCapture()
        // Stop can arrive while ScreenCaptureKit is starting.
        if stopping { try? await source.stopCapture() }
    }

    @MainActor func watchSleep() {
        sleepObserver = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            self?.fail("Audio stopped because the Mac went to sleep.")
        }
    }

    @MainActor func stop() async {
        if let sleepObserver { NSWorkspace.shared.notificationCenter.removeObserver(sleepObserver) }
        if let deviceObserver { NotificationCenter.default.removeObserver(deviceObserver) }
        if let engine {
            engine.inputNode.removeTap(onBus: 0)
            engine.stop()
        }
        if let stream { try? await stream.stopCapture() }
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) { fail(error.localizedDescription) }

    func stream(_ stream: SCStream, didOutputSampleBuffer buffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .audio, buffer.isValid,
              let description = buffer.formatDescription,
              let audio = CMAudioFormatDescriptionGetStreamBasicDescription(description),
              let format = AVAudioFormat(streamDescription: audio), buffer.numSamples > 0,
              let pcm = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(buffer.numSamples)) else { return }
        pcm.frameLength = AVAudioFrameCount(buffer.numSamples)
        if CMSampleBufferCopyPCMDataIntoAudioBufferList(buffer, at: 0, frameCount: Int32(buffer.numSamples), into: pcm.mutableAudioBufferList) == noErr {
            emit(pcm)
        }
    }

    private func emit(_ input: AVAudioPCMBuffer) {
        if input.format == outputFormat, let data = input.floatChannelData?[0] {
            samples(id, data, Int32(input.frameLength))
            return
        }
        if converter?.inputFormat != input.format { converter = AVAudioConverter(from: input.format, to: outputFormat) }
        guard let converter else { return }
        let capacity = AVAudioFrameCount(ceil(Double(input.frameLength) * 16_000 / input.format.sampleRate)) + 64
        guard let result = AVAudioPCMBuffer(pcmFormat: outputFormat, frameCapacity: capacity) else { return }
        var supplied = false
        let conversion = converter.convert(to: result, error: nil) { _, inputStatus in
            inputStatus.pointee = supplied ? .noDataNow : .haveData
            if supplied { return nil }
            supplied = true
            return input
        }
        if conversion != .error, result.frameLength > 0, let data = result.floatChannelData?[0] {
            samples(id, data, Int32(result.frameLength))
        }
    }

    func report(_ code: Int32, _ message: String) { message.withCString { status(id, code, $0) } }

    private func fail(_ message: String) {
        Task { @MainActor in await finish(self, code: -1, message: message) }
    }
}
