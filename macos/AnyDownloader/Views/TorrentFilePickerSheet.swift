import SwiftUI
import AnyDownloaderCore

struct TorrentFilePickerSheet: View {
    let item: DownloadItem
    let onStart: ([TorrentFileEntry], URL) -> Void

    @State private var files: [TorrentFileEntry]
    @State private var outputDirectory: URL = URL.downloadsDirectory
    @State private var duplicateConflict: DownloadItem? = nil
    @Environment(\.dismiss) private var dismiss
    @Environment(DownloadStore.self) private var store

    init(item: DownloadItem, onStart: @escaping ([TorrentFileEntry], URL) -> Void) {
        self.item = item
        self.onStart = onStart
        _files = State(initialValue: item.torrentFiles ?? [])
    }

    private var selectedCount: Int { files.filter(\.isSelected).count }
    private var totalSelectedBytes: Int64 {
        files.filter(\.isSelected).reduce(0) { $0 + $1.length }
    }

    var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Image(systemName: "arrow.triangle.2.circlepath")
                    .font(.title2)
                    .foregroundColor(.green)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.title ?? "Torrent")
                        .font(.headline)
                        .lineLimit(1)
                    Text("\(files.count) files · \(ByteCountFormatter.string(fromByteCount: files.reduce(0) { $0 + $1.length }, countStyle: .binary)) total")
                        .font(.caption)
                        .foregroundColor(.secondary)
                }
                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.top, 16)
            .padding(.bottom, 12)

            Divider()

            // File list
            if files.isEmpty {
                Text("No files found")
                    .foregroundColor(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                // Select All / None row
                HStack {
                    Button(selectedCount == files.count ? "Deselect All" : "Select All") {
                        let selectAll = selectedCount < files.count
                        files = files.map { TorrentFileEntry(index: $0.index, path: $0.path, length: $0.length, isSelected: selectAll) }
                    }
                    .buttonStyle(.plain)
                    .font(.system(size: 12))
                    .foregroundColor(.accentColor)
                    Spacer()
                    Text("\(selectedCount) of \(files.count) selected · \(ByteCountFormatter.string(fromByteCount: totalSelectedBytes, countStyle: .binary))")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 6)
                .background(Color(NSColor.controlBackgroundColor).opacity(0.6))

                Divider()

                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(files.indices, id: \.self) { i in
                            FileRow(entry: files[i]) { toggled in
                                files[i] = toggled
                            }
                            if i < files.count - 1 {
                                Divider().padding(.leading, 38)
                            }
                        }
                    }
                    .padding(.vertical, 4)
                }
                .frame(maxHeight: 320)
            }

            Divider()

            // Destination + action row
            VStack(spacing: 10) {
                HStack(spacing: 8) {
                    Image(systemName: "folder")
                        .foregroundColor(.secondary)
                        .font(.system(size: 13))
                    Text(outputDirectory.abbreviatingWithTilde)
                        .font(.system(size: 12))
                        .foregroundColor(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Spacer()
                    Button("Change…") { pickFolder() }
                        .font(.system(size: 12))
                }
                .padding(.horizontal, 16)

                HStack(spacing: 10) {
                    Button("Cancel") { dismiss() }
                        .keyboardShortcut(.escape)

                    Spacer()

                    Button("Download \(selectedCount) file\(selectedCount == 1 ? "" : "s")") {
                        // Check for a torrent with the same URL already downloading to same folder
                        if let conflict = store.findDuplicate(
                            url: item.url,
                            outputDir: outputDirectory,
                            excludingId: item.id
                        ) {
                            duplicateConflict = conflict
                        } else {
                            onStart(files, outputDirectory)
                            dismiss()
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(selectedCount == 0)
                    .keyboardShortcut(.return)
                }
                .padding(.horizontal, 16)
            }
            .padding(.vertical, 12)
        }
        .frame(width: 480)
        .confirmationDialog(
            "Duplicate Download",
            isPresented: Binding(
                get: { duplicateConflict != nil },
                set: { if !$0 { duplicateConflict = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button("Replace Existing") {
                if let old = duplicateConflict { store.remove(id: old.id) }
                duplicateConflict = nil
                onStart(files, outputDirectory)
                dismiss()
            }
            Button("Keep Both") {
                duplicateConflict = nil
                onStart(files, outputDirectory)
                dismiss()
            }
            Button("Cancel", role: .cancel) { duplicateConflict = nil }
        } message: {
            Text("A torrent download for this resource to the same folder already exists. What would you like to do?")
        }
    }

    private func pickFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Select Folder"
        if panel.runModal() == .OK, let url = panel.url {
            outputDirectory = url
        }
    }
}

// MARK: - FileRow

private struct FileRow: View {
    let entry: TorrentFileEntry
    let onToggle: (TorrentFileEntry) -> Void

    var body: some View {
        HStack(spacing: 10) {
            Toggle("", isOn: Binding(
                get: { entry.isSelected },
                set: { _ in onToggle(TorrentFileEntry(index: entry.index, path: entry.path, length: entry.length, isSelected: !entry.isSelected)) }
            ))
            .toggleStyle(.checkbox)
            .labelsHidden()

            VStack(alignment: .leading, spacing: 1) {
                Text(entry.path.components(separatedBy: "/").last ?? entry.path)
                    .font(.system(size: 12))
                    .lineLimit(1)
                if entry.path.contains("/") {
                    Text(entry.path.components(separatedBy: "/").dropLast().joined(separator: "/"))
                        .font(.system(size: 10))
                        .foregroundColor(.secondary)
                        .lineLimit(1)
                }
            }

            Spacer()

            Text(ByteCountFormatter.string(fromByteCount: entry.length, countStyle: .binary))
                .font(.system(size: 11))
                .foregroundColor(.secondary)
                .monospacedDigit()
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 7)
        .contentShape(Rectangle())
        .onTapGesture {
            onToggle(TorrentFileEntry(index: entry.index, path: entry.path, length: entry.length, isSelected: !entry.isSelected))
        }
        .background(entry.isSelected ? Color.clear : Color(NSColor.controlBackgroundColor).opacity(0.4))
    }
}

// MARK: - URL helper

private extension URL {
    var abbreviatingWithTilde: String {
        path.replacingOccurrences(
            of: FileManager.default.homeDirectoryForCurrentUser.path,
            with: "~"
        )
    }
}
