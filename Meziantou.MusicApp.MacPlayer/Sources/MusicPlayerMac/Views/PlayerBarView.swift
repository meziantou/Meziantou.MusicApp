import MusicPlayerCore
import SwiftUI

struct PlayerBarView: View {
    private let model = AppModel.shared
    private let player = AppModel.shared.player

    var body: some View {
        HStack(spacing: 20) {
            trackInfo
                .frame(minWidth: 180, maxWidth: 300, alignment: .leading)

            VStack(spacing: 4) {
                transportButtons
                ProgressBar()
            }
            .frame(maxWidth: 640)
            .frame(maxWidth: .infinity)

            secondaryControls
                .frame(minWidth: 180, maxWidth: 300, alignment: .trailing)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.bar)
        .overlay(alignment: .top) {
            Divider()
        }
    }

    // MARK: Track info

    private var trackInfo: some View {
        HStack(spacing: 10) {
            Button {
                Task { await model.revealCurrentTrack() }
            } label: {
                CoverImageView(model: model, trackId: player.currentTrack?.id, size: 52, showsPlaceholderWhenHidden: true)
            }
            .buttonStyle(.plain)
            .help("Show the playing track in its playlist")
            .disabled(player.currentTrack == nil)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(player.currentTrack?.title ?? "No track selected")
                        .font(.headline)
                        .lineLimit(1)

                    if player.isLoadingTrack {
                        ProgressView()
                            .controlSize(.mini)
                    } else if let quality = player.currentQuality {
                        FormatBadge(quality: quality)
                    }
                }

                Text(player.currentTrack?.artists ?? "")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
    }

    // MARK: Transport

