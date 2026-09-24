import MusicPlayerCore
import SwiftUI

struct MobileContentView: View {
    @Environment(\.scenePhase) private var scenePhase
    @Bindable var model: MobileAppModel
    @State private var searchText = ""

    var body: some View {
        NavigationSplitView {
            List(selection: $model.selectedPlaylistId) {
                ForEach(model.playlists) { playlist in
                    HStack {
                        Label(playlist.name, systemImage: "music.note.list")
                        Spacer()
                        if model.offlinePlaylistIds.contains(playlist.id) {
                            Image(systemName: "arrow.down.circle.fill")
                                .foregroundStyle(.green)
                        }
                    }
                    .tag(playlist.id)
                    .contextMenu {
                        Button(model.offlinePlaylistIds.contains(playlist.id) ? "Remove Offline Download" : "Download for Offline") {
                            Task { await model.toggleOffline(playlist) }
                        }
                    }
                }
            }
            .navigationTitle("Meziantou Music")
            .onChange(of: model.selectedPlaylistId) { _, id in
                guard let id, let playlist = model.playlists.first(where: { $0.id == id }) else {
                    return
                }

                Task { await model.select(playlist) }
            }
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        model.isShowingSettings = true
                    } label: {
                        Label("Settings", systemImage: "gear")
                    }
                }
            }
        } detail: {
            tracks
        }
        .safeAreaInset(edge: .bottom) {
            MobilePlayerBar(player: model.player, onToggleShuffle: model.toggleShuffle, onCycleRepeatMode: model.cycleRepeatMode)
        }
        .task {
            await model.initialize()
            model.isShowingSettings = model.settings.serverUrl.isEmpty
        }
        .onChange(of: scenePhase) {
            if scenePhase == .active {
                model.applicationDidBecomeActive()
            } else {
                model.savePlayback()
            }
        }
        .sheet(isPresented: $model.isShowingSettings) {
            MobileSettingsView(model: model)
        }
        .alert("Meziantou Music", isPresented: Binding(get: { model.message != nil }, set: { if !$0 { model.message = nil } })) {
            Button("OK") {
                model.message = nil
            }
        } message: {
            Text(model.message ?? "")
        }
    }

    private var tracks: some View {
        List(filteredTracks) { track in
            TrackRow(
                track: track,
                isCached: model.cachedTrackIds.contains(track.id),
                isCurrent: model.player.currentTrack?.id == track.id,
                onPlay: { model.play(track) })
        }
        .navigationTitle(model.playlists.first(where: { $0.id == model.selectedPlaylistId })?.name ?? "Select a Playlist")
        .searchable(text: $searchText, prompt: "Search tracks")
        .overlay {
            if model.selectedPlaylistId == nil {
                ContentUnavailableView("No Playlist Selected", systemImage: "music.note.list")
            }
        }
    }

    private var filteredTracks: [TrackInfo] {
        guard !searchText.isEmpty else {
            return model.tracks
        }

        return model.tracks.filter {
            [$0.title, $0.artists, $0.album, $0.isrc]
                .compactMap { $0 }
                .joined(separator: " ")
                .localizedCaseInsensitiveContains(searchText)
        }
    }
}

private struct TrackRow: View {
    let track: TrackInfo
    let isCached: Bool
    let isCurrent: Bool
    let onPlay: () -> Void

    var body: some View {
        Button(action: onPlay) {
            HStack {
                VStack(alignment: .leading) {
                    Text(track.title)
                    Text(subtitle)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                if isCached {
                    Image(systemName: "arrow.down.circle.fill")
                        .foregroundStyle(.green)
                }
                Text(formatDuration(track.duration))
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
        }
        .foregroundStyle(isCurrent ? AnyShapeStyle(.tint) : AnyShapeStyle(.primary))
    }

    private var subtitle: String {
        [track.artists, track.album].compactMap { $0 }.joined(separator: " — ")
    }
}

private struct MobilePlayerBar: View {
    @Bindable var player: MobilePlayerController
    let onToggleShuffle: () -> Void
    let onCycleRepeatMode: () -> Void
    @State private var isSeeking = false

    private var repeatSymbolName: String {
        player.repeatMode == .one ? "repeat.1" : "repeat"
    }

    var body: some View {
        VStack(spacing: 8) {
            HStack {
                VStack(alignment: .leading) {
                    Text(player.currentTrack?.title ?? "No track selected")
                        .lineLimit(1)
                    Text(player.currentTrack?.artists ?? "")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Spacer()
                Button { onToggleShuffle() } label: {
                    Image(systemName: "shuffle")
                        .foregroundStyle(player.shuffleEnabled ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
                }
                Button { player.previous() } label: { Image(systemName: "backward.fill") }
                Button { player.togglePlayPause() } label: {
                    Image(systemName: player.isPlaying ? "pause.circle.fill" : "play.circle.fill")
                        .font(.title)
                }
                .disabled(player.currentTrack == nil)
                Button { player.next() } label: { Image(systemName: "forward.fill") }
                Button { onCycleRepeatMode() } label: {
                    Image(systemName: repeatSymbolName)
                        .foregroundStyle(player.repeatMode == .off ? AnyShapeStyle(.secondary) : AnyShapeStyle(.tint))
                }
            }
            HStack {
                Text(formatDuration(player.currentTime))
                Slider(value: Binding(get: { player.currentTime }, set: { player.seek(to: $0) }), in: 0...max(0.1, player.duration))
                    .disabled(player.currentTrack == nil)
                Text(formatDuration(player.duration))
            }
            .font(.caption.monospacedDigit())
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
        .background(.bar)
    }
}

private struct MobileSettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @Bindable var model: MobileAppModel
    @State private var draft = AppSettings()

    var body: some View {
        NavigationStack {
            Form {
                Section("Server") {
                    TextField("Server URL", text: $draft.serverUrl, prompt: Text("https://your-server.example"))
                        .textInputAutocapitalization(.never)
                        .keyboardType(.URL)
                }
                Section("Streaming quality") {
                    qualityPicker("Normal quality", $draft.normalQuality)
                    qualityPicker("Download quality", $draft.downloadQuality)
                    Toggle("Prevent streaming on Low Data Mode", isOn: $draft.preventDownloadOnLowData)
                }
                Section("Playback") {
                    Picker("ReplayGain", selection: $draft.replayGainMode) {
                        ForEach(ReplayGainMode.allCases, id: \.self) { mode in
                            Text(mode.rawValue.capitalized).tag(mode)
                        }
                    }
                }
            }
            .navigationTitle("Settings")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        Task {
                            await model.saveSettings(draft)
                            dismiss()
                        }
                    }
                }
            }
            .onAppear {
                draft = model.settings
            }
        }
    }

    private func qualityPicker(_ title: String, _ selection: Binding<StreamingQuality>) -> some View {
        Picker(title, selection: selection) {
            ForEach(QualityOption.all) { option in
                Text(option.label).tag(option.quality)
            }
        }
    }
}

private func formatDuration(_ duration: TimeInterval) -> String {
    guard duration.isFinite, duration >= 0 else {
        return "0:00"
    }

    let total = Int(duration.rounded(.down))
    return String(format: "%d:%02d", total / 60, total % 60)
}
