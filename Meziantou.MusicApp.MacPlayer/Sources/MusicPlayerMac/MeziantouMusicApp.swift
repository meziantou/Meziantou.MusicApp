import AppKit
import MusicPlayerCore
import SwiftUI

@main
struct MeziantouMusicApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    private let model = AppModel.shared

    var body: some Scene {
        WindowGroup("Meziantou Music", id: "main") {
            // Views read `AppModel.shared` directly: environment objects are not reliably available
            // in views hosted by NSTableView (List and Table rows) and crash when missing
            ContentView()
                .frame(minWidth: 820, minHeight: 480)
        }
        .defaultSize(width: 1200, height: 760)
        .commands {
            // Single-window app: a WindowGroup is used because closing its window releases the views
            CommandGroup(replacing: .newItem) {
            }

            CommandGroup(after: .appInfo) {
                Button("Check for Updates…") {
                    UpdateService.checkForUpdates()
                }
            }

            PlayerCommands(model: model, player: model.player)
        }

        Settings {
            SettingsView()
        }

        // A menu (not a window) costs nothing while it is closed
        MenuBarExtra("Meziantou Music", systemImage: "music.note", isInserted: Binding(
            get: { model.settings.showInMenuBar },
            set: { isInserted in Task { await model.setShowInMenuBar(isInserted) } })) {
            MenuBarMenu()
        }
        .menuBarExtraStyle(.menu)
    }
}

extension AppModel {
    static let shared = AppModel()
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var keyMonitor: Any?
    private var visibilityObservers: [any NSObjectProtocol] = []

    func applicationWillFinishLaunching(_ notification: Notification) {
        // Quit before loading or saving anything, so the running instance's state is left untouched
        if SingleInstance.activateExistingInstance() {
            exit(0)
        }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        observeVisibility()
        UpdateService.checkAtLaunch()
#if DEBUG
        DebugSnapshots.startIfRequested()
        DebugSnapshots.dumpDockMenuIfRequested()
        DebugSnapshots.runWindowTestIfRequested()
#endif

        // Space toggles playback and the arrow keys seek, except while typing in a text field.
        // These are handled here rather than as menu shortcuts: a menu key equivalent on a bare
        // arrow key would also steal it from text fields and lists.
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask).subtracting([.capsLock, .numericPad, .function])
            guard modifiers.subtracting(.shift).isEmpty, !(NSApp.keyWindow?.firstResponder is NSText), NSApp.keyWindow?.attachedSheet == nil else {
                return event
            }

            let keyCode = event.keyCode
            let handled = MainActor.assumeIsolated { () -> Bool in
                let player = AppModel.shared.player
                switch keyCode {
                case KeyCode.space where !modifiers.contains(.shift):
                    player.togglePlayPause()
                    return true
                case KeyCode.leftArrow, KeyCode.rightArrow:
                    // Without a track, the arrow keys keep their usual meaning (moving in the track list)
                    guard player.currentTrack != nil else {
                        return false
                    }

                    let step = modifiers.contains(.shift) ? PlaybackConstants.fineSeekStep : PlaybackConstants.seekStep
                    player.skip(by: keyCode == KeyCode.leftArrow ? -step : step)
                    return true
                default:
                    return false
                }
            }

