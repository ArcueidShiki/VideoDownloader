import Foundation

public struct TorrentEngine: DownloadEngineProtocol {
    public init() {}

    public func createTask(
        for item: DownloadItem,
        options: DownloadOptions,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) -> any DownloadTaskProtocol {
        TorrentTask(item: item, options: options, onUpdate: onUpdate)
    }

    public static func buildArguments(url: String, options: DownloadOptions) -> [String] {
        var args: [String] = [
            "--dir=\(options.outputDirectory.path)",
            "--summary-interval=1",
            // "notice" level prints the [#hash size(N%) DL:X ETA:Y] progress lines
            "--console-log-level=notice",
            "--download-result=hide",
            "--continue=true",
            "--allow-overwrite=true",
            // Peer / DHT configuration
            "--enable-dht=true",
            "--enable-dht6=false",
            "--enable-peer-exchange=true",
            "--bt-enable-lpd=true",
            "--dht-entry-point=router.bittorrent.com:6881",
            "--dht-entry-point=router.utorrent.com:6881",
            "--dht-entry-point=dht.transmissionbt.com:6881",
            "--bt-tracker=udp://tracker.opentrackr.org:1337/announce",
            "--bt-tracker=udp://open.tracker.cl:1337/announce",
            "--bt-tracker=udp://tracker.openbittorrent.com:6969/announce",
            "--bt-tracker=udp://9.rarbg.com:2810/announce",
            "--bt-tracker=udp://tracker.torrent.eu.org:451/announce",
            "--bt-tracker-connect-timeout=20",
            "--bt-tracker-timeout=60",
            "--bt-stop-timeout=0",
            "--max-connection-per-server=5",
            "--split=5",
            "--bt-max-peers=500",
            "--check-certificate=false",
        ]
        if let indices = options.selectedFileIndices, !indices.isEmpty {
            args.append("--select-file=\(indices.map(String.init).joined(separator: ","))")
        }
        if url.lowercased().hasPrefix("magnet:") {
            args.append(url)
        } else {
            args.append("--torrent-file=\(url)")
        }
        return args
    }
}
