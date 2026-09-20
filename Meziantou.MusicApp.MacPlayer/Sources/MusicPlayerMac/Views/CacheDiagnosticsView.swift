import MusicPlayerCore
import SwiftUI

struct CacheDiagnosticsView: View {
    private enum PendingAction: Identifiable {
        case clearAll
        case clearCovers

        var id: Self {
            self
        }
    }

    private let model = AppModel.shared
    @Environment(\.dismiss) private var dismiss
    @State private var usage: StorageUsage?
    @State private var isWorking = false
    @State private var pendingAction: PendingAction?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Form {
                Section("Storage Usage") {
                    if let usage {
                        ProgressView(value: Double(usage.usedBytes), total: Double(max(usage.quotaBytes, 1)))
                        LabeledContent("Used", value: Formatting.bytes(usage.usedBytes))
                        LabeledContent("Available", value: Formatting.bytes(usage.availableBytes))
                    } else {
                        ProgressView()
                    }
                }

                Section("Actions") {
                    action("Clear All Cached Data", description: "Removes downloaded tracks, cached playlists and cover art, and stops keeping any playlist offline.") {
                        pendingAction = .clearAll
                    }
                    action("Clear Cover Cache", description: "Removes all cached album art images. They are downloaded again when needed.") {
                        pendingAction = .clearCovers
                    }
                    action("Cleanup Orphaned Tracks", description: "Removes tracks that are not part of any offline playlist.") {
                        run { await model.cleanupOrphanedTracks() }
                    }
                    action("Force Refresh Playlists", description: "Forces a full synchronization of playlists from the server.") {
                        run { await model.forceRefreshPlaylists() }
                    }
                    .disabled(!model.isOnline)
                }
            }
            .formStyle(.grouped)

            HStack {
                Button("Show in Finder") {
                    NSWorkspace.shared.activateFileViewerSelecting([model.store.rootDirectory])
                }
                Spacer()
                Button("Close") {
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
            .padding()
        }
        .frame(width: 480, height: 520)
        .disabled(isWorking)
        .task {
            usage = await model.storageUsage()
        }
        .alert(item: $pendingAction) { action in
            switch action {
            case .clearAll:
                Alert(
                    title: Text("Clear all cached data?"),
                    message: Text("Downloaded tracks, cached playlists and cover art are deleted, and no playlist is kept offline anymore. You will need to mark the playlists you want offline again."),
                    primaryButton: .destructive(Text("Clear")) { run { await model.clearAllCachedData() } },
                    secondaryButton: .cancel())
            case .clearCovers:
                Alert(
                    title: Text("Clear cover cache?"),
                    message: Text("Cover images are downloaded again as needed."),
                    primaryButton: .destructive(Text("Clear")) { run { await model.clearCovers() } },
                    secondaryButton: .cancel())
            }
        }
    }

    private func action(_ title: String, description: String, perform: @escaping () -> Void) -> some View {
        LabeledContent {
            Button(title, action: perform)
        } label: {
            Text(description)
                .foregroundStyle(.secondary)
        }
    }

    private func run(_ operation: @escaping @MainActor () async -> Void) {
        Task {
            isWorking = true
            await operation()
            usage = await model.storageUsage()
            isWorking = false
        }
    }
}
