import MusicPlayerCore
import SwiftUI

struct QueueView: View {
    private let model = AppModel.shared
    private let player = AppModel.shared.player

    var body: some View {
        let lookahead = Array(player.lookahead)
        let manualItems = lookahead.filter { $0.source == .manual }
        let playlistItems = lookahead.filter { $0.source == .playlist }

        Group {
            if player.currentTrack == nil && lookahead.isEmpty {
                ContentUnavailableView("Queue Is Empty", systemImage: "list.bullet", description: Text("Add tracks to play them next."))
            } else {
                List {
                    if let currentTrack = player.currentTrack {
                        Section("Now Playing") {
                            HStack(spacing: 10) {
                                PlayingIndicator(isPlaying: player.isPlaying, isAnimated: !model.settings.disablePlayingAnimation)
                                    .frame(width: 24)
                                    .onTapGesture {
                                        player.togglePlayPause()
                                    }
                                trackLabel(currentTrack, isCurrent: true)
                            }
                        }
                    }

                    if !manualItems.isEmpty {
                        Section("Next Up") {
                            rows(manualItems, startNumber: 1)
                        }
                    }

                    if !playlistItems.isEmpty {
                        Section("Next from: \(playlistName(playlistItems[0].playlistId))") {
                            rows(playlistItems, startNumber: manualItems.count + 1)
                        }
                    }
                }
            }
        }
        .navigationTitle("Playing Queue")
    }

    private func rows(_ items: [QueueItem], startNumber: Int) -> some View {
        ForEach(Array(items.enumerated()), id: \.element.id) { offset, item in
            HStack(spacing: 10) {
                Text("\(startNumber + offset)")
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .frame(width: 24, alignment: .trailing)

                trackLabel(item.track, isCurrent: false)

                Spacer(minLength: 0)

                Button {
                    player.removeFromQueue(itemId: item.id)
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.tertiary)
                }
                .buttonStyle(.borderless)
                .help("Remove from queue")
            }
            .contentShape(Rectangle())
            .onTapGesture(count: 2) {
                player.playQueueItem(itemId: item.id)
            }
            .contextMenu {
                Button("Play Now") {
                    player.playQueueItem(itemId: item.id)
                }
                Button("Remove from Queue") {
                    player.removeFromQueue(itemId: item.id)
                }
                Button("View Details") {
                    model.songDetailsTrack = item.track
                }
            }
        }
        .onMove { source, destination in
            guard let from = source.first else {
                return
            }

            let target = destination > from ? destination - 1 : destination
            guard items.indices.contains(target), target != from else {
                return
            }

            player.moveQueueItem(itemId: items[from].id, toItemId: items[target].id)
        }
    }

    private func trackLabel(_ track: TrackInfo, isCurrent: Bool) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(track.title)
                .foregroundStyle(isCurrent ? AnyShapeStyle(.tint) : AnyShapeStyle(.primary))
                .lineLimit(1)
            Text(track.artists ?? "Unknown Artist")
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
    }

    private func playlistName(_ playlistId: String) -> String {
        model.playlists.first { $0.id == playlistId }?.name ?? "Playlist"
    }
}
