import SwiftUI
import AnyDownloaderCore

struct DownloadListView: View {
    let filter: FilterTab
    let outputDirectory: URL
    @Binding var selectedIDs: Set<UUID>
    var isURLFocused: Bool = false
    @Environment(DownloadStore.self) private var store
    @State private var lastClickedID: UUID?

    private var visibleItems: [DownloadItem] {
        switch filter {
        case .all:       return store.items
        case .active:    return store.filtered(by: .active)
        case .paused:    return store.filtered(by: .paused)
        case .completed: return store.filtered(by: .completed)
        case .torrent:   return store.items.filter { $0.type == .torrent }
        }
    }

    var body: some View {
        ZStack {
            if visibleItems.isEmpty {
                emptyState
            } else {
                ScrollView {
                    LazyVStack(spacing: 6) {
                        ForEach(visibleItems) { item in
                            DownloadRowView(
                                item: item,
                                outputDirectory: outputDirectory,
                                isSelected: selectedIDs.contains(item.id),
                                onToggleSelect: { id in handleToggle(id: id) }
                            )
                        }
                    }
                    .padding(10)
                }
            }

            // Cmd+A: select-all / deselect-all toggle.
            // Hidden when the URL text field is focused so Cmd+A there selects its text.
            if !isURLFocused {
                Button {
                    let allSelectableIDs = Set(
                        visibleItems.filter { $0.state != .completed }.map { $0.id })
                    // Toggle: if everything is already selected → clear; else → select all
                    selectedIDs = (selectedIDs == allSelectableIDs) ? [] : allSelectableIDs
                } label: { EmptyView() }
                .keyboardShortcut("a", modifiers: .command)
                .frame(width: 0, height: 0)
                .hidden()
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        // Deselect items that disappear (e.g. removed from store)
        .onChange(of: store.items.count) { _, _ in
            let validIDs = Set(store.items.map { $0.id })
            selectedIDs.formIntersection(validIDs)
        }
    }

    // MARK: - Selection logic

    private func handleToggle(id: UUID) {
        let isShift = NSApp.currentEvent?.modifierFlags.contains(.shift) ?? false

        if isShift,
           let anchor = lastClickedID,
           let fromIdx = visibleItems.firstIndex(where: { $0.id == anchor }),
           let toIdx   = visibleItems.firstIndex(where: { $0.id == id }) {
            // Range select: toggle all items between anchor and current
            let lo = min(fromIdx, toIdx)
            let hi = max(fromIdx, toIdx)
            let rangeIDs = Set(visibleItems[lo...hi].map { $0.id })
            if selectedIDs.contains(id) {
                selectedIDs.subtract(rangeIDs)
            } else {
                selectedIDs.formUnion(rangeIDs)
            }
        } else {
            if selectedIDs.contains(id) {
                selectedIDs.remove(id)
            } else {
                selectedIDs.insert(id)
            }
        }
        lastClickedID = id
    }

    // MARK: - Empty state

    @ViewBuilder
    private var emptyState: some View {
        VStack(spacing: 10) {
            Image(systemName: "arrow.down.circle")
                .font(.system(size: 40))
                .foregroundColor(.secondary.opacity(0.5))
            Text("No Downloads")
                .font(.headline)
                .foregroundColor(.secondary)
            Text("Paste a URL, YouTube link, or magnet link above.")
                .font(.subheadline)
                .foregroundColor(.secondary.opacity(0.8))
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

#Preview {
    @Previewable @State var selectedIDs = Set<UUID>()
    DownloadListView(
        filter: .all,
        outputDirectory: URL.downloadsDirectory,
        selectedIDs: $selectedIDs
    )
    .environment(DownloadStore.preview)
    .frame(width: 600, height: 400)
}
