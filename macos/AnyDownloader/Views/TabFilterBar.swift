import SwiftUI
import AnyDownloaderCore

enum FilterTab: String, CaseIterable {
    case all = "All"
    case active = "Active"
    case paused = "Paused"
    case completed = "Done"
    case torrent = "Torrents"
}

struct TabFilterBar: View {
    @Binding var selected: FilterTab
    let store: DownloadStore

    var body: some View {
        HStack(spacing: 0) {
            HStack(spacing: 4) {
                ForEach(FilterTab.allCases, id: \.self) { tab in
                    TabButton(tab: tab, isSelected: selected == tab, count: count(for: tab)) {
                        selected = tab
                    }
                }
            }
            Spacer(minLength: 8)
            HStack(spacing: 3) {
                Image(systemName: "arrow.down")
                    .font(.system(size: 9, weight: .semibold))
                Text(totalSpeedString)
                    .font(.system(size: 11).monospacedDigit())
            }
            .foregroundColor(.secondary)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(Color(NSColor.controlBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 5))
            .opacity(store.activeCount > 0 ? 1 : 0)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .frame(height: 36)
        .background(Color(NSColor.windowBackgroundColor).opacity(0.6))
    }

    private func count(for tab: FilterTab) -> Int {
        switch tab {
        case .all:       return store.items.count
        case .active:    return store.filtered(by: .active).count
        case .paused:    return store.filtered(by: .paused).count
        case .completed: return store.filtered(by: .completed).count
        case .torrent:   return store.items.filter { $0.type == .torrent }.count
        }
    }

    private var totalSpeedString: String {
        let total = store.filtered(by: .active).reduce(Int64(0)) { $0 + $1.speedBytesPerSec }
        return ByteCountFormatter.string(fromByteCount: total, countStyle: .binary) + "/s"
    }
}

#Preview {
    @Previewable @State var selected: FilterTab = .all
    TabFilterBar(selected: $selected, store: DownloadStore.preview)
        .frame(width: 600)
}

// MARK: - TabButton

private struct TabButton: View {
    let tab: FilterTab
    let isSelected: Bool
    let count: Int
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                // Invisible max-width ghost keeps the button size locked
                pillLabel(count: 99).opacity(0)
                // Visible label
                pillLabel(count: count)
            }
            // Ensure the full padded rect is the hit area, not just the text
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(isSelected ? Color.accentColor : Color.clear)
        .foregroundColor(isSelected ? .white : .secondary)
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .animation(.none, value: count)
        .animation(.none, value: isSelected)
    }

    @ViewBuilder
    private func pillLabel(count: Int) -> some View {
        HStack(spacing: 4) {
            Text(tab.rawValue)
                .font(.system(size: 12, weight: .semibold))
            Text(count > 0 ? "\(count)" : "")
                .font(.system(size: 10, weight: .semibold).monospacedDigit())
                .padding(.horizontal, 4)
                .padding(.vertical, 1)
                .frame(minWidth: 16)
                .background(
                    isSelected
                        ? Color.white.opacity(0.25)
                        : Color.secondary.opacity(count > 0 ? 0.18 : 0)
                )
                .clipShape(Capsule())
                .opacity(count > 0 ? 1 : 0)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
    }
}
