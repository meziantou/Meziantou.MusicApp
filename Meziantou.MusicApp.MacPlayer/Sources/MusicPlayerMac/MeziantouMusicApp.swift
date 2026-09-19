import AppKit
import MusicPlayerCore
import SwiftUI

@main
struct MeziantouMusicApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    private let model = AppModel.shared

    var body: some Scene {
        Window("Meziantou Music", id: "main") {
            // Views read `AppModel.shared` directly: environment objects are not reliably available
            // in views hosted by NSTableView (List and Table rows) and crash when missing
            ContentView()
                .frame(minWidth: 820, minHeight: 480)
        }
        .defaultSize(width: 1200, height: 760)
        .commands {
            PlayerCommands(model: model, player: model.player)
        }

        Settings {
            SettingsView()
        }
    }
}

extension AppModel {
    static let shared = AppModel()
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var keyMonitor: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
#if DEBUG
        DebugSnapshots.startIfRequested()
        DebugSnapshots.dumpDockMenuIfRequested()
#endif

        // Space toggles playback, except while typing in a text field
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask).subtracting([.capsLock, .numericPad, .function])
            guard event.keyCode == 49, modifiers.isEmpty, !(NSApp.keyWindow?.firstResponder is NSText), NSApp.keyWindow?.attachedSheet == nil else {
                return event
            }

            MainActor.assumeIsolated {
                AppModel.shared.player.togglePlayPause()
            }
            return nil
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
