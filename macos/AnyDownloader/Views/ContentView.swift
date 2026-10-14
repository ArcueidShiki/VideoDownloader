import SwiftUI
import AnyDownloaderCore

extension DownloadStore {
    static var preview: DownloadStore {
        // 尝试用内存存储 mock，避免真实文件操作
        return try! DownloadStore(persistencePath: ":memory:", maxConcurrent: 1)
    }
}

extension AppUpdateChecker {
    static var preview: AppUpdateChecker {
        let checker = AppUpdateChecker.shared
        checker.updateAvailable = true
        checker.latestVersion = "2.0.0"
        checker.releaseNotes = "- 新功能演示\n- 优化体验"
        return checker
    }
}

struct ContentView: View {
    @Environment(DownloadStore.self) private var store
    @State private var toolUpdates: [String] = []
    @State private var selectedFilter: FilterTab = .all
    @State private var urlText = ""
    @FocusState private var urlFocused: Bool
    @State private var urlIsFocused: Bool = false
    @State private var outputDirectory: URL = ContentView.loadSavedOutputDir()
    @State private var selectedIDs: Set<UUID> = []
    @State private var updateChecker = AppUpdateChecker.shared
    @State private var updateBannerDismissed = false
    /// Height of the top URL-bar pane. User-adjustable via the drag handle below it.
    @State private var controlPaneHeight: CGFloat = 90
    @State private var dragLastY: CGFloat = 0
#if DEBUG
    @State private var showDebugMonetization = false
#endif

    private var showUpdateBanner: Bool {
        updateChecker.updateAvailable && !updateBannerDismissed
    }

    var body: some View {
        VStack(spacing: 0) {
            if !toolUpdates.isEmpty {
                HStack {
                    Text("Optional tool updates: " + toolUpdates.joined(separator: ", ") +
                         ". After downloads finish, run brew upgrade yt-dlp deno in Terminal. No app restart is needed.")
                        .font(.caption).textSelection(.enabled)
                    Button("Dismiss") { toolUpdates = [] }
                }.padding(10).background(Color.accentColor.opacity(0.1))
            }
            // Update available banner — shown below toolbar when a new version is in the App Store
            if showUpdateBanner, let version = updateChecker.latestVersion {
                AppUpdateBannerView(
                    latestVersion: version,
                    onUpdate: {
                        updateChecker.openAppStorePage()
                        updateBannerDismissed = true
                    },
                    onDismiss: { updateBannerDismissed = true }
                )
            }
#if DEBUG
            // Debug-only banner with monetization test button
            HStack {
                Image(systemName: "wrench.and.screwdriver")
                    .font(.system(size: 10))
                    .foregroundColor(.orange)
                Text("DEBUG BUILD")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(.orange)
                Spacer()
                Button("Test Monetization Prompts") {
                    showDebugMonetization = true
                }
                .font(.system(size: 10))
                .buttonStyle(.borderless)
                .foregroundColor(.orange)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 4)
            .background(Color.orange.opacity(0.08))
            .sheet(isPresented: $showDebugMonetization) {
                DebugMonetizationView()
            }
            Divider()
#endif
            // ── Section 1: URL bar only ─────────────────────────────────────
            ToolbarURLBar(
                urlText: $urlText,
                urlFocused: $urlFocused,
                outputDirectory: $outputDirectory,
                selectedIDs: $selectedIDs
            )
            .frame(height: controlPaneHeight)
            .clipped()

            // ── Drag handle — resize between URL bar and tabs+list ──────────
            paneResizeHandle

            // ── Section 2: filter tabs + download list ──────────────────────
            TabFilterBar(selected: $selectedFilter, store: store)

            DownloadListView(
                filter: selectedFilter,
                outputDirectory: outputDirectory,
                selectedIDs: $selectedIDs,
                isURLFocused: urlIsFocused
            )
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .contentShape(Rectangle())
            .onTapGesture { urlFocused = false }

            Divider()
            StatusBar()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .onChange(of: urlFocused) { _, v in urlIsFocused = v }
        .onReceive(NotificationCenter.default.publisher(for: .focusURLBar)) { _ in
            urlFocused = true
        }
        .task { toolUpdates = await DependencyStatus.availableUpdates() }
        .onChange(of: outputDirectory) { _, url in
            UserDefaults.standard.set(url.path, forKey: "outputDirectory")
        }
        .task { await updateChecker.checkForUpdates() }
    }

    // MARK: - Pane resize handle

    @State private var handleHovered = false

    private var paneResizeHandle: some View {
        ZStack {
            // Background — brightens on hover to signal interactivity
            Rectangle()
                .fill(handleHovered
                      ? Color(NSColor.separatorColor).opacity(0.25)
                      : Color(NSColor.separatorColor).opacity(0.08))
                .frame(maxWidth: .infinity)
                .frame(height: 8)
                .animation(.easeInOut(duration: 0.15), value: handleHovered)

            // Grip indicator: three short horizontal lines (mimics macOS resize grips)
            HStack(spacing: 4) {
                ForEach(0..<3, id: \.self) { _ in
                    Capsule()
                        .fill(Color.secondary.opacity(handleHovered ? 0.7 : 0.4))
                        .frame(width: 18, height: 2)
                        .animation(.easeInOut(duration: 0.15), value: handleHovered)
                }
            }
        }
        .contentShape(Rectangle())
        .onHover { inside in
            handleHovered = inside
            if inside { NSCursor.resizeUpDown.push() } else { NSCursor.pop() }
        }
        .gesture(
            DragGesture(minimumDistance: 1)
                .onChanged { v in
                    let delta = v.translation.height - dragLastY
                    dragLastY = v.translation.height
                    controlPaneHeight = max(44, min(200, controlPaneHeight + delta))
                }
                .onEnded { _ in dragLastY = 0 }
        )
    }

    private static func loadSavedOutputDir() -> URL {
        guard let path = UserDefaults.standard.string(forKey: "outputDirectory") else {
            return URL.downloadsDirectory
        }
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue else {
            return URL.downloadsDirectory
        }
        return URL(fileURLWithPath: path)
    }
}

#Preview {
    ContentView()
}

