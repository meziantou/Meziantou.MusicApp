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
            // Animated by Core Animation in the render server: a TimelineView re-evaluated the view
            // 12 times per second, which used about 8% CPU while the playing track was on screen
            PlayingBars()
                .frame(width: 14, height: 12)
        } else {
            Image(systemName: isPlaying ? "speaker.wave.2.fill" : "speaker.fill")
                .foregroundStyle(.tint)
        }
    }
}

private struct PlayingBars: NSViewRepresentable {
    func makeNSView(context: Context) -> PlayingBarsView {
        PlayingBarsView()
    }

    func updateNSView(_ nsView: PlayingBarsView, context: Context) {
    }
}

private final class PlayingBarsView: NSView {
    private static let barCount = 3
    private static let barWidth: CGFloat = 3
    private static let barSpacing: CGFloat = 2
    private static let minBarHeight: CGFloat = 3
    private static let maxBarHeight: CGFloat = 12
    /// A bar goes up and down in about 0.9 s, each one out of phase with the previous one.
    private static let halfPeriod: CFTimeInterval = 0.45
    private static let phaseShift: CFTimeInterval = 0.27

    private var bars: [CALayer] = []

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        for _ in 0..<Self.barCount {
            let bar = CALayer()
            bar.anchorPoint = .zero
            bar.cornerRadius = 1
            layer?.addSublayer(bar)
            bars.append(bar)
        }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override var intrinsicContentSize: NSSize {
        NSSize(width: CGFloat(Self.barCount) * (Self.barWidth + Self.barSpacing) - Self.barSpacing, height: Self.maxBarHeight)
    }

    /// Clicks go to the SwiftUI gestures attached to the indicator.
    override func hitTest(_ point: NSPoint) -> NSView? {
        nil
    }

    override func layout() {
        super.layout()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        for (index, bar) in bars.enumerated() {
            bar.bounds = CGRect(x: 0, y: 0, width: Self.barWidth, height: Self.minBarHeight)
            bar.position = CGPoint(x: CGFloat(index) * (Self.barWidth + Self.barSpacing), y: 0)
        }

        CATransaction.commit()
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil {
            updateColors()
            startAnimations()
        }
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateColors()
    }

    private func updateColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            let color = NSColor.controlAccentColor.cgColor
            for bar in bars {
                bar.backgroundColor = color
            }
        }
    }

    private func startAnimations() {
        for (index, bar) in bars.enumerated() {
            let animation = CABasicAnimation(keyPath: "bounds.size.height")
            animation.fromValue = Self.minBarHeight
            animation.toValue = Self.maxBarHeight
            animation.duration = Self.halfPeriod
            animation.autoreverses = true
            animation.repeatCount = .infinity
            animation.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            animation.timeOffset = Double(index) * Self.phaseShift
            bar.add(animation, forKey: "height")
        }
    }
}
