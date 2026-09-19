import MusicPlayerCore
import SwiftUI

struct SettingsView: View {
    private enum ConnectionStatus {
        case idle
        case testing
        case success
        case failure
    }

    private let model = AppModel.shared
    @State private var draft = AppSettings()
    @State private var connectionStatus = ConnectionStatus.idle
    @State private var scanStatus: ScanStatusResponse?
    @State private var isTriggeringScan = false
    @State private var isCleaningCache = false
    @State private var isDiagnosticsPresented = false
    @State private var isSaving = false

    var body: some View {
        Form {
            serverSection
            qualitySection
            interfaceSection
            playbackSection
            advancedSection
            Section {
                LabeledContent("Version", value: AppInfo.version)
            }
        }
        .formStyle(.grouped)
        .frame(width: 560, height: 680)
        .safeAreaInset(edge: .bottom) {
            HStack {
                Spacer()
                Button("Revert") {
                    draft = model.settings
                }
                .disabled(!hasChanges)
                Button("Save") {
                    Task {
                        isSaving = true
                        await model.updateSettings(draft)
                        draft = model.settings
                        isSaving = false
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(!hasChanges || isSaving)
            }
            .padding()
            .background(.bar)
        }
        .onAppear {
            draft = model.settings
            connectionStatus = .idle
        }
        .task(id: "\(model.isOnline):\(model.settings.serverUrl)") {
            while !Task.isCancelled {
                scanStatus = await model.scanStatus()
                try? await Task.sleep(for: .seconds(2))
            }
        }
        .sheet(isPresented: $isDiagnosticsPresented) {
            CacheDiagnosticsView()
        }
    }

    private var hasChanges: Bool {
        draft != model.settings
    }

    // MARK: Sections

    private var serverSection: some View {
        Section("Server Connection") {
            TextField("Server URL", text: $draft.serverUrl, prompt: Text("https://your-server.com"))
                .textContentType(.URL)
                .autocorrectionDisabled()
                .onChange(of: draft.serverUrl) {
                    connectionStatus = .idle
                }

            HStack {
                Button(connectionStatus == .testing ? "Testing…" : "Test Connection") {
                    Task {
                        connectionStatus = .testing
                        connectionStatus = await model.testConnection(serverUrl: draft.serverUrl) ? .success : .failure
                    }
                }
                .disabled(connectionStatus == .testing || draft.serverUrl.trimmingCharacters(in: .whitespaces).isEmpty)

                switch connectionStatus {
                case .success:
                    Label("Connected successfully", systemImage: "checkmark.circle.fill")
                        .foregroundStyle(.green)
                case .failure:
                    Label("Connection failed", systemImage: "xmark.circle.fill")
                        .foregroundStyle(.red)
                case .idle, .testing:
                    EmptyView()
                }
            }
        }
    }

    private var qualitySection: some View {
        Section {
            qualityPicker("Normal Data Quality", selection: $draft.normalQuality, help: "Quality when connected to Wi-Fi or Ethernet")
            qualityPicker("Low Data Quality", selection: $draft.lowDataQuality, help: "Quality in Low Data Mode or on expensive networks such as a cellular hotspot")
            qualityPicker("Download Quality", selection: $draft.downloadQuality, help: "Quality for offline cached tracks. Changing it redownloads offline playlists.")
            Toggle(isOn: $draft.preventDownloadOnLowData) {
                Text("Prevent download on Low Data Mode")
                Text("When enabled, only cached tracks play in Low Data Mode")
            }
        } header: {
            Text("Streaming Quality")
        } footer: {
            Text("Opus and OGG require macOS support for Ogg files; when unsupported, the player automatically falls back to AAC.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private func qualityPicker(_ title: String, selection: Binding<StreamingQuality>, help: String) -> some View {
        Picker(selection: selection) {
            ForEach(QualityOption.all) { option in
                Text(option.label).tag(option.quality)
            }

            if !QualityOption.all.contains(where: { $0.quality == selection.wrappedValue }) {
                Text("Custom (\(selection.wrappedValue.badge))").tag(selection.wrappedValue)
            }
        } label: {
            Text(title)
            Text(help)
        }
    }

    private var interfaceSection: some View {
        Section("Interface") {
            Toggle(isOn: $draft.hideCoverArt) {
                Text("Hide Cover Art")
                Text("Do not load cover images to save memory and data")
            }
            Toggle(isOn: $draft.hideTrackIndex) {
                Text("Hide Track Index")
                Text("Hide track numbers and the playback indicator column")
            }
            Toggle(isOn: $draft.hideTrackDuration) {
                Text("Hide Track Duration")
                Text("Hide the duration column in the track list")
            }
            Toggle(isOn: $draft.hideTrackCacheStatus) {
                Text("Hide Cache Status")
                Text("Hide the offline availability icon in the track list")
            }
            Toggle(isOn: $draft.disablePlayingAnimation) {
                Text("Disable Playing Track Animation")
                Text("Show a static icon instead of animated bars for the playing track")
            }
            Toggle(isOn: $draft.showPlaylistFileSize) {
                Text("Show Playlist File Size")
                Text("Show the total size of files for each playlist in the sidebar")
            }
            Toggle(isOn: $draft.showInMenuBar) {
                Text("Show in Menu Bar")
                Text("Control playback from the menu bar. When the window is closed, the Dock icon is hidden and the app uses the least resources")
            }
        }
    }

    private var playbackSection: some View {
        Section("Playback") {
            Picker(selection: $draft.replayGainMode) {
                Text("Off").tag(ReplayGainMode.off)
                Text("Track").tag(ReplayGainMode.track)
                Text("Album").tag(ReplayGainMode.album)
            } label: {
                Text("ReplayGain Mode")
                Text("Normalize volume levels across tracks")
            }

            Toggle(isOn: $draft.showReplayGainWarning) {
                Text("Show ReplayGain Warning")
                Text("Show an indicator when a track is missing ReplayGain data")
            }
        }
    }

    private var advancedSection: some View {
        Section("Advanced") {
            let isServerScanning = scanStatus?.isScanning ?? false
            let canUseServer = model.isOnline && !model.settings.serverUrl.isEmpty

            LabeledContent("Last scan") {
                Text(lastScanText)
            }

            HStack {
                Button(isServerScanning ? "Scan in Progress…" : "Rescan Music Library") {
                    triggerScan(force: false)
                }
                Button("Force Rescan (Ignore Cache)") {
                    triggerScan(force: true)
                }
            }
            .disabled(!canUseServer || isServerScanning || isTriggeringScan)

            if let scanStatus, scanStatus.isScanning {
                VStack(alignment: .leading, spacing: 4) {
                    if let percentage = scanStatus.percentage {
                        ProgressView(value: min(100, max(0, percentage)), total: 100) {
                            Text("Library scan in progress — \(Int(percentage.rounded()))%")
                        }
                    } else {
                        ProgressView {
                            Text("Library scan in progress")
                        }
                    }

                    Group {
                        if let total = scanStatus.totalFiles {
                            Text("Files: \(scanStatus.processedFiles ?? 0) / \(total)")
                        }

                        if let total = scanStatus.totalPlaylists {
                            Text("Playlists: \(scanStatus.processedPlaylists ?? 0) / \(total)")
                        }

                        if let remaining = scanStatus.estimatedRemainingSeconds, remaining > 0 {
                            Text("Estimated completion: \(Date().addingTimeInterval(remaining).formatted(date: .omitted, time: .shortened))")
                        } else {
                            Text("Estimated completion time unavailable")
                        }
                    }
                    .font(.caption)
                    .foregroundStyle(.secondary)
                }
            }

            HStack {
                Button("Cache Diagnostics…") {
                    isDiagnosticsPresented = true
                }

                Button(isCleaningCache ? "Cleaning…" : "Clean Transcoding Cache") {
                    Task {
                        isCleaningCache = true
                        await model.cleanupTranscodingCache()
                        isCleaningCache = false
                    }
                }
                .disabled(!canUseServer || isCleaningCache)
            }
        }
    }

    private var lastScanText: String {
        guard let value = scanStatus?.lastScanDate else {
            return scanStatus == nil ? "Unknown" : "Never"
        }

        return DateParsing.parse(value)?.formatted(date: .abbreviated, time: .shortened) ?? value
    }

    private func triggerScan(force: Bool) {
        Task {
            isTriggeringScan = true
            await model.triggerLibraryScan(force: force)
            scanStatus = await model.scanStatus()
            isTriggeringScan = false
        }
    }
}
