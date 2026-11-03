import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.dockTile.badgeLabel = nil
    }

    @MainActor
    static func updateDockBadge(activeCount: Int) {
        NSApp.dockTile.badgeLabel = activeCount > 0 ? "\(activeCount)" : nil
        NSApp.dockTile.display()
    }
}
