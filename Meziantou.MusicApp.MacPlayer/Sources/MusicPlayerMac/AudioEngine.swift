import AVFoundation
import CoreAudio
import MusicPlayerCore

/// Plays local audio files through AVAudioEngine: player → gain stage → main mixer → output.
///
/// The next track can be scheduled right after the current one on the same player node,
/// which gives gapless playback when both files share the same audio format.
@MainActor
final class AudioEngine {
    private struct Segment {
        let file: AVAudioFile
        let startFrame: AVAudioFramePosition
        let frameCount: AVAudioFrameCount
        let token: Int
    }

    /// Music does not need a low latency: a large I/O buffer wakes the audio thread up far less often
    /// (512 frames at 48 kHz is about 94 times per second, 4096 frames about 12). The HAL applies it to
    /// this process only, and limits it to what the device supports (for instance 960 frames on some Bluetooth headphones).
    private static let preferredIOBufferFrameSize: UInt32 = 4096

    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    /// Applies ReplayGain and the volume boost above 100% through its global gain.
    private let gainStage = AVAudioUnitEQ(numberOfBands: 0)
    private var connectedFormat: AVAudioFormat?

    private var current: Segment?
    private var next: Segment?
    /// Player sample time at which the current segment started (non-zero after a gapless transition).
    private var segmentBaseSampleTime: AVAudioFramePosition = 0
    private var tokenCounter = 0
    private var pausedPosition: TimeInterval?
    private var isSuspended = false
    /// The current file played to the end and nothing is scheduled anymore.
    private var isFinished = false
    private var configurationObserver: (any NSObjectProtocol)?

    private var replayGainLinear: Double = 1
    private var volume: Double = 1
    private var isMuted = false
    private var outputDeviceId: AudioDeviceID?

    /// The current file finished and nothing was scheduled after it.
    var onFinished: (() -> Void)?
    /// Playback continued gaplessly into the file passed to `scheduleNext`.
    var onAdvancedToNext: (() -> Void)?

    private(set) var isPlaying = false

