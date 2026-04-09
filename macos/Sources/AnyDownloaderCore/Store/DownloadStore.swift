import Foundation
import Observation

@MainActor
@Observable
public final class DownloadStore {
    public private(set) var items: [DownloadItem] = []
    public let maxConcurrentTasks: Int
    private let persistence: PersistenceManager
    private let validator = InputValidator()
    private let classifier = URLClassifier()
    private var activeTasks: [UUID: any DownloadTaskProtocol] = [:]
    private var pausedTasks: [UUID: any DownloadTaskProtocol] = [:]
    private let httpEngine = HTTPDownloadEngine()
    private let ytdlpEngine = YtDlpEngine()
    private let torrentEngine = TorrentEngine()
    /// Cache: URL string → VideoInfo, so repeated URLs don't re-fetch from the network.
    private var videoInfoCache: [String: VideoInfo] = [:]

    public init(
        persistencePath: String = PersistenceManager.defaultPath,
        maxConcurrent: Int = 5
    ) throws {
        self.persistence = try PersistenceManager(path: persistencePath)
        self.maxConcurrentTasks = maxConcurrent

        // Load full history (completed + failed + in-progress)
        let loaded = (try? persistence.loadAll()) ?? []

        // Reset transient states that are invalid without a running task.
        // After a restart there are no active tasks, so:
        //   .active        → .paused   (was downloading; user can resume manually)
        //   .fetchingInfo  → .selectingFormat  (if videoInfo cached) else .pending
        //   .fetchingMetadata → .selectingFiles (if torrentFiles cached) else .pending
        self.items = loaded.map { item in
            switch item.state {
            case .active:
                return item.with(state: .paused)
            case .fetchingInfo:
                return item.videoInfo != nil
                    ? item.with(state: .selectingFormat)
                    : item.with(state: .pending)
            case .fetchingMetadata:
                return item.torrentFiles != nil
                    ? item.with(state: .selectingFiles)
                    : item.with(state: .pending)
            default:
                return item
            }
        }

        // Persist the corrected states so the next launch sees them too
        for item in self.items {
            try? persistence.save(item)
        }
    }

    // MARK: - Adding items

    /// Maximum number of items the queue accepts. Prevents memory/disk exhaustion.
    public static let queueSizeLimit = 500

    /// Validates the URL and starts async info-fetching.
    /// For yt-dlp URLs: fetches video info → state becomes `.selectingFormat`.
    /// For torrents: fetches file list → state becomes `.selectingFiles`.
    /// For HTTP: goes straight to `.queued`.
    public func add(url: String, options: DownloadOptions) async throws {
        guard items.count < Self.queueSizeLimit else {
            throw DownloadStoreError.queueFull
        }
        let trimmed = url.trimmingCharacters(in: .whitespaces)
        try validator.validate(trimmed)

        // For HTTP: simple URL-level dedup — same URL with no format concept.
        // ytdlp and torrent dedup happens at format/file-selection time (in the UI sheets)
        // because the discriminating criteria (formatId, outputDir) aren't known yet.
        if classifier.classify(trimmed) == .http {
            let alreadyExists = items.contains { item in
                item.url == trimmed
                    && item.state != .completed
                    && item.state != .failed
            }
            guard !alreadyExists else {
                throw DownloadStoreError.duplicateURL
            }
        }
        let type_ = classifier.classify(trimmed)
        var item = DownloadItem(url: trimmed, type: type_)
        items.append(item)
        try persistence.save(item)

        switch type_ {
        case .ytdlp:
            Task { await fetchVideoInfo(item: item, options: options) }
        case .torrent:
            Task { await fetchTorrentMetadata(item: item) }
        case .http:
            item = item.with(state: .queued)
            applyUpdate(id: item.id) { _ in item }
            try persistence.save(item)
        }
    }

    // MARK: - Format / file selection confirmation

