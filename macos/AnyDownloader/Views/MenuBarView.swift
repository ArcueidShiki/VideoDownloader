import SwiftUI
import AnyDownloaderCore

struct MenuBarView: View {
    @Environment(DownloadStore.self) private var store

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("AnyDownloader").font(.headline).padding(.bottom, 4)
            if store.activeCount == 0 {
                Text("No active downloads").foregroundColor(.secondary)
            } else {
                ForEach(store.filtered(by: .active).prefix(3)) { item in
                    HStack {
                        Text(item.title ?? "Downloading…").lineLimit(1)
                        Spacer()
                        Text("\(Int(item.progress * 100))%").foregroundColor(.secondary)
                    }
                    .font(.system(size: 12))
                }
            }
            Divider()
            Button("Open AnyDownloader") { NSApp.activate(ignoringOtherApps: true) }
            Button("Quit") { NSApp.terminate(nil) }
        }
        .padding(10)
        .frame(width: 260)
    }
}
