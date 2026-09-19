import AppKit

/// Ensures only one instance of the app runs, even when it is started from another copy of the bundle,
/// with `open -n`, or by running the executable directly.
@MainActor
enum SingleInstance {
    /// Brings the already running instance to the front. Returns false when this is the only instance.
    ///
    /// Instances using a custom data directory (`MEZIANTOU_MUSIC_DATA_DIR`, for development) are independent.
    static func activateExistingInstance() -> Bool {
        guard ProcessInfo.processInfo.environment["MEZIANTOU_MUSIC_DATA_DIR"] == nil,
              let bundleIdentifier = Bundle.main.bundleIdentifier else {
            return false
        }

        let current = NSRunningApplication.current
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier)
            .filter { $0.processIdentifier != current.processIdentifier && !$0.isTerminated }

        // When two instances start at the same time, the oldest one stays
        guard let existing = others.min(by: isOlder), isOlder(existing, than: current) else {
            return false
        }

        if let bundleUrl = existing.bundleURL, bundleUrl.pathExtension == "app" {
            // Opening the running app sends it a reopen event, which also shows its window if it was closed
            let configuration = NSWorkspace.OpenConfiguration()
            configuration.activates = true
            let semaphore = DispatchSemaphore(value: 0)
            NSWorkspace.shared.openApplication(at: bundleUrl, configuration: configuration) { _, _ in
                semaphore.signal()
            }
            _ = semaphore.wait(timeout: .now() + 3)
        } else {
            existing.activate()
        }

        return true
    }

    private static func isOlder(_ lhs: NSRunningApplication, than rhs: NSRunningApplication) -> Bool {
        switch (lhs.launchDate, rhs.launchDate) {
        case let (left?, right?) where left != right:
            return left < right
        default:
            return lhs.processIdentifier < rhs.processIdentifier
        }
    }
}
