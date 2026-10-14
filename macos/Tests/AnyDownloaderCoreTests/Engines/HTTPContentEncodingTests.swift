import Foundation
import Testing
@testable import AnyDownloaderCore

/// Real HTTP responses are required: URLProtocol fixtures do not perform CFNetwork decoding.
@Suite("HTTP content encoding", .serialized)
struct HTTPContentEncodingTests {
    private final class Server {
        let root: URL
        let process = Process()
        var port = ""
        init() throws {
            root = FileManager.default.temporaryDirectory.appendingPathComponent("http-coding-\(UUID())")
            try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
            let script = root.appendingPathComponent("server.py")
            try Self.script.write(to: script, atomically: true, encoding: .utf8)
            process.executableURL = URL(fileURLWithPath: "/usr/bin/python3")
            process.arguments = [script.path, root.path]
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try process.run()
        }
        func ready() async throws {
            let file = root.appendingPathComponent("port")
            let deadline = Date().addingTimeInterval(5)
            while Date() < deadline {
                if let value = try? String(contentsOf: file, encoding: .utf8),
                   let number = Int(value), (1...65535).contains(number) {
                    port = value
                    return
                }
                try await Task.sleep(for: .milliseconds(10))
            }
            throw URLError(.timedOut)
        }
        func close() {
            if process.isRunning { process.terminate() }
            try? FileManager.default.removeItem(at: root)
        }
        static let script = #"""
        import gzip, zlib, http.server, pathlib, sys, json
        root = pathlib.Path(sys.argv[1])
        payload = b'complete HTTP payload ' * 128
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_HEAD(self): self.respond(True)
            def do_GET(self): self.respond(False)
            def respond(self, head):
                mode = self.path.strip('/')
                recovered = (root / 'recover').exists()
                raw = b'x' if mode == 'gzip-small' else payload
                encoded = not recovered and mode != 'identity-ranges' and mode != 'identity-truncated'
                if mode == 'head-identity-get-gzip' and head: encoded = False
                if mode == 'head-gzip-get-identity' and not head: encoded = False
                if mode == 'encoded-range' and head: encoded = False
                coding = 'deflate' if mode == 'deflate' else 'gzip'
                body = (zlib.compress(raw) if coding == 'deflate' else gzip.compress(raw)) if encoded else raw
                status = 200
                content_range = None
                if self.headers.get('Range'):
                    start, end = map(int, self.headers['Range'][6:].split('-'))
                    raw = raw[start:end+1]
                    body = gzip.compress(raw) if encoded else raw
                    status = 206
                    content_range = 'bytes %d-%d/%d' % (start, end, len(payload))
                length = len(body)
                if not head and not recovered:
                    if mode in ('gzip-truncated', 'identity-truncated'): body = body[:-12]
                    if mode == 'gzip-trailer-truncated':
                        body = body[:-8]
                        length = len(body)
                with (root / 'requests.jsonl').open('a') as log:
                    log.write(json.dumps({'method': self.command, 'accept_encoding': self.headers.get('Accept-Encoding'), 'range': self.headers.get('Range')}) + '\n')
                self.send_response(status)
                self.send_header('Content-Length', str(length))
                self.send_header('Content-Type', 'application/octet-stream')
                if encoded: self.send_header('Content-Encoding', coding)
                if mode in ('gzip-ranges', 'identity-ranges', 'encoded-range'):
                    self.send_header('Accept-Ranges', 'bytes')
                if content_range: self.send_header('Content-Range', content_range)
                self.end_headers()
                if not head:
                    try: self.wfile.write(body)
                    except (BrokenPipeError, ConnectionResetError): pass
            def log_message(self, *args): pass
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        (root / 'port.tmp').write_text(str(server.server_port))
        (root / 'port.tmp').replace(root / 'port')
        server.serve_forever()
        """#
    }

    private func download(_ server: Server, _ mode: String) -> (HTTPDownloadTask, SubprocessRegressionTests.Updates) {
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = []
        config.timeoutIntervalForRequest = 5
        config.timeoutIntervalForResource = 10
        let updates = SubprocessRegressionTests.Updates()
        let item = DownloadItem(url: "http://127.0.0.1:\(server.port)/\(mode)", type: .http)
        let task = HTTPDownloadTask(item: item, options: DownloadOptions(outputDirectory: server.root, threadCount: 4),
                                    sessionConfiguration: config, onUpdate: updates.add)
        return (task, updates)
    }

    @Test("complete coded bodies and identity ranges produce exact files", arguments: [
        "gzip", "gzip-small", "deflate", "head-identity-get-gzip", "head-gzip-get-identity", "gzip-ranges", "identity-ranges"
    ])
    func complete(mode: String) async throws {
        let server = try Server()
        defer { server.close() }
        try await server.ready()
        let (task, updates) = download(server, mode)
        try await task.start()
        #expect(updates.last?.state == .completed)
        let expected = Data((mode == "gzip-small" ? "x" : String(repeating: "complete HTTP payload ", count: 128)).utf8)
        #expect(try Data(contentsOf: server.root.appendingPathComponent(mode)) == expected)
        #expect(updates.last?.receivedBytes == Int64(expected.count))
        #expect(updates.last?.totalBytes == Int64(expected.count))
        try await task.resume()
        #expect(updates.last?.state == .completed)
        #expect(updates.last?.receivedBytes == Int64(expected.count))
        let lines = try String(contentsOf: server.root.appendingPathComponent("requests.jsonl"), encoding: .utf8).split(separator: "\n")
        let requests = try lines.map { try JSONSerialization.jsonObject(with: Data($0.utf8)) as! [String: Any] }
        #expect(requests.allSatisfy { $0["accept_encoding"] as? String == "identity" })
        let gets = requests.filter { $0["method"] as? String == "GET" }
        #expect(gets.count == (mode == "identity-ranges" ? 4 : 1))
        if mode != "identity-ranges" { #expect(gets.allSatisfy { $0["range"] is NSNull }) }
    }

    @Test("short transfers, changed encoded lengths and encoded ranges fail and can retry", arguments: [
        "gzip-truncated", "gzip-trailer-truncated", "identity-truncated", "encoded-range"
    ])
    func invalid(mode: String) async throws {
        let server = try Server()
        defer { server.close() }
        try await server.ready()
        let (task, updates) = download(server, mode)
        await #expect(throws: (any Error).self) { try await task.start() }
        #expect(updates.last?.state != .completed)
        try Data().write(to: server.root.appendingPathComponent("recover"))
        try await task.resume()
        #expect(updates.last?.state == .completed)
        #expect(try Data(contentsOf: server.root.appendingPathComponent(mode)) == Data(String(repeating: "complete HTTP payload ", count: 128).utf8))
    }
}
