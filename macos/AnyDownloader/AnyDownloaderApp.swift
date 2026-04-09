import SwiftUI
import AppKit
import AnyDownloaderCore

@main
struct AnyDownloaderApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    @State private var store: DownloadStore = {
        (try? DownloadStore()) ?? (try! DownloadStore(persistencePath: ":memory:"))
    }()
    @State private var showSetup = !DependencyStatus.check().allPresent

    var body: some Scene {
        WindowGroup {
            Group {
                if showSetup {
                    DependencySetupView { showSetup = false }
                        .fixedSize()
                } else {
                    ContentView()
                        .environment(store)
                        .frame(minWidth: 920, minHeight: 580)
                }
            }
            // Deep-link entry point. Triggered when Chrome extension (or any other app)
            // opens `anydownloader://download?url=<encoded>&title=<...>`.
            .onOpenURL { url in
                handleDeepLink(url)
            }
        }
        .windowStyle(.titleBar)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("New Download…") {
                    NotificationCenter.default.post(name: .focusURLBar, object: nil)
                }
                .keyboardShortcut("n")
            }
        }

        MenuBarExtra("AnyDownloader", systemImage: "arrow.down.circle") {
            MenuBarView()
                .environment(store)
        }
    }

    // MARK: - Deep link handling

    /// Parses `anydownloader://download?url=<encoded>&title=<...>&quality=<...>`
    /// and enqueues the target URL through the existing DownloadStore pipeline.
    /// Supported host values: `download`, `add`, or empty (path-only).
    private func handleDeepLink(_ url: URL) {
        guard url.scheme?.lowercased() == "anydownloader" else { return }

        // Bring the app to the foreground so the user sees the new item.
        NSApp.activate(ignoringOtherApps: true)

        let components = URLComponents(url: url, resolvingAgainstBaseURL: false)
        let items = components?.queryItems ?? []
        let targetURL = items.first(where: { $0.name == "url" })?.value?
            .trimmingCharacters(in: .whitespaces) ?? ""

        guard !targetURL.isEmpty else {
            NSLog("[AnyDownloader] Deep link missing ?url= parameter: \(url)")
            return
        }

        Task { @MainActor in
            let opts = DownloadOptions(outputDirectory: .downloadsDirectory)
            do {
                try await store.add(url: targetURL, options: opts)
            } catch {
                NSLog("[AnyDownloader] Deep link add failed: \(error.localizedDescription)")
            }
        }
    }
}

extension Notification.Name {
    static let focusURLBar = Notification.Name("focusURLBar")
}
