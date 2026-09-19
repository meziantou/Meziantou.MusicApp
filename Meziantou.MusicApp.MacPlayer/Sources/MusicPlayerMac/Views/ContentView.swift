import MusicPlayerCore
import SwiftUI

struct ContentView: View {
    private let model = AppModel.shared
    @Environment(\.openSettings) private var openSettings

    var body: some View {
        @Bindable var model = model

        // The player bar is laid out below the split view rather than as a safe area inset:
        // the table's scroll view ignores the inset, which would hide its last rows under the bar
        VStack(spacing: 0) {
            NavigationSplitView {
                SidebarView()
                    .navigationSplitViewColumnWidth(min: 200, ideal: 250, max: 400)
            } detail: {
                detail
            }
            .inspector(isPresented: $model.isQueueVisible) {
                QueueView()
                    .inspectorColumnWidth(min: 260, ideal: 320, max: 480)
            }

            PlayerBarView()
        }
        .overlay(alignment: .bottomTrailing) {
            ToastOverlay()
                .padding(.bottom, 96)
                .padding(.trailing, 16)
        }
        .sheet(item: $model.songDetailsTrack) { track in
            SongDetailsView(track: track)
        }
        .task {
            await model.initialize()
            if model.settings.serverUrl.isEmpty {
                openSettings()
            }
        }
    }

    @ViewBuilder
    private var detail: some View {
        if !model.isInitialized {
            ProgressView()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if model.settings.serverUrl.isEmpty {
            ContentUnavailableView {
                Label("No Server Configured", systemImage: "server.rack")
            } description: {
                Text("Enter the URL of your Meziantou Music Server to browse your playlists.")
            } actions: {
                SettingsLink {
                    Text("Open Settings…")
                }
            }
        } else {
            TrackListView()
        }
    }
}

struct ToastOverlay: View {
    private let model = AppModel.shared

    var body: some View {
        VStack(alignment: .trailing, spacing: 8) {
            ForEach(model.toasts) { toast in
                Label(toast.message, systemImage: toast.symbol)
                    .labelStyle(.titleAndIcon)
                    .font(.callout)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 9)
                    .foregroundStyle(toast.kind == .error ? AnyShapeStyle(.red) : AnyShapeStyle(.primary))
                    .background(.regularMaterial, in: Capsule())
                    .shadow(radius: 4, y: 2)
                    .transition(.move(edge: .trailing).combined(with: .opacity))
            }
        }
        .frame(maxWidth: 420, alignment: .trailing)
        .animation(.snappy, value: model.toasts)
        .allowsHitTesting(false)
    }
}

extension Toast {
    var symbol: String {
        switch kind {
        case .info: "info.circle"
        case .success: "checkmark.circle"
        case .error: "exclamationmark.triangle"
        }
    }
}
