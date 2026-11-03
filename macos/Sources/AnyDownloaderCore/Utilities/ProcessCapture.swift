import Foundation

/// Drain both pipes while the child is alive, including its final unterminated line.
/// Bounded retention prevents a noisy extractor from exhausting application memory.
enum ProcessCapture {
    struct Output: Sendable {
        let status: Int32
        let stdout: Data
        let stderr: Data
    }

    private final class Buffer: @unchecked Sendable {
        let lock = NSLock()
        var data = Data()
        func append(_ chunk: Data, limit: Int) {
            lock.withLock {
                data.append(chunk)
                if data.count > limit { data.removeFirst(data.count - limit) }
            }
        }
        var value: Data { lock.withLock { data } }
    }

    static func run(_ process: Process, stdout: Pipe, stderr: Pipe,
                    launch: (@Sendable () throws -> Void)? = nil,
                    onLine: @escaping @Sendable (String) -> Void = { _ in }) async throws -> Output {
        try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                do {
                    if let launch { try launch() } else { try process.run() }
                } catch { continuation.resume(throwing: error); return }
                let errors = Buffer()
                let group = DispatchGroup()
                group.enter()
                DispatchQueue.global(qos: .utility).async {
                    while let chunk = try? stderr.fileHandleForReading.read(upToCount: 16384), !chunk.isEmpty {
                        errors.append(chunk, limit: 65536)
                    }
                    group.leave()
                }
                let output = Buffer()
                var pending = Data()
                while let chunk = try? stdout.fileHandleForReading.read(upToCount: 16384), !chunk.isEmpty {
                    output.append(chunk, limit: 16 * 1024 * 1024)
                    pending.append(chunk)
                    while let end = pending.firstIndex(of: 10) {
                        onLine(String(decoding: pending[..<end], as: UTF8.self))
                        pending.removeSubrange(...end)
                    }
                    if pending.count > 1024 * 1024 { pending.removeAll() }
                }
                if !pending.isEmpty { onLine(String(decoding: pending, as: UTF8.self)) }
                process.waitUntilExit()
                group.wait()
                continuation.resume(returning: Output(status: process.terminationStatus,
                                                      stdout: output.value, stderr: errors.value))
            }
        }
    }
}