    /// Called from the format-picker sheet.
    /// Sets the chosen format, queues the item, does NOT start.
    public func confirmFormat(id: UUID, formatId: String, options: DownloadOptions,
                              containerFormat: String? = nil) {
        applyUpdate(id: id) {
            $0.with(
                state: .queued,
                selectedFormatId: formatId,
                selectedContainerFormat: containerFormat,
                itemOutputDirectory: options.outputDirectory,
                customFilename: options.customFilename
            )
        }
        if let updated = items.first(where: { $0.id == id }) {
            try? persistence.save(updated)
        }
    }

    /// Called from the torrent file-picker sheet.
    public func startTorrentDownload(
        id: UUID,
        selectedFiles: [TorrentFileEntry],
        outputDirectory: URL
    ) async {
        guard let item = items.first(where: { $0.id == id }) else { return }
        let indices = selectedFiles.filter { $0.isSelected }.map { $0.index }
        let options = DownloadOptions(outputDirectory: outputDirectory, selectedFileIndices: indices)
        await startItem(item: item, options: options)
    }

    // MARK: - Queue start / stop

    /// Starts `.queued` items. Pass `ids` to restrict to a specific selection; `nil` = all.
    public func startAllQueued(outputDirectory: URL, ids: Set<UUID>? = nil) async {
        let queued = items.filter { item in
            item.state == .queued && (ids == nil || ids!.contains(item.id))
        }
        for item in queued {
            let opts = buildOptions(for: item, outputDirectory: outputDirectory)
            await startIfCapacity(item: item, options: opts)
        }
    }

    /// Pauses active downloads. Pass `ids` to restrict to a specific selection; `nil` = all.
    public func pauseAll(ids: Set<UUID>? = nil) async {
        let active = items.filter { item in
            item.state == .active && (ids == nil || ids!.contains(item.id))
        }
        for item in active {
            await pause(id: item.id)
        }
    }

    /// Resumes paused downloads. Pass `ids` to restrict to a specific selection; `nil` = all.
    public func resumeAll(outputDirectory: URL, ids: Set<UUID>? = nil) {
        let paused = items.filter { item in
            item.state == .paused && (ids == nil || ids!.contains(item.id))
        }
        for item in paused {
            resume(id: item.id, outputDirectory: item.itemOutputDirectory ?? outputDirectory)
        }
    }

    /// Starts queued + resumes paused. Pass `ids` to restrict to a selection; `nil` = all.
    public func startOrResumeAll(outputDirectory: URL, ids: Set<UUID>? = nil) async {
        await startAllQueued(outputDirectory: outputDirectory, ids: ids)
        resumeAll(outputDirectory: outputDirectory, ids: ids)
    }

    public var queuedCount: Int { items.filter { $0.state == .queued }.count }

    // MARK: - Existing controls

    public func pause(id: UUID) async {
        // If there's a running task, stop it and preserve the instance for resume().
        // We do NOT return early when there is no task — items can be stuck in .active
        // state after a restart with no corresponding running task. Always update the
        // persisted state so the UI unblocks.
        if let task = activeTasks[id] {
            await task.pause()
            activeTasks.removeValue(forKey: id)
            pausedTasks[id] = task
        }
        applyUpdate(id: id) { $0.with(state: .paused) }
        if let item = items.first(where: { $0.id == id }) {
            try? persistence.save(item)
        }
    }

    public func resume(id: UUID, outputDirectory: URL) {
        guard let item = items.first(where: { $0.id == id }) else { return }
        // Reuse the exact same task instance so HTTP completedSegmentSet and
        // yt-dlp/torrent process references are preserved across pause/resume.
        let task = pausedTasks.removeValue(forKey: id)
            ?? makeTask(for: item, options: buildOptions(for: item, outputDirectory: outputDirectory))
        activeTasks[id] = task
        Task { try? await task.resume() }
    }

