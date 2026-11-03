import Foundation

/// Builds download destinations that can never escape the chosen output folder.
///
/// A server-controlled URL path such as `/a/..%2F..%2F.zshrc` decodes to a last
/// component containing `/` or `..`; appending it verbatim would write outside
/// the download folder. Every name goes through `sanitizedFilename` and the final
/// path is re-checked after resolving symlinks.
public enum SafeOutputPath {
    public static let fallbackName = "download"
    static let maxFilenameBytes = 255

    public enum PathError: LocalizedError {
        case escapesOutputDirectory
        case noAvailableName

        public var errorDescription: String? {
            switch self {
            case .escapesOutputDirectory: return "The download location is outside the selected folder."
            case .noAvailableName: return "Could not find a free filename in the selected folder."
            }
        }
    }

    /// Returns a single safe path component derived from `raw`.
    public static func sanitizedFilename(_ raw: String) -> String {
        let decoded = raw.removingPercentEncoding ?? raw
        // Keep only the final segment of anything that looks like a path.
        let lastSegment = decoded.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
        let cleaned = String(lastSegment.unicodeScalars.filter { scalar in
            !CharacterSet.controlCharacters.contains(scalar) && scalar != ":"
        })
        // Leading dots would create hidden files or the special names "." / "..".
        let trimmed = cleaned
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .drop(while: { $0 == "." })
        let name = String(trimmed).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { return fallbackName }
        return truncated(name)
    }

    /// Returns a non-existing file URL for `url` inside `directory`.
    /// Existing files are never reused: "name.ext" becomes "name (1).ext", etc.
    public static func uniqueOutputURL(for url: URL, in directory: URL) throws -> URL {
        let root = directory.standardizedFileURL.resolvingSymlinksInPath()
        let name = sanitizedFilename(url.lastPathComponent)
        let base = (name as NSString).deletingPathExtension
        let ext = (name as NSString).pathExtension
        for index in 0..<10_000 {
            let candidateName = index == 0 ? name
                : truncated(ext.isEmpty ? "\(base) (\(index))" : "\(base) (\(index)).\(ext)")
            let candidate = root.appendingPathComponent(candidateName).standardizedFileURL
            guard candidate.deletingLastPathComponent().path == root.path else {
                throw PathError.escapesOutputDirectory
            }
            // fileExists follows symlinks, so also check for a dangling link.
            let isLink = (try? FileManager.default.destinationOfSymbolicLink(atPath: candidate.path)) != nil
            if !isLink && !FileManager.default.fileExists(atPath: candidate.path) {
                return candidate
            }
        }
        throw PathError.noAvailableName
    }

    private static func truncated(_ name: String) -> String {
        guard name.utf8.count > maxFilenameBytes else { return name }
        let ext = (name as NSString).pathExtension
        let suffix = ext.isEmpty ? "" : ".\(ext)"
        var base = (name as NSString).deletingPathExtension
        while base.utf8.count + suffix.utf8.count > maxFilenameBytes, !base.isEmpty {
            base.removeLast()
        }
        return base.isEmpty ? fallbackName : base + suffix
    }
}
