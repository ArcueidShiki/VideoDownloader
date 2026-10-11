# Safe local cleanup and pushing

Generated outputs belong outside Git. CI runs `python3 scripts/repo_hygiene.py check`
on the index and builds/tests the Mac app. Remote CI can only inspect its own checkout:
it cannot clean a developer's computer.

Run `python3 scripts/repo_hygiene.py clean` to preview the exact allowlist. With
`clean --apply --archive-dir /absolute/path/outside/checkout`, outputs are renamed
into a timestamped archive on the same volume. `manifest.json` records the source,
HEAD, and moved paths. Restore by moving those paths back into the original checkout
when the destination does not exist. Keep the archive until it is no longer needed.

Only these directories are eligible: `macos/.build`, `macos/.build-app`,
`macos/build`, `macos/DerivedData`, `webapp/dist`, `webapp/build`, `webapp/coverage`,
`windows/publish`, `windows/dist`. Tracked contents or symlink paths abort cleanup.
Paths outside this allowlist (including downloads, SQLite history, dependency tools,
release packages and node_modules) are not selected. Each eligible directory moves
as a whole: any unexpected user files inside it move too and remain recoverable.
Review the preview and stop builds before archiving. If interrupted between rename
and manifest update, inspect the archive batch for the last moved directory.

After reviewing/staging source and committing, use:

```
scripts/push.sh /absolute/path/to/build-archives origin fix/ytdlp-integration
```

This checks the index, archives allowed outputs, then performs a normal push. A push
failure does not delete the archive. There are no automatic destructive hooks and
no broad `git clean` invocation. Plain `git push` does not clean local files; the CI
check still catches generated files committed to the index.

# macOS local packaging and validation

`swift test --package-path macos` runs offline regressions. `scripts/package-macos.sh`
builds and verifies a local ad-hoc signed Release app and zip. Set `OUTPUT_DIR` to a
folder outside the repository to retain deliverables independently of checkout cleanup.
The package is for local use, not notarized distribution. It requires macOS 14+ and
working `yt-dlp`, `deno`, `ffmpeg`, `ffprobe`, and `aria2c`, resolved from bundled
resources or Homebrew. Install with `brew install yt-dlp deno ffmpeg aria2`.
The package does not pretend Homebrew's linked binaries are portable standalone tools.

Startup executes each tool's version probe, detecting missing dynamic libraries.
Read-only yt-dlp/Deno release checks are optional and tolerate offline errors. Homebrew
updates are user initiated; existing downloads should finish first. New subprocesses
pick up updated tools without restarting the app. Windows' staged updater is unchanged.

Opt in to the licensed live test:

```
ANYDOWNLOADER_LIVE_TESTS=1 ANYDOWNLOADER_LIVE_OUTPUT=/absolute/test-folder \
  swift test --package-path macos --filter LiveMediaTests
```

It downloads the Sintel trailer (© Blender Foundation, CC BY 3.0), exercises pause,
resume and repeated download, and verifies the resulting media using ffprobe.
License: https://durian.blender.org/sharing/
Media: https://download.blender.org/durian/trailer/sintel_trailer-480p.mp4

For GUI validation, use a separate bundle identifier and set
`ANYDOWNLOADER_STATE_DIRECTORY` to a task-owned directory before launching. Pass
`-outputDirectory /absolute/test-folder` for isolated download output. A distinct
bundle identifier also isolates preferences and the Keychain service. GUI click
verification requires macOS Accessibility permission; headless tests do not replace it.
