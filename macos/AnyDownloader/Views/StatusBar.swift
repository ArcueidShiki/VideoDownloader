import SwiftUI
import AnyDownloaderCore

struct StatusBar: View {
    @Environment(DownloadStore.self) private var store

    var body: some View {
        HStack(spacing: 12) {
            Circle()
                .fill(store.activeCount > 0 ? Color.green : Color.secondary)
                .frame(width: 6, height: 6)
            Text(store.activeCount > 0 ? "Downloading" : "Idle")
                .font(.system(size: 11))
                .foregroundColor(.secondary)
            Text("·").foregroundColor(.secondary)
            Text("\(store.activeCount) active · \(store.filtered(by: .paused).count) paused · \(store.filtered(by: .completed).count) completed")
                .font(.system(size: 11))
                .foregroundColor(.secondary)
            Spacer()
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 5)
        .background(Color(NSColor.windowBackgroundColor).opacity(0.5))
        .onChange(of: store.activeCount) { _, new in
            AppDelegate.updateDockBadge(activeCount: new)
        }
    }
}

#Preview {
    StatusBar()
        .environment(DownloadStore.preview)
        .frame(width: 500)
}
