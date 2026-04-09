import SwiftUI
import UniformTypeIdentifiers
import AnyDownloaderCore

struct ToolbarURLBar: View {
    @Binding var urlText: String
    var urlFocused: FocusState<Bool>.Binding
    @Binding var outputDirectory: URL
    @Binding var selectedIDs: Set<UUID>
    @Environment(DownloadStore.self) private var store
    @State private var isAdding = false
    @State private var errorMessage: String?
    @State private var showQuotaExceeded = false
    /// Set when an HTTP URL being added collides with an existing in-progress download.
    @State private var httpDuplicateURL: String?
    @State private var httpDuplicateExistingID: UUID?
    private let quota = DownloadQuotaService.shared

    // When a selection is active, button logic is scoped to those items only.
    private var effectiveItems: [DownloadItem] {
        selectedIDs.isEmpty
            ? store.items
            : store.items.filter { selectedIDs.contains($0.id) }
    }
    private var hasActive: Bool    { effectiveItems.contains { $0.state == .active } }
    private var hasStartable: Bool { effectiveItems.contains { $0.state == .queued || $0.state == .paused } }
    private var pausedCount: Int   { effectiveItems.filter { $0.state == .paused }.count }

    // Gradient border colors — blue→purple when focused, muted gray when blurred
    private var borderGradient: LinearGradient {
        urlFocused.wrappedValue
            ? LinearGradient(
                colors: [Color.accentColor, Color.purple],
                startPoint: .leading,
                endPoint: .trailing
              )
            : LinearGradient(
                colors: [Color(nsColor: .separatorColor), Color(nsColor: .separatorColor)],
                startPoint: .leading,
                endPoint: .trailing
              )
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                // Styled URL input field
                styledURLField

                // Browse .torrent file
                Button {
                    let panel = NSOpenPanel()
                    if let type = UTType(filenameExtension: "torrent") {
                        panel.allowedContentTypes = [type]
                    }
                    panel.message = "Select a .torrent file"
                    if panel.runModal() == .OK, let url = panel.url {
                        urlText = url.path
                    }
                } label: {
                    Image(systemName: "paperclip")
                        .font(.system(size: 14))
                }
                .help("Browse for a .torrent file")

                // Add button — no .keyboardShortcut(.return) to avoid conflicting with .onSubmit
                Button("Add") { Task { await addDownload() } }
                    .buttonStyle(.borderedProminent)
                    .disabled(urlText.trimmingCharacters(in: .whitespaces).isEmpty || isAdding)

                Divider().frame(height: 22)

                // Global Play / Pause All toggle
                playPauseAllButton

                // Clear selection badge (visible when items are selected)
                if !selectedIDs.isEmpty {
                    Button {
                        selectedIDs.removeAll()
                    } label: {
                        Text("\(selectedIDs.count) selected")
                            .font(.system(size: 11))
                            .foregroundColor(.accentColor)
                        Image(systemName: "xmark.circle.fill")
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                    .buttonStyle(.plain)
                    .help("Clear selection")
                }
            }
            .padding(.horizontal, 12)
            .padding(.top, 8)
            .padding(.bottom, 4)

            // Save-to row
            HStack(spacing: 6) {
                Image(systemName: "folder")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                Text("Default save to:")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                Button(outputDirectory.abbreviatingWithTilde) { pickFolder() }
                    .buttonStyle(.plain)
                    .font(.system(size: 11))
                    .foregroundColor(.accentColor)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Spacer()
            }
            .padding(.horizontal, 14)
            .padding(.bottom, 7)
        }
        .alert("Error", isPresented: .constant(errorMessage != nil), actions: {
            Button("OK") { errorMessage = nil }
        }, message: { Text(errorMessage ?? "") })
        .sheet(isPresented: $showQuotaExceeded) { QuotaExceededSheet() }
        .confirmationDialog(
            "Duplicate Download",
            isPresented: Binding(
                get: { httpDuplicateURL != nil },
                set: { if !$0 { httpDuplicateURL = nil; httpDuplicateExistingID = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button("Replace Existing") {
                guard let pendingURL = httpDuplicateURL else { return }
                if let oldID = httpDuplicateExistingID { store.remove(id: oldID) }
                httpDuplicateURL = nil
                httpDuplicateExistingID = nil
                Task { await proceedAdd(url: pendingURL) }
            }
            Button("Keep Both") {
                guard let pendingURL = httpDuplicateURL else { return }
                httpDuplicateURL = nil
                httpDuplicateExistingID = nil
                Task { await proceedAdd(url: pendingURL) }
            }
            Button("Cancel", role: .cancel) {
                httpDuplicateURL = nil
                httpDuplicateExistingID = nil
            }
        } message: {
            Text("This URL is already in the download list. What would you like to do?")
        }
    }

    // MARK: - Styled URL TextField

    @ViewBuilder
    private var styledURLField: some View {
        ZStack {
            // Gradient background: window background → slightly lighter
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [
                            Color(nsColor: .textBackgroundColor),
                            Color(nsColor: .textBackgroundColor).opacity(0.85)
                        ],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )

            TextField(
                "Paste a URL, YouTube/video link, magnet URI, or .torrent path… (⌘N)",
                text: $urlText
            )
            .font(.system(size: 15))
            .textFieldStyle(.plain)
            .focused(urlFocused)
            .onSubmit { Task { await addDownload() } }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
        }
        .frame(minHeight: 44)
        // Gradient border via overlay
        .overlay(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .strokeBorder(borderGradient, lineWidth: urlFocused.wrappedValue ? 1.5 : 1.0)
        )
        // Soft glow when focused
        .shadow(
            color: urlFocused.wrappedValue ? .accentColor.opacity(0.35) : .clear,
            radius: 6,
            x: 0,
            y: 0
        )
        .animation(.easeInOut(duration: 0.18), value: urlFocused.wrappedValue)
    }

    // MARK: - Play/Pause All

    @ViewBuilder
    private var playPauseAllButton: some View {
        let ids: Set<UUID>? = selectedIDs.isEmpty ? nil : selectedIDs

        if hasActive {
            Button {
                Task { await store.pauseAll(ids: ids) }
            } label: {
                Label("Pause All", systemImage: "pause.fill").labelStyle(.iconOnly)
            }
            .help(selectedIDs.isEmpty ? "Pause all active downloads" : "Pause selected active downloads")
            .buttonStyle(.bordered)
        } else if hasStartable {
            Button {
                Task { await store.startOrResumeAll(outputDirectory: outputDirectory, ids: ids) }
            } label: {
                Label("Start / Resume All", systemImage: "play.fill").labelStyle(.iconOnly)
            }
            .help(pausedCount > 0
                  ? "Resume \(pausedCount) paused + start queued"
                  : "Start all queued downloads")
            .buttonStyle(.bordered)
            .tint(.green)
        } else {
            Button { } label: {
                Label("Play", systemImage: "play.fill").labelStyle(.iconOnly)
            }
            .buttonStyle(.bordered)
            .disabled(true)
            .help(selectedIDs.isEmpty ? "No queued or paused downloads" : "No queued or paused in selection")
        }
    }

    // MARK: - Actions

    private func addDownload() async {
        let url = urlText.trimmingCharacters(in: .whitespaces)
        guard !url.isEmpty else { return }

        // Check for HTTP duplicate BEFORE consuming a quota slot — no penalty for duplication.
        // (ytdlp and torrent duplicates are checked later in their respective picker sheets.)
        if let conflict = store.findDuplicate(url: url) {
            // Present the dialog; the user's choice will call proceedAdd() or cancel.
            httpDuplicateURL = url
            httpDuplicateExistingID = conflict.id
            return
        }

        await proceedAdd(url: url)
    }

    /// Consumes a quota slot and calls store.add(). Called after any duplicate resolution.
    private func proceedAdd(url: String) async {
        guard quota.consumeSlot() else {
            showQuotaExceeded = true
            return
        }

        isAdding = true
        defer { isAdding = false }
        do {
            let opts = DownloadOptions(outputDirectory: outputDirectory)
            try await store.add(url: url, options: opts)
            urlText = ""
        } catch DownloadStoreError.duplicateURL {
            // Shouldn't happen (HTTP duplicate was already resolved above), but just in case.
            quota.refundSlot()
            urlText = ""
        } catch {
            quota.refundSlot()
            errorMessage = error.localizedDescription
        }
    }

    private func pickFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Select Folder"
        panel.message = "Choose default download folder"
        if panel.runModal() == .OK, let url = panel.url {
            outputDirectory = url
        }
    }
}

#Preview {
    @Previewable @State var urlText = ""
    @Previewable @State var outputDir = URL.downloadsDirectory
    @Previewable @State var selectedIDs = Set<UUID>()
    @Previewable @FocusState var focused: Bool
    ToolbarURLBar(
        urlText: $urlText,
        urlFocused: $focused,
        outputDirectory: $outputDir,
        selectedIDs: $selectedIDs
    )
    .environment(DownloadStore.preview)
    .frame(width: 600)
}

// MARK: - URL display helper

private extension URL {
    var abbreviatingWithTilde: String {
        path.replacingOccurrences(
            of: FileManager.default.homeDirectoryForCurrentUser.path,
            with: "~"
        )
    }
}
