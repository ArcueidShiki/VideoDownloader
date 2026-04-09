import Foundation

// MARK: - VideoInfo

public struct VideoInfo: Sendable, Codable {
    public let id: String
    public let title: String
    public let thumbnailURL: String?
    public let duration: Int?          // seconds
    public let uploader: String?
    public let originalURL: String
    public let videoFormats: [VideoFormat]
    public let audioFormats: [AudioFormat]

    /// All video formats de-duped and sorted best-first (highest resolution → lowest).
    public var sortedVideoFormats: [VideoFormat] {
        videoFormats.sorted { a, b in
            let ha = a.height ?? 0
            let hb = b.height ?? 0
            if ha != hb { return ha > hb }
            return (a.fps ?? 0) > (b.fps ?? 0)
        }
    }

    // MARK: Parse from yt-dlp --dump-json output

    public static func parse(from json: [String: Any]) throws -> VideoInfo {
        let id      = json["id"] as? String ?? UUID().uuidString
        let title   = json["title"] as? String ?? "Unknown"
        let uploader  = json["uploader"] as? String ?? json["channel"] as? String
        let duration  = json["duration"] as? Int
        let originalURL = json["webpage_url"] as? String ?? json["original_url"] as? String ?? ""

        // Pick the best thumbnail
        let thumbnail: String?
        if let thumbs = json["thumbnails"] as? [[String: Any]] {
            thumbnail = thumbs.last?["url"] as? String
        } else {
            thumbnail = json["thumbnail"] as? String
        }

        // Parse formats
        let rawFormats = json["formats"] as? [[String: Any]] ?? []
        var videoFormats: [VideoFormat] = []
        var audioFormats: [AudioFormat] = []

        for raw in rawFormats {
            let formatId    = raw["format_id"] as? String ?? ""
            let vcodec      = raw["vcodec"] as? String ?? "none"
            let acodec      = raw["acodec"] as? String ?? "none"
            let height      = raw["height"] as? Int
            let width       = raw["width"] as? Int
            let fps         = raw["fps"] as? Double
            let filesize    = (raw["filesize"] as? Int64) ?? (raw["filesize_approx"] as? Int64)
            let ext         = raw["ext"] as? String ?? "mp4"
            let formatNote  = raw["format_note"] as? String ?? ""
            let tbr         = raw["tbr"] as? Double

            let hasVideo = vcodec != "none" && vcodec != ""
            let hasAudio = acodec != "none" && acodec != ""

            if hasVideo {
                // Only include useful resolutions (360p and above)
                guard let h = height, h >= 360 else { continue }
                videoFormats.append(VideoFormat(
                    id: formatId,
                    formatId: formatId,
                    height: h,
                    width: width,
                    fps: fps,
                    codec: vcodec,
                    container: ext,
                    filesize: filesize,
                    hasAudio: hasAudio,
                    formatNote: formatNote,
                    bitrateKbps: tbr
                ))
            } else if hasAudio {
                let abr = raw["abr"] as? Double
                audioFormats.append(AudioFormat(
                    id: formatId,
                    formatId: formatId,
                    bitrateKbps: abr ?? tbr,
                    codec: acodec,
                    container: ext,
                    filesize: filesize
                ))
            }
        }

        // De-duplicate video formats: keep best (largest filesize) per height+fps bucket
        let grouped = Dictionary(grouping: videoFormats) { "\($0.height ?? 0)_\(Int($0.fps ?? 30))" }
        let deduped = grouped.values.compactMap { group in
            group.sorted { ($0.filesize ?? 0) > ($1.filesize ?? 0) }.first
        }

        // Sort audio by bitrate descending, keep top 3
        let sortedAudio = audioFormats
            .sorted { ($0.bitrateKbps ?? 0) > ($1.bitrateKbps ?? 0) }
            .prefix(4)
            .map { $0 }

        return VideoInfo(
            id: id,
            title: title,
            thumbnailURL: thumbnail,
            duration: duration,
            uploader: uploader,
            originalURL: originalURL,
            videoFormats: deduped,
            audioFormats: sortedAudio
        )
    }
}

// MARK: - VideoFormat

public struct VideoFormat: Identifiable, Sendable, Codable {
    public let id: String
    public let formatId: String
    public let height: Int?
    public let width: Int?
    public let fps: Double?
    public let codec: String
    public let container: String
    public let filesize: Int64?
    public let hasAudio: Bool
    public let formatNote: String
    public let bitrateKbps: Double?

    /// Human-readable quality label: "4K", "1440p", "1080p HD", "720p", etc.
    public var qualityLabel: String {
        guard let h = height else { return formatNote.isEmpty ? "Unknown" : formatNote }
        let base: String
        switch h {
        case 4320...: base = "8K"
        case 2160...: base = "4K"
        case 1440...: base = "1440p"
        case 1080...: base = "1080p HD"
        case 720...:  base = "720p"
        case 480...:  base = "480p"
        case 360...:  base = "360p"
        default:      base = "\(h)p"
        }
        if let f = fps, f >= 50 { return "\(base) \(Int(f))fps" }
        return base
    }

    public var fileSizeDisplay: String? {
        guard let sz = filesize else { return nil }
        return ByteCountFormatter.string(fromByteCount: sz, countStyle: .binary)
    }
}

// MARK: - AudioFormat

public struct AudioFormat: Identifiable, Sendable, Codable {
    public let id: String
    public let formatId: String
    public let bitrateKbps: Double?
    public let codec: String
    public let container: String
    public let filesize: Int64?

    public var label: String {
        let kbps = bitrateKbps.map { "\(Int($0))kbps" } ?? "Audio"
        let codecLabel = codec.contains("mp4") || codec.contains("aac") ? "AAC" :
                         codec.contains("opus") ? "Opus" :
                         codec.contains("vorbis") ? "Vorbis" : codec.uppercased()
        return "\(kbps) \(codecLabel)"
    }

    public var fileSizeDisplay: String? {
        guard let sz = filesize else { return nil }
        return ByteCountFormatter.string(fromByteCount: sz, countStyle: .binary)
    }
}