            return handled ? nil : event
        }
    }

    private enum KeyCode {
        static let space: UInt16 = 49
        static let leftArrow: UInt16 = 123
        static let rightArrow: UInt16 = 124
    }

    /// Tracks whether a window is on screen, so work that only matters for the UI can pause.
    private func observeVisibility() {
        let names: [Notification.Name] = [
            NSWindow.didChangeOcclusionStateNotification,
            NSWindow.didMiniaturizeNotification,
            NSWindow.didDeminiaturizeNotification,
            NSWindow.willCloseNotification,
            NSWindow.didBecomeKeyNotification,
            NSApplication.didHideNotification,
            NSApplication.didUnhideNotification,
        ]
        for name in names {
            let observer = NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main) { [weak self] notification in
                // A closing window still reports itself as visible
                let window = name == NSWindow.willCloseNotification ? notification.object as? NSWindow : nil
                let closingWindow = window.map(ObjectIdentifier.init)
                MainActor.assumeIsolated {
                    if window?.identifier?.rawValue.hasPrefix("main") == true {
                        AppModel.shared.mainWindowDidClose()
                    }

                    self?.updateVisibility(excluding: closingWindow)
                    self?.updateActivationPolicy(excluding: closingWindow)
                }
            }
            visibilityObservers.append(observer)
        }
    }

    private func updateVisibility(excluding closingWindow: ObjectIdentifier?) {
        let isVisible = !NSApp.isHidden && NSApp.windows.contains { window in
            ObjectIdentifier(window) != closingWindow
                && window.canBecomeMain
                && window.isVisible
                && !window.isMiniaturized
                && window.occlusionState.contains(.visible)
        }
        AppModel.shared.setUIVisible(isVisible)
    }

    /// In menu bar mode, the app has no Dock icon (and no menu bar of its own) while no window is open,
    /// so it only lives in the menu bar and in Control Center.
    private func updateActivationPolicy(excluding closingWindow: ObjectIdentifier?) {
        let hasWindow = NSApp.windows.contains { window in
            ObjectIdentifier(window) != closingWindow
                && window.canBecomeMain
                && (window.isVisible || window.isMiniaturized)
        }
        let policy: NSApplication.ActivationPolicy = AppModel.shared.settings.showInMenuBar && !hasWindow ? .accessory : .regular
        if NSApp.activationPolicy() != policy {
            NSApp.setActivationPolicy(policy)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        // Keep playing in the background; the window can be reopened from the Dock
        false
    }

    func applicationDockMenu(_ sender: NSApplication) -> NSMenu? {
        DockMenu.make(model: AppModel.shared)
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        AppModel.shared.applicationDidBecomeActive()
    }

    func applicationWillTerminate(_ notification: Notification) {
        AppModel.shared.applicationWillTerminate()
    }
}

struct PlayerCommands: Commands {
    let model: AppModel
    let player: PlayerController

    var body: some Commands {
        CommandGroup(after: .textEditing) {
            Button("Find") {
                model.requestSearchFocus()
            }
            .keyboardShortcut("f")
        }

        CommandMenu("Controls") {
            Button(player.isPlaying ? "Pause" : "Play") {
                player.togglePlayPause()
            }
            .disabled(player.currentTrack == nil)

            Button("Next") {
                player.next()
            }
            .keyboardShortcut(.rightArrow)

            Button("Previous") {
                player.previous()
            }
            .keyboardShortcut(.leftArrow)

            Divider()

            Button("Skip Forward 10 Seconds") {
                player.skip(by: 10)
            }
            .keyboardShortcut(.rightArrow, modifiers: [.command, .shift])

            Button("Skip Back 10 Seconds") {
                player.skip(by: -10)
            }
            .keyboardShortcut(.leftArrow, modifiers: [.command, .shift])

            Button("Skip Forward 30 Seconds") {
                player.skip(by: 30)
            }
            .keyboardShortcut(.rightArrow, modifiers: [.command, .option])

            Button("Skip Back 30 Seconds") {
                player.skip(by: -30)
            }
            .keyboardShortcut(.leftArrow, modifiers: [.command, .option])

            Divider()

            Button("Increase Volume") {
                player.setVolume(player.volume + PlaybackConstants.volumeStep)
            }
            .keyboardShortcut(.upArrow)

            Button("Decrease Volume") {
                player.setVolume(player.volume - PlaybackConstants.volumeStep)
            }
            .keyboardShortcut(.downArrow)

            Button(player.isMuted ? "Unmute" : "Mute") {
                player.toggleMute()
            }
            .keyboardShortcut(.downArrow, modifiers: [.command, .option])

            Divider()

            Toggle("Shuffle", isOn: Binding(get: { player.shuffleEnabled }, set: { player.setShuffle($0) }))

            Button("Repeat: \(player.repeatMode.label)") {
                player.cycleRepeatMode()
            }
            .keyboardShortcut("r")

            Divider()

            Button("Go to Current Track") {
                Task { await model.revealCurrentTrack() }
            }
            .keyboardShortcut("l")
            .disabled(player.currentTrack == nil)

            Button(model.isQueueVisible ? "Hide Playing Queue" : "Show Playing Queue") {
                model.isQueueVisible.toggle()
            }
            .keyboardShortcut("u", modifiers: [.command, .option])
        }
    }
}

extension RepeatMode {
    var label: String {
        switch self {
        case .off: "Off"
        case .all: "All"
        case .one: "One"
        }
    }
}