    /// Restarts a completed or failed download **in-place** — same row, same UUID.
    /// If the item already has video info and a chosen format (yt-dlp), it skips
    /// straight to queued so the format picker doesn't reappear.
    public func restart(id: UUID, outputDirectory: URL) async {
        if let task = activeTasks[id] { await task.cancel() }
        if let task = pausedTasks[id]  { await task.cancel() }
        activeTasks.removeValue(forKey: id)
        pausedTasks.removeValue(forKey: id)

        guard let item = items.first(where: { $0.id == id }) else { return }
        let resetItem = item.resetForRestart()
        applyUpdate(id: id) { _ in resetItem }
        try? persistence.save(resetItem)

        let opts = buildOptions(for: resetItem, outputDirectory: outputDirectory)
        switch resetItem.type {
        case .ytdlp:
            if resetItem.selectedFormatId != nil && resetItem.videoInfo != nil {
                // Format already chosen — skip picker, re-queue immediately.
                let queued = resetItem.with(state: .queued)
                applyUpdate(id: id) { _ in queued }
                try? persistence.save(queued)
                await startIfCapacity(item: queued, options: opts)
            } else {
                Task { await fetchVideoInfo(item: resetItem, options: opts) }
            }
        case .torrent:
            if resetItem.torrentFiles != nil {
                // File list already known — go back to file selection.
                applyUpdate(id: id) { $0.with(state: .selectingFiles) }
            } else {
                Task { await fetchTorrentMetadata(item: resetItem) }
            }
        case .http:
            let queued = resetItem.with(state: .queued)
            applyUpdate(id: id) { _ in queued }
            try? persistence.save(queued)
            await startIfCapacity(item: queued, options: opts)
        }
    }

    public func cancel(id: UUID) async {
        if let task = activeTasks[id] { await task.cancel() }
        if let task = pausedTasks[id]  { await task.cancel() }
        activeTasks.removeValue(forKey: id)
        pausedTasks.removeValue(forKey: id)
        applyUpdate(id: id) { $0.with(state: .failed, errorMessage: "Cancelled") }
        if let item = items.first(where: { $0.id == id }) {
            try? persistence.save(item)
        }
    }

    public func setOutputDirectory(_ url: URL, for id: UUID) {
        applyUpdate(id: id) { $0.with(itemOutputDirectory: url) }
        if let item = items.first(where: { $0.id == id }) {
            try? persistence.save(item)
        }
    }

    public func remove(id: UUID) {
        pausedTasks.removeValue(forKey: id)
        activeTasks.removeValue(forKey: id)
        items.removeAll { $0.id == id }
        try? persistence.delete(id: id)
    }

    public func filtered(by state: DownloadState) -> [DownloadItem] {
        items.filter { $0.state == state }
    }

    /// Returns an existing in-progress item that matches all the provided criteria,
    /// or `nil` if no conflict exists.
    ///
    /// Pass `excludingId` to skip the item being reconfigured (e.g. the format-picker
    /// item whose format is being confirmed — we don't want it to match itself).
    ///
    /// Nil parameters are treated as "don't care" — only non-nil values are compared.
    public func findDuplicate(
        url: String,
        formatId: String? = nil,
        containerFormat: String? = nil,
        outputDir: URL? = nil,
        customFilename: String? = nil,
        excludingId: UUID? = nil
    ) -> DownloadItem? {
        items.first { item in
            guard item.state != .completed, item.state != .failed else { return false }
            guard item.url == url else { return false }
            if let excludeId = excludingId, item.id == excludeId { return false }
            if let fid = formatId,   item.selectedFormatId != fid         { return false }
            if let cf  = containerFormat, item.selectedContainerFormat != cf { return false }
            if let dir = outputDir,  item.itemOutputDirectory?.path != dir.path { return false }
            if let fn  = customFilename, item.customFilename != fn         { return false }
            return true
        }
    }

    public var activeCount: Int { items.filter { $0.state == .active }.count }

    // MARK: - Private helpers

