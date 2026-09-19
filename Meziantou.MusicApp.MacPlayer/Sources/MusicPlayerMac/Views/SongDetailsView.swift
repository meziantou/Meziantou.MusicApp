import MusicPlayerCore
import SwiftUI

struct SongDetailsView: View {
    private enum LyricsState {
        case loading
        case loaded(String?)
        case failed
    }

    private let model = AppModel.shared
    @Environment(\.dismiss) private var dismiss
    let track: TrackInfo
    @State private var lyrics = LyricsState.loading

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    HStack(spacing: 16) {
                        CoverImageView(model: model, trackId: track.id, size: 120, showsPlaceholderWhenHidden: true)
                        VStack(alignment: .leading, spacing: 4) {
                            Text(track.title)
                                .font(.title2.bold())
                                .textSelection(.enabled)
                            Text(track.artists ?? "Unknown Artist")
                                .font(.title3)
                            Text(track.album ?? "Unknown Album")
                                .foregroundStyle(.secondary)
                        }
                    }
                }

                Section("Track Information") {
                    LabeledContent("Duration", value: Formatting.duration(track.duration))
                    if let number = track.track {
                        LabeledContent("Track Number", value: "\(number)")
                    }

                    if let year = track.year {
                        LabeledContent("Year", value: String(year))
                    }

                    if let genre = track.genre {
                        LabeledContent("Genre", value: genre)
                    }

                    if let isrc = track.isrc {
                        LabeledContent("ISRC", value: isrc)
                    }
                }

                Section("File Information") {
                    LabeledContent("Relative Path") {
                        Text(track.path)
                            .textSelection(.enabled)
                            .multilineTextAlignment(.trailing)
                    }
                    LabeledContent("Added On", value: track.addedDate.flatMap(DateParsing.parse)?.formatted(date: .long, time: .shortened) ?? "Unknown")
                    LabeledContent("Size", value: Formatting.bytes(track.size))
                    LabeledContent("Bit Rate", value: track.bitRate.map { "\($0) kbps" } ?? "Unknown")
                    if let contentType = track.contentType {
                        LabeledContent("Format", value: contentType)
                    }

                    LabeledContent("Available Offline", value: model.cachedTrackIds.contains(track.id) ? "Yes" : "No")
                }

                if track.replayGainTrackGain != nil || track.replayGainAlbumGain != nil {
                    Section("ReplayGain") {
                        if let gain = track.replayGainTrackGain {
                            LabeledContent("Track Gain", value: String(format: "%.2f dB", gain))
                        }

                        if let peak = track.replayGainTrackPeak {
                            LabeledContent("Track Peak", value: String(format: "%.4f", peak))
                        }

                        if let gain = track.replayGainAlbumGain {
                            LabeledContent("Album Gain", value: String(format: "%.2f dB", gain))
                        }

                        if let peak = track.replayGainAlbumPeak {
                            LabeledContent("Album Peak", value: String(format: "%.4f", peak))
                        }
                    }
                }

                Section("Lyrics") {
                    switch lyrics {
                    case .loading:
                        ProgressView("Loading lyrics…")
                    case .failed:
                        Text("Failed to load lyrics")
                            .foregroundStyle(.red)
                    case let .loaded(text?) where !text.isEmpty:
                        Text(text)
                            .font(.body.monospaced())
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    case .loaded:
                        Text("No lyrics available")
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .formStyle(.grouped)

            HStack {
                Spacer()
                Button("Close") {
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
            .padding()
        }
        .frame(width: 560, height: 640)
        .task(id: track.id) {
            lyrics = .loading
            do {
                lyrics = .loaded(try await model.apiClient.songLyrics(songId: track.id).lyrics)
            } catch {
                lyrics = model.isOnline ? .failed : .loaded(nil)
            }
        }
    }
}
