import Testing
@testable import AnyDownloaderCore

@Suite("yt-dlp failure message")
struct YtDlpFailureMessageTests {
    // Exact stderr captured from a failed Bilibili download (2026-11-03).
    private let staleWarningThenError = """
    WARNING: Your yt-dlp version (2026.03.17) is older than 90 days!
             It is strongly recommended to always use the latest version.
             You installed yt-dlp with pip or using the wheel from PyPi; Use that to update.
             To suppress this warning, add --no-update to your command/config.
    ERROR: \r[download] Got error: HTTP Error 567: Unknown Status. Giving up after 10 retries
    """

    @Test("ERROR line wins over earlier WARNING block")
    func errorBeatsWarning() {
        let message = YtDlpTask.failureMessage(stderr: staleWarningThenError, status: 1)
        #expect(message == "ERROR: [download] Got error: HTTP Error 567: Unknown Status. Giving up after 10 retries")
        #expect(!message.contains("WARNING"))
    }

    @Test("multiple ERROR lines are all kept")
    func multipleErrors() {
        let stderr = "ERROR: first\nWARNING: noise\nERROR: second"
        #expect(YtDlpTask.failureMessage(stderr: stderr, status: 1) == "ERROR: first\nERROR: second")
    }

    @Test("without ERROR lines, warnings and their continuations are dropped")
    func dropsWarningsWithoutError() {
        let stderr = "WARNING: old version\n         indented continuation\nsomething broke"
        #expect(YtDlpTask.failureMessage(stderr: stderr, status: 2) == "something broke")
    }

    @Test("only warnings falls back to the exit code")
    func onlyWarnings() {
        let stderr = "WARNING: old version\n         indented continuation"
        #expect(YtDlpTask.failureMessage(stderr: stderr, status: 3) == "yt-dlp exited with code 3")
    }

    @Test("empty stderr falls back to the exit code")
    func emptyStderr() {
        #expect(YtDlpTask.failureMessage(stderr: "  \n", status: 1) == "yt-dlp exited with code 1")
    }
}
