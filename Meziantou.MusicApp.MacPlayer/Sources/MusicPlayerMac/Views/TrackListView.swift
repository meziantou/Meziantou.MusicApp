import AppKit
import MusicPlayerCore
import SwiftUI

/// A row of the track table. `id` is the index of the track in the playlist, which is unique
/// even when the same track appears several times.
struct TrackRow: Identifiable, Hashable {
    let id: Int
    let track: TrackInfo

    var title: String {
        track.title
    }

    var artist: String {
        track.artists ?? ""
    }

    var album: String {
        track.album ?? ""
    }

    var addedDate: String {
        track.addedDate ?? ""
    }
}

struct TrackListView: View {
    private let model = AppModel.shared
    private let player = AppModel.shared.player
    @AppStorage(DefaultsKeys.trackSortOption) private var sortOption = TrackSortOption.added
    @AppStorage(DefaultsKeys.trackSortDirection) private var sortDirection = TrackSortDirection.descending

    @State private var searchText = ""
    @State private var appliedSearch = ""
    @State private var isSearchFocused = false
    @State private var sortedRows: [TrackRow] = []
    @State private var haystacks: [String]?
    @State private var visibleRows: [TrackRow] = [] {
        didSet { rowsIdentity = Self.identity(of: visibleRows) }
    }

    /// Changes when the set or order of rows changes (not when only their content changes).
    @State private var rowsIdentity = 0
    @State private var selection = Set<TrackRow.ID>()

    var body: some View {
        ScrollViewReader { proxy in
            table
                .onChange(of: model.scrollToCurrentTrackRequest) {
                    scrollToCurrentTrack(proxy)
                }
        }
        .navigationTitle(selectedPlaylistName)
        .navigationSubtitle(trackCountText)
        .searchable(text: $searchText, isPresented: $isSearchFocused, placement: .toolbar, prompt: "Search tracks")
        .toolbar {
            ToolbarItem {
                sortMenu
            }

            if model.isLoading {
                ToolbarItem {
                    ProgressView()
                        .controlSize(.small)
                }
            }

            ToolbarItem {
                Button {
                    model.isQueueVisible.toggle()
                } label: {
                    Label("Playing Queue", systemImage: "list.bullet")
                }
                .help("Show or hide the playing queue")
            }
        }
        .task(id: searchText) {
            // Debounce typing like the web player
            try? await Task.sleep(for: .milliseconds(150))
            guard !Task.isCancelled else {
                return
            }

            appliedSearch = searchText
            updateVisibleRows()
        }
        .onChange(of: model.tracksVersion, initial: true) {
            rebuildRows()
        }
        .onChange(of: sortOption) {
            rebuildRows()
        }
        .onChange(of: sortDirection) {
            rebuildRows()
        }
        .onChange(of: model.selectedPlaylistId) {
            selection = []
        }
        .onChange(of: model.searchFocusRequest) {
            isSearchFocused = true
        }
    }

