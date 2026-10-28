# Local macOS verification — 2026-10-11

The original source checkout remained on clean main at
`7a1a0f438f774a87d2ac42b5a039c2f5437479d2`. Work was performed in an isolated
shared clone on the user's Mac, based on PR #1
`bb73d35a2a9a0ca96cefe6a2380977f6e69928cc`. No cloud execution substituted for Mac tests.
No project AGENTS.md or .agents/skills instructions were present.

Validation target: native Apple Silicon macOS, Swift 6 toolchain.

## Observed defects and repairs

- Homebrew FFmpeg 8.1 existed but crashed because `libx265.215.dylib` was missing.
  Its original keg was archived outside the checkout. `HOMEBREW_NO_INSTALL_CLEANUP=1`
  Homebrew reinstall restored working FFmpeg/ffprobe 9.0.2. Homebrew also updated
  dependent formulae; no `brew cleanup` was run. Startup now runs version probes,
  rather than trusting executable-file existence alone.
- Metadata stderr could fill its pipe while the parent waited for termination.
  Both streams now drain concurrently with bounded retained output.
- Final file detection relied on chunk-split human-readable messages and guessed
  filenames. The app now consumes yt-dlp's `after_move` JSON filename, drains the
  final unterminated line, and requires a non-empty regular output file.
- Launch exceptions and stale callbacks could leave or overwrite queue state.
  Exceptions now become failures, cancellation invalidates stale attempts, and
  pause/retry waits for the previous owned writer before starting again.
- The app plist lacked CFBundleExecutable/CFBundlePackageType. A copied/renamed
  Release app entered its event loop without a usable window. Both keys are now
  explicit and checked by the packaging script.
- Deno and FFmpeg/ffprobe are now included in setup. Read-only yt-dlp/Deno startup
  release checks displayed newer releases without forcing a restart or replacing
  Homebrew files. Windows staged-update behavior remains unchanged.

## Verified

- Baseline: 130 Swift tests and the Debug GUI build passed before repairs.
- Final offline suite: 137 executed tests passed; two opt-in live tests skipped
  in the ordinary run (Swift Testing reports 139 total tests).
- Seven new regressions cover split Unicode/final output, missing final file,
  failed executable, large stderr, real child pause/drain, retry recovery,
  resume launch failure and cancellation before launch.
- Four hygiene tests cover preview, archive/restore, preserving downloads,
  rejecting tracked contents, symlink escape and archives inside the checkout.
- Separate live yt-dlp test: metadata, pause/resume, repeated download and ffprobe
  all passed using the Blender Foundation's CC BY 3.0 Sintel trailer.
  Size: 4,372,373 bytes. Duration: 52.208333 seconds.
  SHA-256: `b670602fa00934ca27c4351bb0efe7ea7a07fae57284e44226025eeed7c51254`.
- Separate live DownloadStore test: real HTTP transfer completed and the persisted
  completed row survived reopening the store. The Release GUI displayed this
  actual record, completed status, output folder, optional tool-update notice,
  and zero active downloads.
- Debug and Release builds succeeded. The packaged app passed strict/deep
  ad-hoc signature validation and was launched from outside the build directory.
  The GUI validation copy used a separate bundle identifier, database and folder;
  lsof confirmed the task-owned SQLite database was open.

## Remaining limits

- GUI screenshot inspection and real engine/store execution are verified. Button
  clicking via Accessibility is not: osascript was denied with error -1719, and
  the computer-use plugin transport was unavailable. Full interactive button
  acceptance remains a review gate, not a claimed pass.
- The W3C mirror rejected yt-dlp with a Cloudflare 403; the real error was surfaced.
  The official Blender mirror succeeded. No protection bypass was attempted.
- No live YouTube account/cookies test, torrent swarm test, Intel-Mac execution,
  notarization, or self-contained redistributable dependency bundle is claimed.
- The app uses installed Homebrew dependencies. It is a locally signed Mac app,
  not a notarized public release. yt-dlp/Deno updates are advisory on macOS.
- Remote PR checks must be assessed at the pushed HEAD. The inherited Cloudflare
  failure is not resolved merely by successful local tests.

Evidence and packages are outside Git under the task's `evidence/`, `deliverables/`
and `dependency-archive/` directories. The explicit push workflow archives known
build outputs outside the checkout; CI checks the repository, never the user's Mac.