    private var transportButtons: some View {
        HStack(spacing: 18) {
            Button {
                player.setShuffle(!player.shuffleEnabled)
            } label: {
                Image(systemName: "shuffle")
                    .foregroundStyle(player.shuffleEnabled ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
            }
            .help(player.shuffleEnabled ? "Shuffle is on" : "Shuffle is off")

            Button {
                player.previous()
            } label: {
                Image(systemName: "backward.fill")
            }
            .help("Previous")

            Button {
                player.togglePlayPause()
            } label: {
                Image(systemName: player.isPlaying ? "pause.circle.fill" : "play.circle.fill")
                    .font(.system(size: 34))
            }
            .help(player.isPlaying ? "Pause" : "Play")
            .disabled(player.currentTrack == nil)

            Button {
                player.next()
            } label: {
                Image(systemName: "forward.fill")
            }
            .help("Next")

            Button {
                player.cycleRepeatMode()
            } label: {
                Image(systemName: player.repeatMode == .one ? "repeat.1" : "repeat")
                    .foregroundStyle(player.repeatMode != .off ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
            }
            .help("Repeat: \(player.repeatMode.label)")
        }
        .buttonStyle(.borderless)
        .font(.title3)
    }

    // MARK: Secondary controls

    private var secondaryControls: some View {
        HStack(spacing: 14) {
            OutputDeviceMenu()

            Button {
                model.isQueueVisible.toggle()
            } label: {
                Image(systemName: "list.bullet")
                    .foregroundStyle(model.isQueueVisible ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
                    .overlay(alignment: .topTrailing) {
                        let count = player.queue.items.count
                        if count > 0 {
                            Text(count > 99 ? "99+" : "\(count)")
                                .font(.system(size: 8, weight: .bold))
                                .padding(.horizontal, 3)
                                .padding(.vertical, 1)
                                .background(.tint, in: Capsule())
                                .foregroundStyle(.white)
                                .offset(x: 10, y: -8)
                        }
                    }
            }
            .buttonStyle(.borderless)
            .help("Playing queue")

            VolumeControl()
        }
        .font(.title3)
    }
}

private struct ProgressBar: View {
    private let player = AppModel.shared.player
    @AppStorage(DefaultsKeys.showRemainingTime) private var showRemainingTime = false
    @State private var dragValue: Double?

    var body: some View {
        let duration = max(player.duration, 0)
        let time = dragValue ?? min(player.currentTime, duration)

        HStack(spacing: 8) {
            Text(Formatting.duration(time))
                .frame(width: 48, alignment: .trailing)

            Slider(
                value: Binding(get: { time }, set: { dragValue = $0 }),
                in: 0...max(duration, 0.1)
            ) { editing in
                if !editing, let dragValue {
                    player.seek(to: dragValue)
                    self.dragValue = nil
                }
            }
            .controlSize(.small)
            .disabled(player.currentTrack == nil || duration <= 0)

            Button {
                showRemainingTime.toggle()
            } label: {
                Text(showRemainingTime && duration > 0 ? "-\(Formatting.duration(duration - time))" : Formatting.duration(duration))
                    .frame(width: 48, alignment: .leading)
            }
            .buttonStyle(.plain)
            .help(showRemainingTime ? "Show total time" : "Show remaining time")
        }
        .font(.caption.monospacedDigit())
        .foregroundStyle(.secondary)
    }
}

private struct VolumeControl: View {
    private let player = AppModel.shared.player

    var body: some View {
        HStack(spacing: 6) {
            Button {
                player.toggleMute()
            } label: {
                Image(systemName: volumeSymbol)
                    .frame(width: 22)
            }
            .buttonStyle(.borderless)
            .help(player.isMuted ? "Unmute" : "Mute")

            Slider(
                value: Binding(get: { player.isMuted ? 0 : player.volume }, set: { player.setVolume($0) }),
                in: 0...PlaybackConstants.maxVolume
            )
            .controlSize(.small)
            .frame(width: 90)
            .help("Volume: \(Int((player.volume * 100).rounded()))%")

            Text("\(Int(((player.isMuted ? 0 : player.volume) * 100).rounded()))%")
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
                .frame(width: 34, alignment: .trailing)
        }
    }

    private var volumeSymbol: String {
        if player.isMuted || player.volume == 0 {
            return "speaker.slash.fill"
        }

        return player.volume < 0.5 ? "speaker.wave.1.fill" : "speaker.wave.3.fill"
    }
}

private struct OutputDeviceMenu: View {
    private let player = AppModel.shared.player

    var body: some View {
        Menu {
            Button {
                player.selectOutputDevice(nil)
            } label: {
                if player.selectedOutputDeviceId == nil {
                    Label("System Default", systemImage: "checkmark")
                } else {
                    Text("System Default")
                }
            }

            Divider()

            ForEach(player.outputDevices) { device in
                Button {
                    player.selectOutputDevice(device.id)
                } label: {
                    if player.selectedOutputDeviceId == device.id {
                        Label(device.name, systemImage: "checkmark")
                    } else {
                        Label(device.name, systemImage: device.isAirPlay ? "airplayaudio" : "hifispeaker")
                    }
                }
            }
        } label: {
            Image(systemName: "airplayaudio")
                .foregroundStyle(isAirPlayActive ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .help("Audio output (AirPlay)")
        .onTapGesture {
            player.refreshOutputDevices()
        }
        .task {
            player.refreshOutputDevices()
        }
    }

    private var isAirPlayActive: Bool {
        guard let id = player.selectedOutputDeviceId else {
            return false
        }

        return player.outputDevices.first { $0.id == id }?.isAirPlay == true
    }
}

struct FormatBadge: View {
    let quality: StreamingQuality

    var body: some View {
        Text(quality.badge)
            .font(.system(size: 9, weight: .bold))
            .padding(.horizontal, 5)
            .padding(.vertical, 2)
            .foregroundStyle(.white)
            .background(color, in: RoundedRectangle(cornerRadius: 4))
            .fixedSize()
    }

    private var color: Color {
        switch quality.format {
        case .flac: Color(red: 0, green: 0.74, blue: 0.83)
        case .mp3: .orange
        case .ogg: Color(red: 1, green: 0.34, blue: 0.13)
        case .opus: .pink
        case .m4a: .blue
        case .raw: .gray
        }
    }
}