    private var table: some View {
        Table(visibleRows, selection: $selection, sortOrder: sortOrderBinding) {
            TableColumn("#") { row in
                IndexCell(
                    index: row.id,
                    isCurrent: isCurrent(row),
                    isPlaying: player.isPlaying,
                    isAnimated: !model.settings.disablePlayingAnimation,
                    onTogglePlay: { player.togglePlayPause() })
            }
            .width(min: 34, ideal: 40, max: 60)
            .defaultVisibility(model.settings.hideTrackIndex ? .hidden : .automatic)

            TableColumn("") { row in
                CoverImageView(model: model, trackId: row.track.id, size: 30)
                    .opacity(isAvailable(row) ? 1 : 0.4)
            }
            .width(38)
            .defaultVisibility(model.settings.hideCoverArt ? .hidden : .automatic)

            TableColumn("Title", value: \.title) { row in
                TitleCell(
                    row: row,
                    isCurrent: isCurrent(row),
                    isAvailable: isAvailable(row),
                    replayGainWarning: model.settings.showReplayGainWarning ? ReplayGain.missingDataWarning(for: row.track, mode: model.settings.replayGainMode) : nil)
            }
            .width(min: 120, ideal: 240)

            TableColumn("Artist", value: \.artist) { row in
                Text(row.track.artists ?? "Unknown Artist")
                    .foregroundStyle(isAvailable(row) ? .secondary : .tertiary)
            }
            .width(min: 80, ideal: 150)

            TableColumn("Album", value: \.album) { row in
                Text(row.track.album ?? "Unknown Album")
                    .foregroundStyle(isAvailable(row) ? .secondary : .tertiary)
            }
            .width(min: 80, ideal: 150)

            TableColumn("Added", value: \.addedDate) { row in
                Text(row.track.addedDate.flatMap(DateParsing.parse)?.formatted(date: .abbreviated, time: .omitted) ?? "")
                    .foregroundStyle(.secondary)
            }
            .width(min: 70, ideal: 100)
            .defaultVisibility(.hidden)

            TableColumn("") { row in
                if model.cachedTrackIds.contains(row.track.id) {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundStyle(.green)
                        .help("Available offline")
                }
            }
            .width(22)
            .defaultVisibility(model.settings.hideTrackCacheStatus ? .hidden : .automatic)

            TableColumn("Time") { row in
                Text(Formatting.duration(row.track.duration))
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            .width(min: 44, ideal: 56, max: 80)
            .alignment(.trailing)
            .defaultVisibility(model.settings.hideTrackDuration ? .hidden : .automatic)
        }
        // Recreate the table when column visibility settings change, as `defaultVisibility` is only read once.
        // Also recreate it when rows are added, removed or reordered: diffing thousands of rows into a
        // displayed NSTableView makes AppKit re-estimate row heights reentrantly ("reentrant operation in
        // its NSTableView delegate"), whereas a new table starts directly with its rows.
        .id("\(columnConfigurationId)-\(rowsIdentity)")
        .contextMenu(forSelectionType: TrackRow.ID.self) { ids in
            contextMenu(for: ids)
        } primaryAction: { ids in
            if let row = rows(for: ids).first {
                play(row)
            }
        }
        .overlay {
            if visibleRows.isEmpty && !model.isLoading {
                if !appliedSearch.isEmpty {
                    ContentUnavailableView.search(text: appliedSearch)
                } else if model.selectedPlaylistId == nil {
                    ContentUnavailableView("No Playlist Selected", systemImage: "music.note.list")
                }
            }
        }
    }

    private var columnConfigurationId: String {
        let settings = model.settings
        return [settings.hideTrackIndex, settings.hideCoverArt, settings.hideTrackCacheStatus, settings.hideTrackDuration].map { $0 ? "1" : "0" }.joined()
    }

    // MARK: Context menu

    @ViewBuilder
    private func contextMenu(for ids: Set<TrackRow.ID>) -> some View {
        let rows = rows(for: ids)
        if let first = rows.first {
            Button("Play") {
                play(first)
            }
            .disabled(!isAvailable(first))

            Button(rows.count > 1 ? "Add \(rows.count) Tracks to Queue" : "Add to Queue") {
                for row in rows.reversed() {
                    model.addToQueue(row.track, indexInPlaylist: row.id)
                }
            }

            Divider()

            let uncached = rows.filter { !model.cachedTrackIds.contains($0.track.id) }
            let cached = rows.filter { model.cachedTrackIds.contains($0.track.id) }
            if !uncached.isEmpty {
                Button("Download") {
                    Task {
                        for row in uncached {
                            await model.downloadTrack(row.track)
                        }
                    }
                }
                .disabled(!model.isOnline)
            }

            if !cached.isEmpty {
                Button("Remove Download") {
                    Task {
                        for row in cached {
                            await model.deleteDownloadedTrack(row.track)
                        }
                    }
                }
            }

            Button("Download Raw File…") {
                saveRawFile(first.track)
            }
            .disabled(rows.count != 1 || !model.isOnline)

            Divider()

            Button(rows.count > 1 ? "Copy File Paths" : "Copy File Path") {
                let paths = rows.map(\.track.path).joined(separator: "\n")
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(paths, forType: .string)
                model.showToast(rows.count > 1 ? "Copied \(rows.count) file paths" : "Copied file path: \(first.track.path)")
            }

            Button("View Details") {
                model.songDetailsTrack = first.track
            }
            .disabled(rows.count != 1)
        }
    }

    private func saveRawFile(_ track: TrackInfo) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = track.downloadFileName
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else {
            return
        }

        Task { await model.saveRawFile(track, to: url) }
    }

    // MARK: Sorting

    private var sortMenu: some View {
        Menu {
            Section("Sort by") {
                ForEach(TrackSortOption.allCases, id: \.self) { option in
                    Button {
                        if sortOption == option {
                            sortDirection = sortDirection.toggled
                        } else {
                            sortOption = option
                            sortDirection = option.defaultDirection
                        }
                    } label: {
                        if sortOption == option {
                            Label(option.label, systemImage: sortDirection == .ascending ? "chevron.up" : "chevron.down")
                        } else {
                            Text(option.label)
                        }
                    }
                }
            }
        } label: {
            Label("Sort", systemImage: "arrow.up.arrow.down")
        }
        .help("Sort tracks")
    }

