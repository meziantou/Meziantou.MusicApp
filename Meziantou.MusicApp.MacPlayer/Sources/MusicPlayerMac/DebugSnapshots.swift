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

    /// When `MEZIANTOU_MUSIC_DOCK_MENU_DUMP` is set, writes the Dock menu to that file after startup. When
    /// `MEZIANTOU_MUSIC_DOCK_MENU_ACTION` is also set (e.g. "Playlists/work"), chooses that item, then writes the menu again.
    static func dumpDockMenuIfRequested() {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["MEZIANTOU_MUSIC_DOCK_MENU_DUMP"], !path.isEmpty else {
            return
        }

        Task { @MainActor in
            try? await Task.sleep(for: .seconds(12))
            var output = describe(DockMenu.make(model: AppModel.shared), indent: "")
            if let action = environment["MEZIANTOU_MUSIC_DOCK_MENU_ACTION"] {
                output += "--- after choosing \(action): \(choose(action, in: DockMenu.make(model: AppModel.shared)))\n"
                try? await Task.sleep(for: .seconds(10))
                output += describe(DockMenu.make(model: AppModel.shared), indent: "")
            }

            try? output.write(toFile: path, atomically: true, encoding: .utf8)
        }
    }

    /// When `MEZIANTOU_MUSIC_WINDOW_TEST` is `minimize` or `close`, minimizes or closes the main window after
    /// 25 seconds (and restores a minimized window 25 seconds later) to measure the app while it is not visible.
    static func runWindowTestIfRequested() {
        guard let mode = ProcessInfo.processInfo.environment["MEZIANTOU_MUSIC_WINDOW_TEST"] else {
            return
        }

        Task { @MainActor in
            try? await Task.sleep(for: .seconds(25))
            guard let window = NSApp.windows.first(where: { $0.canBecomeMain && $0.isVisible }) else {
                return
            }

            if mode == "close" {
                window.performClose(nil)
                return
            }

            window.miniaturize(nil)
            try? await Task.sleep(for: .seconds(25))
            window.deminiaturize(nil)
        }
    }

    private static func describe(_ menu: NSMenu, indent: String) -> String {
        menu.items.map { item in
            if item.isSeparatorItem {
                return "\(indent)---\n"
            }

            let state = item.state == .on ? "[x] " : ""
            let enabled = item.isEnabled ? "" : " (disabled)"
            let line = "\(indent)\(state)\(item.title)\(enabled)\n"
            return line + (item.submenu.map { describe($0, indent: indent + "    ") } ?? "")
        }.joined()
    }

    private static func choose(_ path: String, in menu: NSMenu) -> Bool {
        var current = menu
        let titles = path.split(separator: "/").map(String.init)
        for (index, title) in titles.enumerated() {
            guard let itemIndex = current.items.firstIndex(where: { $0.title == title }) else {
                return false
            }

            if index == titles.count - 1 {
                current.performActionForItem(at: itemIndex)
                return true
            }

            guard let submenu = current.items[itemIndex].submenu else {
                return false
            }

            current = submenu
        }

        return false
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
