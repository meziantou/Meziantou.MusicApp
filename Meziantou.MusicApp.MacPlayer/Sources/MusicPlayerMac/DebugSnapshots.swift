#if DEBUG
import AppKit

/// Development aid: when `MEZIANTOU_MUSIC_SNAPSHOT_PATH` is set, periodically renders the visible
/// windows to `<path>-<index>.png` so the UI can be inspected without screen recording permission.
@MainActor
enum DebugSnapshots {
    private static var timer: Timer?

    static func startIfRequested() {
        guard let path = ProcessInfo.processInfo.environment["MEZIANTOU_MUSIC_SNAPSHOT_PATH"], !path.isEmpty else {
            return
        }

        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { _ in
            MainActor.assumeIsolated {
                capture(to: path)
            }
        }
    }

    private static func capture(to path: String) {
        let windows = NSApp.windows.filter { $0.isVisible && $0.frame.width > 100 && $0.frame.height > 100 }
        for (index, window) in windows.enumerated() {
            guard let view = window.contentView?.superview ?? window.contentView,
                  let representation = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
                continue
            }

            view.cacheDisplay(in: view.bounds, to: representation)
            try? representation.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: "\(path)-\(index).png"))
        }
    }
}
#endif