    init() {
        engine.attach(player)
        engine.attach(gainStage)

        engine.connect(gainStage, to: engine.mainMixerNode, format: nil)
        engine.connect(player, to: gainStage, format: nil)

        configurationObserver = NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.handleConfigurationChange()
            }
        }
    }

    // MARK: State

    var hasFile: Bool {
        current != nil
    }

    var hasScheduledNext: Bool {
        next != nil
    }

    var duration: TimeInterval {
        guard let current else {
            return 0
        }

        return Double(current.file.length) / current.file.processingFormat.sampleRate
    }

    var currentTime: TimeInterval {
        guard let current else {
            return 0
        }

        if !isPlaying || isSuspended {
            return pausedPosition ?? Double(current.startFrame) / current.file.processingFormat.sampleRate
        }

        guard let nodeTime = player.lastRenderTime, nodeTime.isSampleTimeValid, let playerTime = player.playerTime(forNodeTime: nodeTime) else {
            return pausedPosition ?? Double(current.startFrame) / current.file.processingFormat.sampleRate
        }

        let playedFrames = max(0, playerTime.sampleTime - segmentBaseSampleTime)
        let time = Double(current.startFrame + playedFrames) / playerTime.sampleRate
        return min(time, duration)
    }

    // MARK: Transport

    /// Opens a file and schedules it from `startTime`. Throws when the file cannot be decoded.
    func load(url: URL, startTime: TimeInterval = 0) throws {
        let file = try AVAudioFile(forReading: url)
        stopPlayer()
        connect(format: file.processingFormat)
        schedule(file: file, from: startTime)
        pausedPosition = startTime
    }

    func play() throws {
        guard let current else {
            return
        }

        if isSuspended {
            isSuspended = false
            // A file that played to the end starts over, as when it is not suspended
            let position = isFinished ? 0 : pausedPosition ?? 0
            connect(format: current.file.processingFormat)
            schedule(file: current.file, from: position)
        } else if isFinished {
            schedule(file: current.file, from: 0)
        }

        if !engine.isRunning {
            applyIOBufferSize()
            engine.prepare()
            try engine.start()
        }

        player.play()
        isPlaying = true
    }

    func pause() {
        if isPlaying {
            pausedPosition = currentTime
            player.pause()
            isPlaying = false
        }

        // Nothing is audible: stop the audio device instead of mixing silence. `play()` restarts it and the
        // player resumes where it was. The resources are kept until `suspend()`.
        if engine.isRunning {
            engine.pause()
        }
    }

    /// Stops playback and forgets the current file.
    func stop() {
        stopPlayer()
        current = nil
        pausedPosition = nil
    }

    func seek(to time: TimeInterval) {
        guard let current else {
            return
        }

        let clamped = max(0, min(time, duration))
        pausedPosition = clamped
        if isSuspended {
            return
        }

        let wasPlaying = isPlaying
        stopPlayer()
        schedule(file: current.file, from: clamped)
        if wasPlaying {
            player.play()
            isPlaying = true
        }
    }

    /// Schedules a file right after the current one. Returns false when the formats differ,
    /// in which case the caller must load the file when the current one finishes.
    @discardableResult
    func scheduleNext(url: URL) -> Bool {
        guard current != nil, next == nil, !isSuspended, let file = try? AVAudioFile(forReading: url), file.processingFormat == connectedFormat, file.length > 0 else {
            return false
        }

        let segment = makeSegment(file: file, startFrame: 0)
        next = segment
        scheduleSegment(segment)
        return true
    }

    /// Removes the gaplessly scheduled file by rescheduling the current one from the current position.
    func cancelScheduledNext() {
        guard next != nil else {
            return
        }

        next = nil
        seek(to: currentTime)
    }

    /// Releases the audio hardware after a long pause. `play()` resumes transparently.
    func suspend() {
        guard !isPlaying, current != nil, !isSuspended else {
            return
        }

        pausedPosition = currentTime
        stopPlayer()
        engine.stop()
        isSuspended = true
    }

    // MARK: Output

    func setReplayGain(linear: Double) {
        replayGainLinear = linear
        applyGain()
    }

    func setVolume(_ volume: Double, muted: Bool) {
        self.volume = volume
        isMuted = muted
        applyGain()
    }

    /// Routes the audio to a specific output device, or to the system default when nil.
    func setOutputDevice(_ deviceId: AudioDeviceID?) {
        outputDeviceId = deviceId
        guard let audioUnit = engine.outputNode.audioUnit else {
            return
        }

        var id = deviceId ?? AudioOutputDevices.defaultOutputDeviceId() ?? 0
        guard id != 0 else {
            return
        }

        AudioUnitSetProperty(audioUnit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &id, UInt32(MemoryLayout<AudioDeviceID>.size))
        // Each device has its own buffer size
        applyIOBufferSize()
    }

    // MARK: Internals

    private func applyIOBufferSize() {
        guard let audioUnit = engine.outputNode.audioUnit else {
            return
        }

        var deviceId = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        guard AudioUnitGetProperty(audioUnit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &deviceId, &size) == noErr,
              deviceId != 0,
              let range = AudioOutputDevices.bufferFrameSizeRange(deviceId) else {
            return
        }

        var frameSize = min(max(Self.preferredIOBufferFrameSize, range.lowerBound), range.upperBound)
        var currentFrameSize: UInt32 = 0
        size = UInt32(MemoryLayout<UInt32>.size)
        if AudioUnitGetProperty(audioUnit, kAudioDevicePropertyBufferFrameSize, kAudioUnitScope_Global, 0, &currentFrameSize, &size) == noErr, currentFrameSize == frameSize {
            return
        }

        AudioUnitSetProperty(audioUnit, kAudioDevicePropertyBufferFrameSize, kAudioUnitScope_Global, 0, &frameSize, UInt32(MemoryLayout<UInt32>.size))
    }

    private func applyGain() {
        let volumeAmplitude = Volume.perceptualAmplitude(volume)
        if isMuted || volumeAmplitude <= 0 {
            engine.mainMixerNode.outputVolume = 0
            return
        }

        engine.mainMixerNode.outputVolume = 1
        let gainDb = 20 * log10(replayGainLinear * volumeAmplitude)
        gainStage.globalGain = Float(max(-96, min(24, gainDb)))
    }

    private func connect(format: AVAudioFormat) {
        guard connectedFormat != format else {
            return
        }

        // The gain stage cannot convert formats, so both of its sides use the file format; the mixer resamples
        engine.disconnectNodeOutput(player)
        engine.disconnectNodeOutput(gainStage)
        engine.connect(gainStage, to: engine.mainMixerNode, format: format)
        engine.connect(player, to: gainStage, format: format)
        connectedFormat = format
    }

    private func stopPlayer() {
        // Invalidate the tokens so completion callbacks of the stopped segments are ignored
        current = current.map { makeSegment(file: $0.file, startFrame: $0.startFrame) }
        next = nil
        player.stop()
        segmentBaseSampleTime = 0
        isPlaying = false
    }

    private func schedule(file: AVAudioFile, from time: TimeInterval) {
        let startFrame = AVAudioFramePosition(max(0, time) * file.processingFormat.sampleRate)
        let segment = makeSegment(file: file, startFrame: min(startFrame, max(0, file.length - 1)))
        current = segment
        next = nil
        segmentBaseSampleTime = 0
        isFinished = false
        scheduleSegment(segment)
    }

    private func makeSegment(file: AVAudioFile, startFrame: AVAudioFramePosition) -> Segment {
        tokenCounter += 1
        let frameCount = AVAudioFrameCount(max(0, file.length - startFrame))
        return Segment(file: file, startFrame: startFrame, frameCount: frameCount, token: tokenCounter)
    }

    private func scheduleSegment(_ segment: Segment) {
        guard segment.frameCount > 0 else {
            let token = segment.token
            Task { @MainActor in
                self.segmentFinished(token: token)
            }
            return
        }

        let token = segment.token
        player.scheduleSegment(segment.file, startingFrame: segment.startFrame, frameCount: segment.frameCount, at: nil, completionCallbackType: .dataPlayedBack) { @Sendable [weak self] _ in
            Task { @MainActor in
                self?.segmentFinished(token: token)
            }
        }
    }

    private func segmentFinished(token: Int) {
        guard let current, current.token == token else {
            return
        }

        if let next {
            segmentBaseSampleTime += AVAudioFramePosition(current.frameCount)
            self.current = next
            self.next = nil
            onAdvancedToNext?()
            return
        }

        isPlaying = false
        isFinished = true
        player.stop()
        pausedPosition = duration
        onFinished?()
    }

    private func handleConfigurationChange() {
        // The output device or its format changed: the engine stopped, restart where we were
        guard let current, !isSuspended else {
            return
        }

        let position = currentTime
        let wasPlaying = isPlaying
        stopPlayer()
        if let outputDeviceId {
            setOutputDevice(outputDeviceId)
        } else {
            // The default device may have changed (e.g. headphones connected)
            applyIOBufferSize()
        }

        schedule(file: current.file, from: position)
        pausedPosition = position
        if wasPlaying {
            try? play()
        }
    }
}
