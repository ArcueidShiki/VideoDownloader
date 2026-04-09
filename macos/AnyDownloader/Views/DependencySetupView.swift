import SwiftUI
import AnyDownloaderCore

struct DependencySetupView: View {
    @State private var status = DependencyStatus.check()
    @State private var installing: String? = nil
    @State private var installLog: String = ""
    @State private var installError: String? = nil
    let onComplete: () -> Void

    private var brewPath: String {
        ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"]
            .first { FileManager.default.isExecutableFile(atPath: $0) }
            ?? "/opt/homebrew/bin/brew"
    }
    private var brewInstalled: Bool { FileManager.default.isExecutableFile(atPath: brewPath) }

    var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack(spacing: 14) {
                Image(nsImage: NSImage(named: "AppIcon") ?? NSImage())
                    .resizable()
                    .frame(width: 56, height: 56)
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                VStack(alignment: .leading, spacing: 4) {
                    Text("AnyDownloader Setup")
                        .font(.title2.bold())
                    Text("Two command-line tools are required before downloading.")
                        .font(.subheadline)
                        .foregroundColor(.secondary)
                }
            }
            .padding(24)

            Divider()

            VStack(spacing: 12) {
                DepRow(name: "aria2c",
                       description: "Handles torrents and magnet links",
                       installed: !status.aria2cMissing,
                       installing: installing == "aria2c",
                       brewInstalled: brewInstalled) {
                    Task { await install("aria2") }
                }
                DepRow(name: "yt-dlp",
                       description: "Downloads from YouTube, Vimeo, etc.",
                       installed: !status.ytDlpMissing,
                       installing: installing == "yt-dlp",
                       brewInstalled: brewInstalled) {
                    Task { await install("yt-dlp") }
                }

                if !brewInstalled {
                    Link("Install Homebrew first →",
                         destination: URL(string: "https://brew.sh")!)
                        .font(.system(size: 12))
                        .padding(.top, 4)
                }

                if !installLog.isEmpty {
                    ScrollView {
                        Text(installLog)
                            .font(.system(size: 10, design: .monospaced))
                            .foregroundColor(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(8)
                    }
                    .frame(height: 80)
                    .background(Color(NSColor.textBackgroundColor))
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                }

                if let err = installError {
                    Text(err)
                        .font(.system(size: 11))
                        .foregroundColor(.red)
                        .multilineTextAlignment(.leading)
                }
            }
            .padding(24)

            Divider()

            HStack {
                Button("Skip for now") { onComplete() }
                    .buttonStyle(.plain)
                    .foregroundColor(.secondary)
                Spacer()
                Button("Continue") { onComplete() }
                    .buttonStyle(.borderedProminent)
                    .disabled(!status.allPresent)
                    .keyboardShortcut(.return)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 16)
        }
        .frame(width: 460)
    }

    private func install(_ packageName: String) async {
        let toolName = packageName == "aria2" ? "aria2c" : packageName
        installing = toolName
        installLog = ""
        installError = nil

        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: brewPath)
        proc.arguments = ["install", packageName]

        let pipe = Pipe()
        proc.standardOutput = pipe
        proc.standardError = pipe

        do {
            try proc.run()
            let handle = pipe.fileHandleForReading
            await withCheckedContinuation { continuation in
                handle.readabilityHandler = { fh in
                    let data = fh.availableData
                    guard !data.isEmpty else { return }
                    let line = String(data: data, encoding: .utf8) ?? ""
                    Task { @MainActor in installLog += line }
                }
                proc.terminationHandler = { _ in
                    handle.readabilityHandler = nil
                    continuation.resume()
                }
            }
            if proc.terminationStatus == 0 {
                status = DependencyStatus.check()
            } else {
                installError = "brew install \(packageName) failed (exit \(proc.terminationStatus)). Try running it manually in Terminal."
            }
        } catch {
            installError = error.localizedDescription
        }
        installing = nil
    }
}

// MARK: - DepRow

private struct DepRow: View {
    let name: String
    let description: String
    let installed: Bool
    let installing: Bool
    let brewInstalled: Bool
    let onInstall: () -> Void

    var body: some View {
        HStack(spacing: 14) {
            ZStack {
                Circle()
                    .fill(installed ? Color.green.opacity(0.15) : Color.orange.opacity(0.12))
                    .frame(width: 36, height: 36)
                if installing {
                    ProgressView()
                        .scaleEffect(0.65)
                } else {
                    Image(systemName: installed ? "checkmark.circle.fill" : "exclamationmark.circle.fill")
                        .foregroundColor(installed ? .green : .orange)
                        .font(.system(size: 18))
                }
            }

            VStack(alignment: .leading, spacing: 2) {
                Text(name).font(.system(size: 13, weight: .semibold))
                Text(description).font(.system(size: 11)).foregroundColor(.secondary)
            }

            Spacer()

            if installed {
                Text("Installed")
                    .font(.system(size: 11))
                    .foregroundColor(.green)
            } else {
                BrewInstallButton(brewing: installing, brewAvailable: brewInstalled, action: onInstall)
            }
        }
        .padding(12)
        .background(Color(NSColor.controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

private struct BrewInstallButton: View {
    let brewing: Bool
    let brewAvailable: Bool
    let action: () -> Void

    var body: some View {
        SwiftUI.Button(brewing ? "Installing…" : "Install via Homebrew") {
            action()
        }
        .buttonStyle(.bordered)
        .disabled(brewing || !brewAvailable)
        .font(.system(size: 12))
    }
}
