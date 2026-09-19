import MusicPlayerCore
import SwiftUI

/// Cover art for a track, loaded from the cache or the server.
struct CoverImageView: View {
    let model: AppModel
    let trackId: String?
    let size: CGFloat
    var showsPlaceholderWhenHidden = false

    /// The model is passed explicitly because the view is also used in table cells,
    /// which can be built outside of the SwiftUI environment chain.
    init(model: AppModel, trackId: String?, size: CGFloat, showsPlaceholderWhenHidden: Bool = false) {
        self.model = model
        self.trackId = trackId
        self.size = size
        self.showsPlaceholderWhenHidden = showsPlaceholderWhenHidden
    }

    @Environment(\.displayScale) private var displayScale
    @State private var image: NSImage?

    var body: some View {
        if model.settings.hideCoverArt && !showsPlaceholderWhenHidden {
            EmptyView()
        } else {
            content
                .frame(width: size, height: size)
                .clipShape(RoundedRectangle(cornerRadius: max(3, size / 12)))
                .task(id: loadKey) {
                    await load()
                }
        }
    }

    @ViewBuilder
    private var content: some View {
        if let image {
            Image(nsImage: image)
                .resizable()
                .aspectRatio(contentMode: .fill)
        } else {
            ZStack {
                Rectangle()
                    .fill(.quaternary)
                Image(systemName: "music.note")
                    .font(.system(size: size * 0.4))
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var loadKey: String {
        "\(trackId ?? ""):\(model.settings.hideCoverArt):\(pixelSize)"
    }

    /// Covers are decoded at the size they are displayed to keep memory low.
    private var pixelSize: Int {
        min(CoverLoader.downloadSize, Int((size * max(displayScale, 1)).rounded(.up)))
    }

    private func load() async {
        guard let trackId, !trackId.isEmpty, !model.settings.hideCoverArt else {
            image = nil
            return
        }

        if let cached = model.coverLoader.cachedImage(trackId: trackId, pixelSize: pixelSize) {
            image = cached
            return
        }

        image = nil
        let loaded = await model.coverLoader.image(trackId: trackId, pixelSize: pixelSize, isTrackCached: model.cachedTrackIds.contains(trackId))
        if !Task.isCancelled {
            image = loaded
        }
    }
}

/// Animated bars shown next to the playing track.
struct PlayingIndicator: View {
    let isPlaying: Bool
    let isAnimated: Bool

    var body: some View {
        if isPlaying && isAnimated {
            TimelineView(.animation(minimumInterval: 1 / 12)) { context in
                bars(phase: context.date.timeIntervalSinceReferenceDate)
            }
        } else {
            Image(systemName: isPlaying ? "speaker.wave.2.fill" : "speaker.fill")
                .foregroundStyle(.tint)
        }
    }

    private func bars(phase: TimeInterval) -> some View {
        HStack(alignment: .bottom, spacing: 2) {
            ForEach(0..<3) { index in
                let value = (sin(phase * 7 + Double(index) * 1.9) + 1) / 2
                RoundedRectangle(cornerRadius: 1)
                    .fill(.tint)
                    .frame(width: 3, height: 3 + 9 * value)
            }
        }
        .frame(width: 14, height: 12, alignment: .bottom)
    }
}