    private var sortOrderBinding: Binding<[KeyPathComparator<TrackRow>]> {
        Binding(
            get: {
                let order: SortOrder = sortDirection == .ascending ? .forward : .reverse
                switch sortOption {
                case .title: return [KeyPathComparator(\TrackRow.title, order: order)]
                case .artist: return [KeyPathComparator(\TrackRow.artist, order: order)]
                case .album: return [KeyPathComparator(\TrackRow.album, order: order)]
                case .added: return [KeyPathComparator(\TrackRow.addedDate, order: order)]
                }
            },
            set: { comparators in
                guard let comparator = comparators.first else {
                    return
                }

                switch comparator.keyPath {
                case \TrackRow.title: sortOption = .title
                case \TrackRow.artist: sortOption = .artist
                case \TrackRow.album: sortOption = .album
                default: sortOption = .added
                }

                sortDirection = comparator.order == .forward ? .ascending : .descending
            })
    }

    // MARK: Data

    private func rebuildRows() {
        let tracks = model.selectedPlaylistTracks
        let indices = TrackSorting.sortIndices(tracks, by: sortOption, direction: sortDirection)
        sortedRows = indices.map { TrackRow(id: $0, track: tracks[$0]) }
        haystacks = nil
        updateVisibleRows()
    }

    private func updateVisibleRows() {
        let fragments = Search.normalize(appliedSearch).split(separator: " ")
        guard !fragments.isEmpty else {
            visibleRows = sortedRows
            return
        }

        // Only build the normalized search text once the user actually searches
        let haystacks = haystacks ?? sortedRows.map { Search.haystack(for: $0.track) }
        self.haystacks = haystacks
        visibleRows = sortedRows.indices
            .filter { index in fragments.allSatisfy { haystacks[index].contains($0) } }
            .map { sortedRows[$0] }
    }

    private static func identity(of rows: [TrackRow]) -> Int {
        var hasher = Hasher()
        hasher.combine(rows.count)
        for row in rows {
            hasher.combine(row.id)
            hasher.combine(row.track.id)
        }

        return hasher.finalize()
    }

    private func rows(for ids: Set<TrackRow.ID>) -> [TrackRow] {
        visibleRows.filter { ids.contains($0.id) }
    }

    private func play(_ row: TrackRow) {
        guard isAvailable(row) else {
            return
        }

        // Play in the displayed order (sorted, not filtered), like the web player
        model.playTrack(row.track, orderedTracks: sortedRows.map(\.track))
    }

    private func isAvailable(_ row: TrackRow) -> Bool {
        model.isTrackAvailable(row.track)
    }

    /// The same track can be in several playlists: only flag it in the playlist it is playing from.
    private func isCurrent(_ row: TrackRow) -> Bool {
        row.track.id == player.currentTrack?.id && model.selectedPlaylistId == player.playingPlaylistId
    }

    private func scrollToCurrentTrack(_ proxy: ScrollViewProxy) {
        guard let trackId = player.currentTrack?.id, let row = visibleRows.first(where: { $0.track.id == trackId }) else {
            return
        }

        selection = [row.id]
        withAnimation {
            proxy.scrollTo(row.id, anchor: .center)
        }
    }

    private var selectedPlaylistName: String {
        model.playlists.first { $0.id == model.selectedPlaylistId }?.name ?? "Meziantou Music"
    }

    private var trackCountText: String {
        let total = model.selectedPlaylistTracks.count
        if !appliedSearch.isEmpty && visibleRows.count != total {
            return "\(visibleRows.count) of \(total) tracks"
        }

        return "\(total) tracks"
    }
}

// Table cells can be built by NSTableView outside of the SwiftUI environment chain,
// so they receive their data explicitly instead of reading @Environment objects.
private struct IndexCell: View {
    let index: Int
    let isCurrent: Bool
    let isPlaying: Bool
    let isAnimated: Bool
    let onTogglePlay: () -> Void

    var body: some View {
        if isCurrent {
            PlayingIndicator(isPlaying: isPlaying, isAnimated: isAnimated)
                .onTapGesture(perform: onTogglePlay)
                .help(isPlaying ? "Pause" : "Play")
        } else {
            Text("\(index + 1)")
                .monospacedDigit()
                .foregroundStyle(.secondary)
        }
    }
}

private struct TitleCell: View {
    let row: TrackRow
    let isCurrent: Bool
    let isAvailable: Bool
    let replayGainWarning: String?

    var body: some View {
        HStack(spacing: 6) {
            Text(row.track.title)
                .fontWeight(isCurrent ? .semibold : .regular)
                .foregroundStyle(isCurrent ? AnyShapeStyle(.tint) : (isAvailable ? AnyShapeStyle(.primary) : AnyShapeStyle(.tertiary)))

            if let replayGainWarning {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.yellow)
                    .imageScale(.small)
                    .help(replayGainWarning)
            }
        }
    }
}
