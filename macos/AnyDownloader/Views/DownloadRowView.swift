import SwiftUI
import AnyDownloaderCore

struct DownloadRowView: View {
    let item: DownloadItem
    let outputDirectory: URL
    let isSelected: Bool
    let onToggleSelect: (UUID) -> Void
    @Environment(DownloadStore.self) private var store

    @State private var showingFilePicker     = false
    @State private var showingFormatPicker   = false
    @State private var showingDeleteConfirm  = false
    @State private var isTorrentExpanded     = false

    var body: some View {
        VStack(spacing: 0) {
            // ── Main row ──────────────────────────────────────────────────────
            HStack(spacing: 10) {
                // Selection indicator — visual only; row tap gesture handles toggling
                selectionIndicator

                // Thumbnail
                itemThumbnail
                    .frame(width: 56, height: 36)
                    .clipShape(RoundedRectangle(cornerRadius: 7))

                // Title / detail / state
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 5) {
                        // Source badge (e.g. "YouTube", "Bilibili", "GitHub")
                        Text(sourceLabel)
                            .font(.system(size: 9, weight: .semibold))
                            .foregroundColor(.white)
                            .padding(.horizontal, 5)
                            .padding(.vertical, 2)
                            .background(sourceBadgeColor.opacity(0.85))
                            .clipShape(RoundedRectangle(cornerRadius: 4))

                        Text(displayTitle)
                            .font(.system(size: 13, weight: .semibold))
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }

                    if let detail = formatDetailLine {
                        Text(detail)
                            .font(.system(size: 10))
                            .foregroundColor(.secondary)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }

                    stateRow
                }

                Spacer()

                // Right side: status + buttons
                VStack(alignment: .trailing, spacing: 4) {
                    rightSideStatus
                    // Always exactly 3 (+ optional expand) buttons
                    HStack(spacing: 4) {
                        primaryActionButton
                        playButton
                        folderButton
                        // Expand/collapse for torrents that have a known file list
                        if item.type == .torrent,
                           let files = item.torrentFiles, !files.isEmpty {
                            actionBtn(
                                icon: isTorrentExpanded ? "chevron.up" : "chevron.down",
                                color: .secondary
                            ) { isTorrentExpanded.toggle() }
                        }
                        deleteButton
                    }
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)

            // ── Torrent file list (collapsible) ────────────────────────────────
            if isTorrentExpanded, let files = item.torrentFiles, !files.isEmpty {
                Divider()
                    .padding(.horizontal, 14)
                torrentFileList(files)
                    .padding(.horizontal, 22)
                    .padding(.vertical, 8)
            }
        }
        .background(
            isSelected
                ? Color.accentColor.opacity(0.08)
                : Color(NSColor.controlBackgroundColor)
        )
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .stroke(isSelected ? Color.accentColor : Color.clear, lineWidth: 1.5)
        )
        .opacity(item.state == .paused ? 0.78 : 1.0)
        // Whole-row tap selects / deselects this item.
        // SwiftUI Buttons inside absorb their own taps and don't propagate here.
        .contentShape(Rectangle())
        .onTapGesture { onToggleSelect(item.id) }
        .onChange(of: item.state) { _, newState in
            if newState == .selectingFiles  { showingFilePicker   = true }
            if newState == .selectingFormat { showingFormatPicker = true }
            if newState == .completed       { SoundManager.shared.play("complete") }
            if newState == .failed          { SoundManager.shared.play("error") }
        }
        .sheet(isPresented: $showingFilePicker) {
            if let files = item.torrentFiles {
                TorrentFilePickerSheet(item: item.with(torrentFiles: files)) { selected, outputDir in
                    Task {
                        await store.startTorrentDownload(
                            id: item.id, selectedFiles: selected, outputDirectory: outputDir)
                    }
                }
            }
        }
        .sheet(isPresented: $showingFormatPicker) {
            URLInfoPreviewSheet(item: item,
                                outputDirectory: item.itemOutputDirectory ?? outputDirectory)
                .environment(store)
        }
        .confirmationDialog("Remove Download",
                            isPresented: $showingDeleteConfirm,
                            titleVisibility: .visible) {
            Button("Remove Task Only", role: .destructive) {
                store.remove(id: item.id)
            }
            if let path = item.filePath, isFile(path) {
                Button("Remove Task and Delete File", role: .destructive) {
                    try? FileManager.default.removeItem(atPath: path)
                    store.remove(id: item.id)
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("What would you like to do with this download?")
        }
    }

    // MARK: - Selection indicator (visual only — row tap gesture handles toggling)

    private var selectionIndicator: some View {
        Image(systemName: isSelected ? "checkmark.circle.fill" : "circle")
            .font(.system(size: 15))
            .foregroundColor(isSelected ? .accentColor : Color.secondary.opacity(0.35))
            .frame(width: 20, height: 20)
            .animation(.easeInOut(duration: 0.15), value: isSelected)
    }

    // MARK: - Thumbnail

    @ViewBuilder
    private var itemThumbnail: some View {
        ZStack {
            typePlaceholder

            if let urlString = item.videoInfo?.thumbnailURL,
               let url = URL(string: urlString) {
                AsyncImage(url: url) { phase in
                    if case .success(let image) = phase {
                        image.resizable().aspectRatio(contentMode: .fill)
                    }
                }
            }

            // Spinning overlay when torrent is fetching metadata or active with no progress yet
            if item.type == .torrent &&
               (item.state == .active || item.state == .fetchingMetadata) &&
               item.progress < 0.01 {
                Color.black.opacity(0.4)
                ProgressView()
                    .progressViewStyle(.circular)
                    .scaleEffect(0.6)
                    .tint(.white)
            }
        }
    }

    @ViewBuilder
    private var typePlaceholder: some View {
        switch item.type {
        case .http:
            LinearGradient(colors: [.blue, Color(hue: 0.6, saturation: 0.9, brightness: 0.8)],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            Image(systemName: "arrow.down.circle.fill").font(.system(size: 16)).foregroundColor(.white)
        case .ytdlp:
            LinearGradient(colors: [.red, Color(hue: 0.0, saturation: 0.9, brightness: 0.7)],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            Image(systemName: "play.rectangle.fill").font(.system(size: 16)).foregroundColor(.white)
        case .torrent:
            LinearGradient(colors: [.green, Color(hue: 0.35, saturation: 0.9, brightness: 0.6)],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            Image(systemName: "arrow.triangle.2.circlepath").font(.system(size: 16)).foregroundColor(.white)
        }
    }

    // MARK: - Torrent file list

    @ViewBuilder
    private func torrentFileList(_ files: [TorrentFileEntry]) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            ForEach(files) { file in
                HStack(spacing: 6) {
                    Image(systemName: file.isSelected ? "checkmark.square.fill" : "square")
                        .font(.system(size: 11))
                        .foregroundColor(file.isSelected ? .accentColor : .secondary.opacity(0.5))
                    Text(URL(fileURLWithPath: file.path).lastPathComponent)
                        .font(.system(size: 11))
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Spacer()
                    Text(ByteCountFormatter.string(fromByteCount: file.length, countStyle: .binary))
                        .font(.system(size: 10))
                        .foregroundColor(.secondary)
                        .monospacedDigit()
                }
            }
        }
    }

    // MARK: - State row

    @ViewBuilder
    private var stateRow: some View {
        switch item.state {
        case .fetchingInfo:
            HStack(spacing: 5) {
                ProgressView().scaleEffect(0.55).frame(width: 14, height: 14)
                Text("Fetching video info…").font(.system(size: 11)).foregroundColor(.secondary)
            }
        case .selectingFormat:
            HStack(spacing: 5) {
                Image(systemName: "hand.tap").font(.system(size: 10)).foregroundColor(.accentColor)
                Text("Tap ✏ to choose resolution").font(.system(size: 11)).foregroundColor(.accentColor)
            }
        case .fetchingMetadata:
            HStack(spacing: 5) {
                ProgressView().scaleEffect(0.55).frame(width: 14, height: 14)
                Text("Fetching torrent file list…").font(.system(size: 11)).foregroundColor(.secondary)
            }
        case .selectingFiles:
            HStack(spacing: 5) {
                Image(systemName: "hand.tap").font(.system(size: 10)).foregroundColor(.orange)
                Text("Tap ☰ to select files").font(.system(size: 11)).foregroundColor(.orange)
            }
        case .queued:
            HStack(spacing: 4) {
                Image(systemName: "clock").font(.system(size: 10)).foregroundColor(.secondary)
                Text(destinationLabel).font(.system(size: 11)).foregroundColor(.secondary)
            }
        case .completed:
            Text("✓  Completed")
                .font(.system(size: 11)).foregroundColor(.green)
        case .failed:
            Text(item.errorMessage ?? "Failed")
                .font(.system(size: 11)).foregroundColor(.red).lineLimit(2)
        default:
            VStack(alignment: .leading, spacing: 2) {
                ProgressView(value: item.progress)
                    .progressViewStyle(.linear)
                    .tint(progressColor)
            }
        }
    }

    // MARK: - Right side status

    @ViewBuilder
    private var rightSideStatus: some View {
        switch item.state {
        case .completed:
            Text("Done").font(.system(size: 12, weight: .semibold)).foregroundColor(.green)
        case .active:
            VStack(alignment: .trailing, spacing: 2) {
                Text(percentText).font(.system(size: 12, weight: .semibold))
                Text(speedText).font(.system(size: 11)).foregroundColor(.secondary)
            }
        case .paused:
            Text(percentText).font(.system(size: 12, weight: .semibold)).foregroundColor(.orange)
        default:
            EmptyView()
        }
    }

    // MARK: - Buttons

    /// Button 1: context-aware primary action
    @ViewBuilder
    private var primaryActionButton: some View {
        switch item.state {
        case .active:
            actionBtn(icon: "pause.fill", color: .secondary) {
                Task { await store.pause(id: item.id) }
            }
        case .paused:
            actionBtn(icon: "play.fill", color: .green) {
                store.resume(id: item.id,
                             outputDirectory: item.itemOutputDirectory ?? outputDirectory)
            }
        case .queued:
            actionBtn(icon: "play.fill", color: .accentColor) {
                Task { await store.startAllQueued(
                    outputDirectory: item.itemOutputDirectory ?? outputDirectory) }
            }
        case .completed:
            actionBtn(icon: "arrow.counterclockwise", color: .accentColor) {
                Task { await store.restart(id: item.id,
                                           outputDirectory: item.itemOutputDirectory ?? outputDirectory) }
            }
        case .selectingFormat:
            actionBtn(icon: "square.and.pencil", color: .accentColor) {
                showingFormatPicker = true
            }
        case .selectingFiles:
            actionBtn(icon: "list.bullet", color: .orange) {
                showingFilePicker = true
            }
        case .failed:
            actionBtn(icon: "arrow.counterclockwise", color: .accentColor) {
                Task { await store.restart(id: item.id,
                                           outputDirectory: item.itemOutputDirectory ?? outputDirectory) }
            }
        default:
            actionBtn(icon: "xmark", color: .secondary) {
                Task { await store.cancel(id: item.id) }
            }
        }
    }

    /// Button 2: play completed file in system default player
    @ViewBuilder
    private var playButton: some View {
        if item.state == .completed, let path = item.filePath {
            actionBtn(icon: "play.circle", color: .accentColor) {
                NSWorkspace.shared.open(URL(fileURLWithPath: path))
            }
        }
    }

    /// Button 3: "Show in Finder" when done (selects the file if known, otherwise opens dir),
    ///            "Pick folder" when not yet completed.
    @ViewBuilder
    private var folderButton: some View {
        if item.state == .completed {
            actionBtn(icon: "folder", color: .green) {
                if let path = item.filePath, isFile(path) {
                    NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)])
                } else {
                    let dir = item.itemOutputDirectory ?? outputDirectory
                    NSWorkspace.shared.open(dir)
                }
            }
        } else {
            actionBtn(icon: "folder.badge.plus", color: .secondary) {
                pickItemFolder()
            }
        }
    }

    /// Button 3: always delete/remove
    private var deleteButton: some View {
        actionBtn(icon: "trash", color: .red.opacity(0.7)) {
            showingDeleteConfirm = true
        }
    }

    // MARK: - Per-task folder picker

    private func pickItemFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Select"
        panel.message = "Choose destination for this download"
        if panel.runModal() == .OK, let url = panel.url {
            store.setOutputDirectory(url, for: item.id)
        }
    }

    // MARK: - Helpers

    private var destinationLabel: String {
        let dir = item.itemOutputDirectory ?? outputDirectory
        return dir.path.replacingOccurrences(
            of: FileManager.default.homeDirectoryForCurrentUser.path, with: "~")
    }

    private var formatDetailLine: String? {
        let dir = item.itemOutputDirectory ?? outputDirectory
        let dirStr = dir.path.replacingOccurrences(
            of: FileManager.default.homeDirectoryForCurrentUser.path, with: "~")

        switch item.type {
        case .ytdlp:
            var parts: [String] = []
            if let info = item.videoInfo, let fid = item.selectedFormatId {
                let baseId = fid.components(separatedBy: "+").first ?? fid
                if let vfmt = info.sortedVideoFormats.first(where: { $0.formatId == baseId }) {
                    parts.append(vfmt.qualityLabel)
                    parts.append(vfmt.container.uppercased())
                } else if let afmt = info.audioFormats.first(where: { $0.formatId == baseId }) {
                    parts.append(afmt.label)
                }
            }
            if let filename = item.customFilename { parts.append("\"\(filename)\"") }
            parts.append(dirStr)
            return parts.isEmpty ? nil : parts.joined(separator: "  ·  ")
        case .http:
            var parts = [dirStr]
            if let total = item.totalBytes {
                parts.insert(ByteCountFormatter.string(fromByteCount: total, countStyle: .binary), at: 0)
            }
            return parts.joined(separator: "  ·  ")
        case .torrent:
            return dirStr
        }
    }

    private var displayTitle: String {
        if let t = item.title, !t.isEmpty { return t }
        if item.type == .http,
           let url = URL(string: item.url),
           !url.lastPathComponent.isEmpty { return url.lastPathComponent }
        return URL(string: item.url)?.host.map { "\($0)\(URL(string: item.url)?.path ?? "")" } ?? item.url
    }

    // private var metaLine: String {
    //     if let total = item.totalBytes {
    //         return ByteCountFormatter.string(fromByteCount: total, countStyle: .binary)
    //     }
    //     return ""
    // }

    private var percentText: String { "\(Int(item.progress * 100))%" }

    private var speedText: String {
        let s = ByteCountFormatter.string(fromByteCount: item.speedBytesPerSec,
                                          countStyle: .binary) + "/s"
        if let eta = item.etaSeconds {
            return "\(s) · \(eta / 60):\(String(format: "%02d", eta % 60))"
        }
        return s
    }

    private var progressColor: Color {
        item.state == .paused ? .orange : .accentColor
    }

    private func isFile(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && !isDir.boolValue
    }

    private func actionBtn(icon: String, color: Color,
                           action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 11))
                .frame(width: 24, height: 24)
                .foregroundColor(color)
                .background(color.opacity(0.12))
                .clipShape(RoundedRectangle(cornerRadius: 6))
        }
        .buttonStyle(.plain)
    }

    // MARK: - Source badge

    private var sourceLabel: String {
        if item.url.lowercased().hasPrefix("magnet:") { return "Torrent" }
        guard let url = URL(string: item.url) else { return "HTTP" }
        let host = (url.host ?? "")
            .lowercased()
            .replacingOccurrences(of: "www.", with: "")
            .replacingOccurrences(of: "m.", with: "")
        switch true {
        case host.contains("youtube"), host == "youtu.be":  return "YouTube"
        case host.contains("bilibili"):                      return "Bilibili"
        case host.contains("tiktok"), host.contains("douyin"): return "TikTok"
        case host.contains("twitter"), host == "x.com":     return "Twitter/X"
        case host.contains("instagram"):                     return "Instagram"
        case host.contains("vimeo"):                         return "Vimeo"
        case host.contains("twitch"):                        return "Twitch"
        case host.contains("facebook"):                      return "Facebook"
        case host.contains("nicovideo"):                     return "NicoVideo"
        case host.contains("soundcloud"):                    return "SoundCloud"
        case host.contains("github"):                        return "GitHub"
        default:
            let parts = host.split(separator: ".")
            guard parts.count >= 2 else { return host.isEmpty ? "HTTP" : host }
            let name = String(parts[parts.count - 2])
            return name.prefix(1).uppercased() + name.dropFirst()
        }
    }

    private var sourceBadgeColor: Color {
        switch sourceLabel {
        case "YouTube":    return .red
        case "Bilibili":   return Color(red: 0.00, green: 0.63, blue: 0.84) // #00A1D6
        case "TikTok":     return .black
        case "Twitter/X":  return Color(red: 0.10, green: 0.10, blue: 0.10)
        case "Instagram":  return Color(red: 0.73, green: 0.16, blue: 0.73)
        case "Vimeo":      return Color(red: 0.07, green: 0.72, blue: 0.46)
        case "Twitch":     return Color(red: 0.57, green: 0.26, blue: 0.98)
        case "Facebook":   return Color(red: 0.23, green: 0.35, blue: 0.60)
        case "SoundCloud": return Color(red: 1.00, green: 0.43, blue: 0.00)
        case "NicoVideo":  return Color(red: 0.00, green: 0.46, blue: 0.80)
        case "GitHub":     return Color(red: 0.15, green: 0.15, blue: 0.15)
        case "Torrent":    return .green
        default:
            switch item.type {
            case .ytdlp:   return .red
            case .torrent: return .green
            case .http:    return .blue
            }
        }
    }
}

#Preview {
    let store = DownloadStore.preview
    let item = DownloadItem(url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ", type: .ytdlp)
    DownloadRowView(
        item: item,
        outputDirectory: URL(fileURLWithPath: NSHomeDirectory() + "/Downloads"),
        isSelected: false,
        onToggleSelect: { _ in }
    )
    .environment(store)
    .frame(width: 600)
    .padding()
}
