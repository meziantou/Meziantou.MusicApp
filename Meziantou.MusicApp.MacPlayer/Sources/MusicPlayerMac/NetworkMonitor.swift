import Foundation
import MusicPlayerCore
import Network

/// Reports connectivity and whether the connection should be treated as "Low Data"
/// (Low Data Mode or an expensive connection such as a cellular hotspot).
@MainActor
final class NetworkMonitor {
    private let monitor = NWPathMonitor()

    private(set) var isOnline = true
    private(set) var networkType = NetworkType.normal

    /// Called on the main actor when the status changes.
    var onChange: ((_ isOnline: Bool, _ networkType: NetworkType) -> Void)?

    func start() {
        monitor.pathUpdateHandler = { @Sendable [weak self] path in
            let isOnline = path.status == .satisfied
            let networkType: NetworkType = path.isConstrained || path.isExpensive ? .lowData : .normal
            Task { @MainActor in
                self?.update(isOnline: isOnline, networkType: networkType)
            }
        }
        monitor.start(queue: DispatchQueue(label: "net.meziantou.music.network"))
    }

    private func update(isOnline: Bool, networkType: NetworkType) {
        guard isOnline != self.isOnline || networkType != self.networkType else {
            return
        }

        self.isOnline = isOnline
        self.networkType = networkType
        onChange?(isOnline, networkType)
    }
}