    private func fetchVideoInfo(item: DownloadItem, options: DownloadOptions) async {
        // Return immediately from cache if already fetched for this URL
        if let cached = videoInfoCache[item.url] {
            applyUpdate(id: item.id) {
                $0.with(state: .selectingFormat, title: cached.title, videoInfo: cached)
            }
            return
        }
        applyUpdate(id: item.id) { $0.with(state: .fetchingInfo) }
        do {
            let ytdlpPath = try YtDlpTask.findYtDlp()
            let info = try await URLInfoFetcher.fetch(url: item.url, ytDlpPath: ytdlpPath)
            videoInfoCache[item.url] = info
            applyUpdate(id: item.id) {
                $0.with(state: .selectingFormat, title: info.title, videoInfo: info)
            }
        } catch {
            applyUpdate(id: item.id) { $0.with(state: .failed, errorMessage: error.localizedDescription) }
        }
    }

    private func fetchTorrentMetadata(item: DownloadItem) async {
        applyUpdate(id: item.id) { $0.with(state: .fetchingMetadata) }
        do {
            let aria2Path = try TorrentTask.findAria2c()
            let metadata: TorrentMetadata
            if item.url.lowercased().hasPrefix("magnet:") {
                metadata = try await MagnetMetadataFetcher.fetch(magnetURL: item.url, aria2Path: aria2Path)
            } else {
                metadata = try TorrentMetadata.parse(torrentPath: item.url)
            }
            applyUpdate(id: item.id) {
                $0.with(state: .selectingFiles, title: metadata.name, torrentFiles: metadata.files)
            }
        } catch {
            applyUpdate(id: item.id) { $0.with(state: .failed, errorMessage: error.localizedDescription) }
        }
    }

    private func startItem(item: DownloadItem, options: DownloadOptions) async {
        await startIfCapacity(item: item, options: options)
    }

    private func startIfCapacity(item: DownloadItem, options: DownloadOptions) async {
        guard activeCount < maxConcurrentTasks else { return }
        let task = makeTask(for: item, options: options)
        activeTasks[item.id] = task
        Task { try? await task.start() }
    }

    private func buildOptions(for item: DownloadItem, outputDirectory: URL) -> DownloadOptions {
        DownloadOptions(
            outputDirectory: item.itemOutputDirectory ?? outputDirectory,
            quality: item.selectedFormatId ?? "bestvideo+bestaudio/best",
            format: item.selectedContainerFormat ?? "mp4",
            customFilename: item.customFilename
        )
    }

    private func makeTask(for item: DownloadItem, options: DownloadOptions) -> any DownloadTaskProtocol {
        let onUpdate: @Sendable (DownloadItem) -> Void = { [weak self] updated in
            Task { @MainActor [weak self] in
                self?.applyUpdate(id: updated.id) { _ in updated }
                try? self?.persistence.save(updated)
            }
        }
        switch item.type {
        case .http:    return httpEngine.createTask(for: item, options: options, onUpdate: onUpdate)
        case .ytdlp:   return ytdlpEngine.createTask(for: item, options: options, onUpdate: onUpdate)
        case .torrent: return torrentEngine.createTask(for: item, options: options, onUpdate: onUpdate)
        }
    }

    private func applyUpdate(id: UUID, transform: (DownloadItem) -> DownloadItem) {
        guard let idx = items.firstIndex(where: { $0.id == id }) else { return }
        items[idx] = transform(items[idx])
    }

    // MARK: - Testing helpers (internal — accessible via @testable import)

    /// Directly appends an item with any state. For unit tests only.
    func _injectItem(_ item: DownloadItem) {
        items.append(item)
    }

    /// Registers a mock task for a given item ID. For unit tests only.
    func _injectTask(_ task: any DownloadTaskProtocol, for id: UUID) {
        activeTasks[id] = task
    }

    /// Registers a mock task in the paused-tasks slot. For unit tests only.
    func _injectPausedTask(_ task: any DownloadTaskProtocol, for id: UUID) {
        pausedTasks[id] = task
    }
}

// MARK: - Errors

public enum DownloadStoreError: Error, LocalizedError {
    case queueFull
    case duplicateURL

    public var errorDescription: String? {
        switch self {
        case .queueFull:
            return "Queue is full (500 items). Remove some downloads before adding more."
        case .duplicateURL:
            return "This URL is already in the download list."
        }
    }
}
