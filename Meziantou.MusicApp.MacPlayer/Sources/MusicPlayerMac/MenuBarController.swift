import AppKit
import MusicPlayerCore
import Observation
import SwiftUI

/// The menu bar item: the same actions as the Dock menu with a volume slider, plus a way back to the window.
/// It is an AppKit status item rather than a SwiftUI `MenuBarExtra` because SwiftUI menus cannot show a slider.
/// Its menu is built each time it opens, so it costs nothing while it is closed.
@MainActor
final class MenuBarController: NSObject, NSMenuDelegate {
    static let shared = MenuBarController()

    private var model: AppModel?
    private var statusItem: NSStatusItem?
    private var visibilityObservation: NSKeyValueObservation?
    private var openWindow: OpenWindowAction?
    private var openSettings: OpenSettingsAction?

    /// Shows the item whenever Settings > Interface > Show in Menu Bar is on.
    func start(model: AppModel) {
        self.model = model
        observeSettings()
    }

    /// Only SwiftUI can open its windows: `SceneActionsCommands` hands its actions over.
    func setSceneActions(openWindow: OpenWindowAction, openSettings: OpenSettingsAction) {
        self.openWindow = openWindow
        self.openSettings = openSettings
    }

    private func observeSettings() {
        guard let model else {
            return
        }

        let isShown = withObservationTracking {
            model.settings.showInMenuBar
        } onChange: { [weak self] in
            Task { @MainActor in
                self?.observeSettings()
            }
        }
        setShown(isShown)
    }

    private func setShown(_ isShown: Bool) {
        if isShown, statusItem == nil {
            let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
            item.autosaveName = "MeziantouMusic"
            item.behavior = .removalAllowed
            item.isVisible = true
            item.button?.image = NSImage(systemSymbolName: "music.note", accessibilityDescription: "Meziantou Music")
            let menu = NSMenu()
            menu.delegate = self
            item.menu = menu

            // Removing the item from the menu bar (⌘-drag) turns the setting off
            visibilityObservation = item.observe(\.isVisible, options: [.new]) { _, change in
                guard change.newValue == false else {
                    return
                }

                Task { @MainActor in
                    await AppModel.shared.setShowInMenuBar(false)
                }
            }
            statusItem = item
        } else if !isShown, let statusItem {
            visibilityObservation = nil
            NSStatusBar.system.removeStatusItem(statusItem)
            self.statusItem = nil
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        guard let model else {
            return
        }

        menu.removeAllItems()
        DockMenu.addPlayerItems(to: menu, model: model, usesVolumeSlider: true)

        menu.addItem(.separator())
        menu.addItem(ActionMenuItem(title: "Show Meziantou Music") { [weak self] in
            self?.showMainWindow()
        })
        menu.addItem(ActionMenuItem(title: "Settings…") { [weak self] in
            NSApp.activate()
            self?.openSettings?()
        })

        menu.addItem(.separator())
        menu.addItem(ActionMenuItem(title: "Quit Meziantou Music") {
            NSApp.terminate(nil)
        })
    }

    private func showMainWindow() {
        // The Dock icon comes back as soon as a window is shown; set it first so the window is ordered in front
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()

        // A closed window is not reused: SwiftUI released its content
        let existing = NSApp.windows.first { window in
            window.identifier?.rawValue.hasPrefix("main") == true && (window.isVisible || window.isMiniaturized)
        }
        if let existing {
            if existing.isMiniaturized {
                existing.deminiaturize(nil)
            }

            existing.makeKeyAndOrderFront(nil)
        } else {
            openWindow?(id: "main")
        }
    }
}

/// Hands SwiftUI's scene actions to the menu bar item. Commands, unlike views, exist for the whole life of the app.
struct SceneActionsCommands: Commands {
    @Environment(\.openWindow) private var openWindow
    @Environment(\.openSettings) private var openSettings

    var body: some Commands {
        let _ = MenuBarController.shared.setSceneActions(openWindow: openWindow, openSettings: openSettings)
        EmptyCommands()
    }
}

/// A menu item showing a volume slider between a speaker icon and the volume percentage, like the player bar.
@MainActor
final class VolumeSliderMenuItem: NSMenuItem {
    init(player: PlayerController, onChange: @escaping @MainActor () -> Void) {
        super.init(title: "Volume", action: nil, keyEquivalent: "")
        view = VolumeSliderView(player: player, onChange: onChange)
    }

    @available(*, unavailable)
    required init(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }
}

@MainActor
private final class VolumeSliderView: NSView {
    private let player: PlayerController
    private let onChange: @MainActor () -> Void
    private let icon = NSImageView()
    private let slider = NSSlider()
    private let percentage = NSTextField(labelWithString: "")

    init(player: PlayerController, onChange: @escaping @MainActor () -> Void) {
        self.player = player
        self.onChange = onChange
        super.init(frame: NSRect(x: 0, y: 0, width: 240, height: 28))
        // The menu stretches the view to its width
        autoresizingMask = .width

        icon.contentTintColor = .secondaryLabelColor
        icon.imageScaling = .scaleProportionallyDown

        slider.minValue = 0
        slider.maxValue = PlaybackConstants.maxVolume
        slider.isContinuous = true
        slider.target = self
        slider.action = #selector(sliderChanged)
        slider.setAccessibilityLabel("Volume")

        percentage.font = .monospacedDigitSystemFont(ofSize: NSFont.smallSystemFontSize, weight: .regular)
        percentage.textColor = .secondaryLabelColor
        percentage.alignment = .right

        for subview in [icon, slider, percentage] {
            subview.translatesAutoresizingMaskIntoConstraints = false
            addSubview(subview)
        }

        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 14),
            icon.centerYAnchor.constraint(equalTo: centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 20),
            slider.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 6),
            slider.centerYAnchor.constraint(equalTo: centerYAnchor),
            percentage.leadingAnchor.constraint(equalTo: slider.trailingAnchor, constant: 6),
            percentage.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -14),
            percentage.centerYAnchor.constraint(equalTo: centerYAnchor),
            percentage.widthAnchor.constraint(equalToConstant: 38),
        ])

        update()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func scrollWheel(with event: NSEvent) {
        let steps = ScrollWheel.steps(
            deltaX: event.scrollingDeltaX,
            deltaY: event.scrollingDeltaY,
            hasPreciseDeltas: event.hasPreciseScrollingDeltas,
            isDirectionInverted: event.isDirectionInvertedFromDevice)
        if steps != 0 {
            player.setVolume(player.volume + steps * PlaybackConstants.volumeStep)
            update()
            onChange()
        }
    }

    @objc private func sliderChanged() {
        player.setVolume(slider.doubleValue)
        update()
        onChange()
    }

    private func update() {
        let volume = player.isMuted ? 0 : player.volume
        slider.doubleValue = volume
        percentage.stringValue = "\(Int((volume * 100).rounded()))%"
        icon.image = NSImage(systemSymbolName: player.volumeSymbolName, accessibilityDescription: nil)
    }
}

extension PlayerController {
    /// SF Symbol matching the volume and mute state.
    var volumeSymbolName: String {
        if isMuted || volume == 0 {
            return "speaker.slash.fill"
        }

        return volume < 0.5 ? "speaker.wave.1.fill" : "speaker.wave.3.fill"
    }
}
