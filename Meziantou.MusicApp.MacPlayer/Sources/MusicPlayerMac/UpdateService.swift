import AppKit
import MusicPlayerCore

/// Checks GitHub for a newer release of the app and offers to open its release page.
@MainActor
enum UpdateService {
    private static var isChecking = false

    /// Checks silently at launch: errors are ignored and versions the user chose to skip are not suggested again.
    static func checkAtLaunch() {
#if DEBUG
        // Development builds are not versioned
        return
#else
        Task { await check(userInitiated: false) }
#endif
    }

    /// Checks on request and reports the result, including when the app is up to date.
    static func checkForUpdates() {
        Task { await check(userInitiated: true) }
    }

    private static func check(userInitiated: Bool) async {
        guard !isChecking else {
            return
        }

        isChecking = true
        defer { isChecking = false }

        guard let currentVersion = UpdateChecker.currentVersion else {
            return
        }

        let release: AppRelease?
        do {
            release = try await UpdateChecker().availableUpdate(currentVersion: currentVersion)
        } catch {
            if userInitiated {
                showAlert(message: "Unable to check for updates", information: error.localizedDescription)
            }

            return
        }

        guard let release else {
            if userInitiated {
                showAlert(message: "Meziantou Music is up to date", information: "Version \(currentVersion) is the latest version.")
            }

            return
        }

        if !userInitiated && UserDefaults.standard.string(forKey: DefaultsKeys.skippedUpdateVersion) == release.version.description {
            return
        }

        promptUpdate(release: release, currentVersion: currentVersion)
    }

    private static func promptUpdate(release: AppRelease, currentVersion: AppVersion) {
        let alert = NSAlert()
        alert.messageText = "A new version of Meziantou Music is available"
        alert.informativeText = "Meziantou Music \(release.version) is available. You have version \(currentVersion). Would you like to open the release page to download it?"
        alert.addButton(withTitle: "Update")
        alert.addButton(withTitle: "Later")
        alert.addButton(withTitle: "Skip This Version")

        NSApp.activate()
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            NSWorkspace.shared.open(release.url)
        case .alertThirdButtonReturn:
            UserDefaults.standard.set(release.version.description, forKey: DefaultsKeys.skippedUpdateVersion)
        default:
            break
        }
    }

    private static func showAlert(message: String, information: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = information
        NSApp.activate()
        alert.runModal()
    }
}
