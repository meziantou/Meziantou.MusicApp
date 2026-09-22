import MusicPlayerCore
import SwiftUI

struct SidebarView: View {
    private let model = AppModel.shared
    private let player = AppModel.shared.player
    @State private var playlistToRemoveFromOffline: PlaylistSummary?

    var body: some View {
        List(selection: selection) {
            Section("Playlists") {
                if model.playlists.isEmpty {
                    Text("No playlists")
                        .foregroundStyle(.secondary)
                }

                ForEach(model.playlists) { playlist in
                    PlaylistRow(
                        playlist: playlist,
                        isPlaying: playlist.id == player.playingPlaylistId && player.currentTrack != nil,
                        onToggleOffline: { toggleOffline(playlist) })
                        .tag(playlist.id)
                        .contextMenu {
                            if model.offlinePlaylistIds.contains(playlist.id) {
                                Button("Remove from Offline…") {
                                    playlistToRemoveFromOffline = playlist
                                }
                            } else {
                                Button("Download for Offline") {
                                    Task { await model.startPlaylistCaching(playlist.id) }
                                }
                                .disabled(!model.isOnline)
                            }
                        }
                }
            }

            if !model.invalidPlaylists.isEmpty {
                Section("Invalid Playlists") {
                    ForEach(model.invalidPlaylists, id: \.self) { invalid in
                        Label {
                            VStack(alignment: .leading) {
                                Text(invalid.fileName)
                                Text(invalid.errorMessage)
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                                    .lineLimit(2)
                            }
                        } icon: {
                            Image(systemName: "exclamationmark.circle.fill")
                                .foregroundStyle(.red)
                        }
                        .help(invalid.errorMessage)
                        .selectionDisabled()
                    }
                }
            }
        }
        .listStyle(.sidebar)
        .safeAreaInset(edge: .bottom, spacing: 0) {
            footer
        }
        .alert(
            "Remove from offline cache?",
            isPresented: Binding(get: { playlistToRemoveFromOffline != nil }, set: { if !$0 { playlistToRemoveFromOffline = nil } }),
            presenting: playlistToRemoveFromOffline
        ) { playlist in
            Button("Remove", role: .destructive) {
                Task { await model.stopPlaylistCaching(playlist.id) }
            }
            Button("Cancel", role: .cancel) {
            }
        } message: { playlist in
            Text("Are you sure you want to remove \"\(playlist.name)\" from offline cache? Its downloaded tracks will be deleted.")
        }
    }

    private var selection: Binding<String?> {
        Binding(
            get: { model.selectedPlaylistId },
            set: { id in
                if let id {
                    Task { await model.selectPlaylist(id) }
                }
            })
    }

    private func toggleOffline(_ playlist: PlaylistSummary) {
        if model.offlinePlaylistIds.contains(playlist.id) {
            playlistToRemoveFromOffline = playlist
        } else {
            Task { await model.startPlaylistCaching(playlist.id) }
        }
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 6) {
            if !model.isOnline {
                Label("Offline", systemImage: "wifi.slash")
                    .foregroundStyle(.orange)
            } else if model.networkType == .lowData {
                Label("Low Data Mode", systemImage: "antenna.radiowaves.left.and.right")
                    .foregroundStyle(.orange)
            }

            Link("Version \(AppInfo.version)", destination: AppInfo.releaseUrl)
                .foregroundStyle(.tertiary)
                .help("Open the release notes on GitHub")
        }
        .font(.caption)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
    }
}

private struct PlaylistRow: View {
    private let model = AppModel.shared
    let playlist: PlaylistSummary
    let isPlaying: Bool
    let onToggleOffline: () -> Void

    var body: some View {
        let isOffline = model.offlinePlaylistIds.contains(playlist.id)
        let progress = model.playlistDownloadProgress[playlist.id]

        HStack(spacing: 6) {
            VStack(alignment: .leading, spacing: 2) {
                Text(playlist.name)
                    .lineLimit(1)
                Text(details(isOffline: isOffline, progress: progress))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            Spacer(minLength: 0)

            if isPlaying {
                Image(systemName: "speaker.wave.2.fill")
                    .foregroundStyle(.tint)
                    .help("Playing from this playlist")
            }

            if model.isOnline || isOffline {
                Button(action: onToggleOffline) {
                    offlineIcon(isOffline: isOffline, progress: progress)
                        .frame(width: 18, height: 18)
                }
                .buttonStyle(.borderless)
                .help(offlineHelp(isOffline: isOffline, progress: progress))
            }
        }
        .padding(.vertical, 2)
    }

    private func details(isOffline: Bool, progress: PlaylistDownloadProgress?) -> String {
        var parts = ["\(playlist.trackCount) tracks", Formatting.duration(playlist.duration)]
        if model.settings.showPlaylistFileSize {
            parts.append(Formatting.bytes(playlist.size))
        }

        if isOffline, let progress, !progress.isComplete {
            parts.append("\(progress.cached)/\(progress.total) cached")
        }

        return parts.joined(separator: " • ")
    }

    @ViewBuilder
    private func offlineIcon(isOffline: Bool, progress: PlaylistDownloadProgress?) -> some View {
        if isOffline, let progress, !progress.isComplete {
            ProgressView(value: Double(progress.cached), total: Double(max(progress.total, 1)))
                .progressViewStyle(.circular)
                .controlSize(.small)
        } else if isOffline, progress?.isComplete == true {
            Image(systemName: "checkmark.circle.fill")
                .foregroundStyle(.green)
        } else if isOffline {
            Image(systemName: "arrow.down.circle.fill")
                .foregroundStyle(.tint)
        } else {
            Image(systemName: "arrow.down.circle")
                .foregroundStyle(.secondary)
        }
    }

    private func offlineHelp(isOffline: Bool, progress: PlaylistDownloadProgress?) -> String {
        guard isOffline else {
            return "Download for offline"
        }

        return progress?.isComplete == true ? "Remove from offline" : "Stop caching"
    }
}

enum AppInfo {
    static var version: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
    }

    /// The GitHub page of the running release, or the list of releases for development builds.
    static var releaseUrl: URL {
        UpdateChecker.currentVersion.map(UpdateChecker.releasePageUrl) ?? UpdateChecker.releasesPageUrl
    }
}
