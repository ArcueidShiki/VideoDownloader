import AVFoundation

@MainActor
final class SoundManager {
    static let shared = SoundManager()
    private var players: [String: AVAudioPlayer] = [:]

    private init() {
        preload("complete")
        preload("error")
    }

    private func preload(_ name: String) {
        // Sounds live in AnyDownloader/Resources/ (plain folder, not xcassets)
        // Bundle.main.url resolves them directly with no subdirectory
        guard let url = Bundle.main.url(forResource: name, withExtension: "wav") else {
            return
        }
        players[name] = try? AVAudioPlayer(contentsOf: url)
        players[name]?.prepareToPlay()
    }

    func play(_ name: String) {
        players[name]?.play()
    }
}
