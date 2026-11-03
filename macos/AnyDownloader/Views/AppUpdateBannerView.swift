import SwiftUI

// ─────────────────────────────────────────────────────────────────────────────
// AppUpdateBannerView
//
// A slim, dismissible banner that slides in under the toolbar when a newer
// version of AnyDownloader is available in the Mac App Store.
//
// Tapping "Update Now" opens the App Store product page directly.
// ─────────────────────────────────────────────────────────────────────────────

struct AppUpdateBannerView: View {
    let latestVersion: String
    let onUpdate: () -> Void
    let onDismiss: () -> Void

    @State private var showReleaseNotes = false
    @State private var visible = false

    private var releaseNotes: String? { AppUpdateChecker.shared.releaseNotes }

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "arrow.down.app.fill")
                .foregroundColor(.accentColor)
                .font(.system(size: 14))

            VStack(alignment: .leading, spacing: 1) {
                Text("Update Available — v\(latestVersion)")
                    .font(.system(size: 12, weight: .semibold))
                if let notes = releaseNotes, !notes.isEmpty {
                    Button("What's new") { showReleaseNotes.toggle() }
                        .buttonStyle(.plain)
                        .font(.system(size: 10))
                        .foregroundColor(.accentColor)
                }
            }

            Spacer()

            Button("Update Now") { onUpdate() }
                .buttonStyle(.borderedProminent)
                .controlSize(.small)

            Button {
                withAnimation(.easeOut(duration: 0.2)) { visible = false }
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { onDismiss() }
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .medium))
                    .foregroundColor(.secondary)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
        .background(Color.accentColor.opacity(0.08))
        .overlay(alignment: .bottom) { Divider() }
        .opacity(visible ? 1 : 0)
        .offset(y: visible ? 0 : -8)
        .onAppear {
            withAnimation(.easeOut(duration: 0.25)) { visible = true }
        }
        .sheet(isPresented: $showReleaseNotes) {
            if let notes = releaseNotes {
                ReleaseNotesView(version: latestVersion, notes: notes, onUpdate: onUpdate)
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ReleaseNotesView — shown when the user taps "What's new"
// ─────────────────────────────────────────────────────────────────────────────

private struct ReleaseNotesView: View {
    let version: String
    let notes: String
    let onUpdate: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Image(systemName: "arrow.down.app.fill")
                    .font(.title2)
                    .foregroundColor(.accentColor)
                VStack(alignment: .leading, spacing: 2) {
                    Text("AnyDownloader \(version)")
                        .font(.headline)
                    Text("Now available in the Mac App Store")
                        .font(.caption)
                        .foregroundColor(.secondary)
                }
                Spacer()
                Button("Close") { dismiss() }
                    .buttonStyle(.borderless)
            }

            Divider()

            ScrollView {
                Text(notes)
                    .font(.body)
                    .foregroundColor(.primary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled)
            }

            HStack(spacing: 10) {
                Button("Not Now") { dismiss() }
                    .buttonStyle(.bordered)
                Button("Update Now") {
                    dismiss()
                    onUpdate()
                }
                .buttonStyle(.borderedProminent)
            }
            .frame(maxWidth: .infinity, alignment: .trailing)
        }
        .padding(24)
        .frame(width: 400, height: 340)
    }
}
