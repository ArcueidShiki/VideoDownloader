import SwiftUI
import AnyDownloaderCore

struct URLInfoPreviewSheet: View {
    let item: DownloadItem
    @Environment(DownloadStore.self) private var store
    @Environment(\.dismiss) private var dismiss

    @State private var includeVideo: Bool = true
    @State private var includeAudio: Bool = true
    @State private var mergeStreams: Bool = true
    @State private var selectedVideoFormat: VideoFormat?
    @State private var selectedAudioFormat: AudioFormat?
    @State private var customFilename: String = ""
    @State private var saveDirectory: URL
    @State private var showCreateFolderAlert = false
    @State private var duplicateConflict: DownloadItem? = nil

    init(item: DownloadItem, outputDirectory: URL) {
        self.item = item
        self._saveDirectory = State(initialValue: outputDirectory)
        self._selectedVideoFormat = State(initialValue: item.videoInfo?.sortedVideoFormats.first)
        self._selectedAudioFormat = State(initialValue: item.videoInfo?.audioFormats.first)
        let title = item.videoInfo?.title ?? item.title ?? "download"
        let safe = Self.safeName(title)
        if let fmt = item.videoInfo?.sortedVideoFormats.first {
            self._customFilename = State(initialValue: "\(safe) \(fmt.qualityLabel).mp4")
        } else {
            self._customFilename = State(initialValue: "\(safe).mp4")
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            downloadModeSection
            Divider()
            saveOptionsSection
            Divider()
            footer
        }
        .frame(width: 580)
        .onChange(of: selectedVideoFormat?.id) { _, _ in updateDefaultFilename() }
        .onChange(of: selectedAudioFormat?.id) { _, _ in updateDefaultFilename() }
        .onChange(of: includeVideo) { _, _ in updateDefaultFilename() }
        .onChange(of: includeAudio) { _, _ in updateDefaultFilename() }
        .onChange(of: mergeStreams) { _, _ in updateDefaultFilename() }
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
                addToQueue()
            }
            Button("Create A New Download") {
                duplicateConflict = nil
                addToQueue()
            }
            Button("Cancel", role: .cancel) { duplicateConflict = nil }
        } message: {
            Text("A download for this video with the same format and filename already exists. What would you like to do?")
        }
        .alert("Folder Doesn't Exist", isPresented: $showCreateFolderAlert) {
            Button("Create Folder") {
                do {
                    try FileManager.default.createDirectory(
                        at: saveDirectory,
                        withIntermediateDirectories: true
                    )
                    addToQueue()
                } catch {
                    addToQueue()
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("\"\(saveDirectory.abbreviatingWithTilde)\" doesn't exist. Create it?")
        }
    }

    // MARK: - Header

    private var header: some View {
        HStack(alignment: .top, spacing: 14) {
            thumbnail
                .frame(width: 120, height: 68)
                .clipShape(RoundedRectangle(cornerRadius: 8))

            VStack(alignment: .leading, spacing: 4) {
                Text(item.videoInfo?.title ?? item.title ?? item.url)
                    .font(.system(size: 14, weight: .semibold))
                    .lineLimit(2)

                HStack(spacing: 8) {
                    if let uploader = item.videoInfo?.uploader {
                        Text(uploader)
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                    if let duration = item.videoInfo?.duration {
                        Text(formatDuration(duration))
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                    }
                }
                Text(hostLabel)
                    .font(.system(size: 11))
                    .foregroundColor(.accentColor)
            }
            Spacer()
        }
        .padding(14)
    }

    @ViewBuilder
    private var thumbnail: some View {
        if let urlString = item.videoInfo?.thumbnailURL,
           let url = URL(string: urlString) {
            AsyncImage(url: url) { phase in
                switch phase {
                case .success(let image):
                    image.resizable().aspectRatio(contentMode: .fill)
                case .failure:
                    thumbnailPlaceholder
                default:
                    ProgressView()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.secondary.opacity(0.15))
                }
            }
        } else {
            thumbnailPlaceholder
        }
    }

    private var thumbnailPlaceholder: some View {
        ZStack {
            LinearGradient(colors: [.red, Color(hue: 0.0, saturation: 0.9, brightness: 0.7)],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            Image(systemName: "play.rectangle.fill")
                .font(.system(size: 28))
                .foregroundColor(.white.opacity(0.8))
        }
    }

    // MARK: - Download Mode

    private var downloadModeSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            // ── Video ──────────────────────────────────────────────────────────
            VStack(alignment: .leading, spacing: 8) {
                Toggle(isOn: $includeVideo) {
                    Label("Video", systemImage: "video")
                        .font(.system(size: 13, weight: .semibold))
                }
                .toggleStyle(.checkbox)

                if includeVideo {
                    videoFormatPicker
                        .padding(.leading, 22)
                }
            }

            Divider()

            // ── Audio ──────────────────────────────────────────────────────────
            VStack(alignment: .leading, spacing: 8) {
                Toggle(isOn: $includeAudio) {
                    Label("Audio", systemImage: "music.note")
                        .font(.system(size: 13, weight: .semibold))
                }
                .toggleStyle(.checkbox)

                if includeAudio {
                    audioFormatPicker
                        .padding(.leading, 22)
                }
            }

            // ── Merge ──────────────────────────────────────────────────────────
            if includeVideo && includeAudio {
                Divider()
                Toggle(isOn: $mergeStreams) {
                    VStack(alignment: .leading, spacing: 2) {
                        Label("Merge into MP4", systemImage: "arrow.triangle.merge")
                            .font(.system(size: 13, weight: .semibold))
                        Text(mergeStreams
                             ? "Video and audio will be combined into a single .mp4 file using FFmpeg."
                             : "Video and audio tracks will be kept in their native container (no post-processing).")
                            .font(.system(size: 11))
                            .foregroundColor(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .toggleStyle(.checkbox)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: - Video format chips

    private var videoFormatPicker: some View {
        let formats = item.videoInfo?.sortedVideoFormats ?? []
        return VStack(alignment: .leading, spacing: 8) {
            if formats.isEmpty {
                Text("No video formats available.")
                    .foregroundColor(.secondary)
                    .font(.system(size: 12))
            } else {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(formats) { fmt in
                            FormatChip(
                                label: fmt.qualityLabel,
                                detail: fmt.fileSizeDisplay,
                                badge: fmt.hasAudio ? nil : nil, // badge removed; merge shown separately
                                isSelected: selectedVideoFormat?.id == fmt.id
                            ) { selectedVideoFormat = fmt }
                        }
                    }
                    .padding(.vertical, 2)
                }
                if let sel = selectedVideoFormat {
                    Text(videoSubtitle(sel))
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                }
            }
        }
        .onAppear {
            if selectedVideoFormat == nil { selectedVideoFormat = formats.first }
        }
    }

    // MARK: - Audio format chips

    private var audioFormatPicker: some View {
        let formats = item.videoInfo?.audioFormats ?? []
        return VStack(alignment: .leading, spacing: 8) {
            if formats.isEmpty {
                Text("Best available audio will be used automatically.")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
            } else {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(formats) { fmt in
                            FormatChip(
                                label: fmt.label,
                                detail: fmt.fileSizeDisplay,
                                badge: nil,
                                isSelected: selectedAudioFormat?.id == fmt.id
                            ) { selectedAudioFormat = fmt }
                        }
                    }
                    .padding(.vertical, 2)
                }
            }
        }
        .onAppear {
            if selectedAudioFormat == nil { selectedAudioFormat = formats.first }
        }
    }

    // MARK: - Save Options

    private var saveOptionsSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Text("Save As:")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                    .frame(width: 58, alignment: .trailing)
                TextField("filename", text: $customFilename)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 12))
            }

            HStack(spacing: 8) {
                Text("Save To:")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                    .frame(width: 58, alignment: .trailing)
                Text(saveDirectory.abbreviatingWithTilde)
                    .font(.system(size: 12))
                    .foregroundColor(.primary)
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Button("Browse…") { pickSaveDirectory() }
                    .buttonStyle(.bordered)
                    .font(.system(size: 11))
                    .controlSize(.small)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    // MARK: - Footer

    private var footer: some View {
        HStack {
            Button("Cancel") { dismiss() }
                .keyboardShortcut(.escape)

            Spacer()

            // Summary label
            Text(modeSummary)
                .font(.system(size: 11))
                .foregroundColor(.secondary)

            Button("Add to Queue") {
                let trimmed = customFilename.trimmingCharacters(in: .whitespaces)
                let finalName = trimmed.isEmpty ? nil : trimmed
                if let name = finalName { customFilename = name }

                // Check for ytdlp duplicate (same URL + format + output dir + filename)
                let fmtId = resolvedFormatId()
                let container = (includeVideo && includeAudio && !mergeStreams) ? "native" : "mp4"
                let fname = trimmed.isEmpty ? nil : trimmed
                if let conflict = store.findDuplicate(
                    url: item.url,
                    formatId: fmtId,
                    containerFormat: container,
                    outputDir: saveDirectory,
                    customFilename: fname,
                    excludingId: item.id
                ) {
                    duplicateConflict = conflict
                    return
                }

                if !FileManager.default.fileExists(atPath: saveDirectory.path) {
                    showCreateFolderAlert = true
                } else {
                    addToQueue()
                }
            }
            .buttonStyle(.borderedProminent)
            .keyboardShortcut(.return)
            .disabled(!includeVideo && !includeAudio)
        }
        .padding(14)
    }

    // MARK: - Actions

    private func addToQueue() {
        let trimmed = customFilename.trimmingCharacters(in: .whitespaces)
        let opts = DownloadOptions(
            outputDirectory: saveDirectory,
            customFilename: trimmed.isEmpty ? nil : trimmed
        )
        let container = (includeVideo && includeAudio && !mergeStreams) ? "native" : "mp4"
        store.confirmFormat(id: item.id, formatId: resolvedFormatId(), options: opts,
                            containerFormat: container)
        dismiss()
    }

    private func pickSaveDirectory() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Select"
        panel.message = "Choose download folder"
        if panel.runModal() == .OK, let url = panel.url {
            saveDirectory = url
        }
    }

    // MARK: - Format resolution

    private func resolvedFormatId() -> String {
        if includeVideo && includeAudio {
            let vid = selectedVideoFormat?.formatId ?? "bestvideo"
            let aud = selectedAudioFormat?.formatId ?? "bestaudio"
            // mergeStreams 为 true 时用 "+" 合并，为 false 时用 "," 分别下载
            return mergeStreams ? "\(vid)+\(aud)/best" : "\(vid),\(aud)"
        } else if includeVideo {
            return selectedVideoFormat?.formatId ?? "bestvideo/best"
        } else if includeAudio {
            return selectedAudioFormat?.formatId ?? "bestaudio/best"
        } else {
            return "bestvideo+bestaudio/best"
        }
    }

    // MARK: - Filename helpers

    private func updateDefaultFilename() {
        let title = item.videoInfo?.title ?? item.title ?? "download"
        let safe = Self.safeName(title)
        if includeVideo && includeAudio {
            let label = selectedVideoFormat?.qualityLabel ?? "best"
            if mergeStreams {
                // 合并时，我们确定想要一个 mp4
                customFilename = "\(safe) \(label).mp4"
            } else {
                // 分别下载时，使用 yt-dlp 的变量 %(ext)s 自动匹配各自的后缀名
                // 如果担心文件名完全冲突（极少数情况下音视频后缀相同），可以加上 %(format_id)s 以示区分
                customFilename = "\(safe) \(label)_%(ext)s.%(ext)s"
            }
        } else if includeVideo {
            let label = selectedVideoFormat?.qualityLabel ?? "video"
            customFilename = "\(safe) \(label).\(selectedVideoFormat?.container ?? "mp4")"
        } else if includeAudio {
            if let fmt = selectedAudioFormat {
                let kbps = fmt.bitrateKbps.map { "\(Int($0))kbps" } ?? "audio"
                customFilename = "\(safe) \(kbps).\(fmt.container)"
            } else {
                customFilename = "\(safe).m4a"
            }
        } else {
            customFilename = "\(safe).mp4"
        }
    }

    private static func safeName(_ title: String) -> String {
        title.components(separatedBy: CharacterSet(charactersIn: "/\\:*?\"<>|"))
             .joined(separator: "_")
             .trimmingCharacters(in: .whitespaces)
    }

    // MARK: - Other helpers

    private var modeSummary: String {
        if includeVideo && includeAudio {
            return mergeStreams ? "Video + Audio → MP4" : "Video + Audio (separate files)"
        } else if includeVideo {
            return "Video only (no audio)"
        } else if includeAudio {
            return "Audio only"
        } else {
            return "Nothing selected"
        }
    }

    private var hostLabel: String {
        URL(string: item.url)?.host ?? item.url
    }

    private func formatDuration(_ seconds: Int) -> String {
        let h = seconds / 3600
        let m = (seconds % 3600) / 60
        let s = seconds % 60
        if h > 0 { return String(format: "%d:%02d:%02d", h, m, s) }
        return String(format: "%d:%02d", m, s)
    }

    private func videoSubtitle(_ fmt: VideoFormat) -> String {
        var parts: [String] = [fmt.codec.uppercased(), fmt.container.uppercased()]
        if !fmt.hasAudio { parts.append("video only") }
        return parts.joined(separator: " · ")
    }
}

// MARK: - FormatChip

private struct FormatChip: View {
    let label: String
    let detail: String?
    let badge: String?
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 2) {
                HStack(spacing: 4) {
                    Text(label)
                        .font(.system(size: 12, weight: isSelected ? .bold : .regular))
                    if let badge = badge {
                        Text(badge)
                            .font(.system(size: 9, weight: .semibold))
                            .padding(.horizontal, 4)
                            .padding(.vertical, 1)
                            .background(Color.white.opacity(0.25))
                            .clipShape(Capsule())
                    }
                }
                if let d = detail {
                    Text(d)
                        .font(.system(size: 10))
                        .foregroundColor(isSelected ? .white.opacity(0.8) : .secondary)
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(isSelected ? Color.accentColor : Color(NSColor.controlBackgroundColor))
            .foregroundColor(isSelected ? .white : .primary)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(isSelected ? Color.accentColor : Color.secondary.opacity(0.3), lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
    }
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
