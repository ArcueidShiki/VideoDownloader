using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using AnyDownloader.Windows.Models;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// SQLite-backed persistence for download history.
/// Mirrors the macOS PersistenceManager / DownloadStore save/load logic.
///
/// Database is stored at %LOCALAPPDATA%\AnyDownloader\downloads.db.
/// Schema uses two logical "migrations":
///   v1 — core fields
///   v2 — rich metadata (video_info_json, torrent_files_json, format selection)
///
/// All writes are INSERT OR REPLACE (upsert) — every state change triggers a save.
/// On load, transient states are normalised:
///   Active          → Paused   (process was killed when app closed)
///   FetchingInfo    → Pending  (metadata must be re-fetched)
///   SelectingFormat → Pending
///   SelectingFiles  → Pending
/// </summary>
public sealed class DownloadPersistenceService
{
    // ─────────────────────────────────────────────────────
    // Paths
    // ─────────────────────────────────────────────────────

    public static readonly string DbPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AnyDownloader", "downloads.db");

    // ─────────────────────────────────────────────────────
    // JSON options
    // ─────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    // ─────────────────────────────────────────────────────
    // Constructor — creates schema if missing
    // ─────────────────────────────────────────────────────

    public DownloadPersistenceService()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        InitSchema();
    }

    private void InitSchema()
    {
        using var conn = OpenConn();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS download_item (
                id                       TEXT PRIMARY KEY,
                url                      TEXT NOT NULL,
                type                     TEXT NOT NULL,
                source_platform          TEXT NOT NULL DEFAULT '',
                state                    TEXT NOT NULL,
                progress                 REAL NOT NULL DEFAULT 0,
                file_path                TEXT,
                file_paths_json          TEXT,
                error_message            TEXT,
                title                    TEXT,
                created_at               TEXT NOT NULL,
                torrent_files_json       TEXT,
                video_info_json          TEXT,
                selected_video_format_id TEXT,
                selected_audio_format_id TEXT,
                merge_to_mp4             INTEGER NOT NULL DEFAULT 1,
                item_output_directory    TEXT
            )";
        cmd.ExecuteNonQuery();

        // Migration: add file_paths_json to existing databases that predate this column
        try
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE download_item ADD COLUMN file_paths_json TEXT";
            alter.ExecuteNonQuery();
        }
        catch { /* column already exists — ignore */ }
    }

    // ─────────────────────────────────────────────────────
    // Save (upsert)
    // ─────────────────────────────────────────────────────

    public Task SaveAsync(DownloadItemModel item)
    {
        // Capture values on caller thread before going async
        var torrentJson   = item.TorrentFiles.Count > 0
            ? JsonSerializer.Serialize(item.TorrentFiles, JsonOpts)
            : null;

        var videoJson     = item.VideoInfo is not null
            ? JsonSerializer.Serialize(item.VideoInfo, JsonOpts)
            : null;

        var filePathsJson = item.FilePaths.Count > 0
            ? JsonSerializer.Serialize(item.FilePaths, JsonOpts)
            : null;

        var snapshot = new
        {
            id             = item.Id.ToString(),
            url            = item.Url,
            type           = item.Type.ToString(),
            platform       = item.SourcePlatform,
            state          = item.State.ToString(),
            progress       = item.Progress,
            filePath       = item.FilePath,
            filePathsJson,
            errorMessage   = item.ErrorMessage,
            title          = item.Title,
            createdAt      = item.CreatedAt.ToString("o"),
            torrentJson,
            videoJson,
            vidFmt         = item.SelectedVideoFormatId,
            audFmt         = item.SelectedAudioFormatId,
            mergeMp4       = item.MergeToMp4 ? 1 : 0,
            outDir         = item.ItemOutputDirectory,
        };

        return Task.Run(() =>
        {
            try
            {
                using var conn = OpenConn();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO download_item
                        (id, url, type, source_platform, state, progress,
                         file_path, file_paths_json, error_message, title, created_at,
                         torrent_files_json, video_info_json,
                         selected_video_format_id, selected_audio_format_id,
                         merge_to_mp4, item_output_directory)
                    VALUES
                        ($id, $url, $type, $platform, $state, $progress,
                         $file_path, $file_paths_json, $error_message, $title, $created_at,
                         $torrent_json, $video_json,
                         $vid_fmt, $aud_fmt, $merge_mp4, $item_outdir)";

                cmd.Parameters.AddWithValue("$id",             snapshot.id);
                cmd.Parameters.AddWithValue("$url",            snapshot.url);
                cmd.Parameters.AddWithValue("$type",           snapshot.type);
                cmd.Parameters.AddWithValue("$platform",       snapshot.platform);
                cmd.Parameters.AddWithValue("$state",          snapshot.state);
                cmd.Parameters.AddWithValue("$progress",       snapshot.progress);
                cmd.Parameters.AddWithValue("$file_path",      (object?)snapshot.filePath       ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$file_paths_json",(object?)snapshot.filePathsJson  ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$error_message",  (object?)snapshot.errorMessage   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$title",          (object?)snapshot.title          ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$created_at",     snapshot.createdAt);
                cmd.Parameters.AddWithValue("$torrent_json",   (object?)snapshot.torrentJson    ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$video_json",     (object?)snapshot.videoJson      ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$vid_fmt",        (object?)snapshot.vidFmt         ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$aud_fmt",        (object?)snapshot.audFmt         ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$merge_mp4",      snapshot.mergeMp4);
                cmd.Parameters.AddWithValue("$item_outdir",    (object?)snapshot.outDir         ?? DBNull.Value);

                cmd.ExecuteNonQuery();
            }
            catch { /* best-effort; never crash the UI for a DB write */ }
        });
    }

    // ─────────────────────────────────────────────────────
    // Delete
    // ─────────────────────────────────────────────────────

    public Task DeleteAsync(Guid id)
    {
        var idStr = id.ToString();
        return Task.Run(() =>
        {
            try
            {
                using var conn = OpenConn();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM download_item WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", idStr);
                cmd.ExecuteNonQuery();
            }
            catch { }
        });
    }

    // ─────────────────────────────────────────────────────
    // Load all (ordered by creation time)
    // ─────────────────────────────────────────────────────

    public Task<List<PersistedDownloadData>> LoadAllAsync()
    {
        return Task.Run(() =>
        {
            var result = new List<PersistedDownloadData>();
            try
            {
                using var conn = OpenConn();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM download_item ORDER BY created_at ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    try { result.Add(ReadRow(reader)); }
                    catch { /* skip corrupt rows */ }
                }
            }
            catch { }
            return result;
        });
    }

    // ─────────────────────────────────────────────────────
    // Row → PersistedDownloadData
    // ─────────────────────────────────────────────────────

    private static PersistedDownloadData ReadRow(SqliteDataReader r)
    {
        static string? NullableStr(SqliteDataReader r, string col)
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? null : r.GetString(ord);
        }

        // Deserialize rich metadata from JSON
        var torrentJson   = NullableStr(r, "torrent_files_json");
        var videoJson     = NullableStr(r, "video_info_json");
        var filePathsJson = NullableStr(r, "file_paths_json");

        List<TorrentFileEntry>? torrentFiles = null;
        if (torrentJson is not null)
        {
            try { torrentFiles = JsonSerializer.Deserialize<List<TorrentFileEntry>>(torrentJson, JsonOpts); }
            catch { }
        }

        VideoInfoModel? videoInfo = null;
        if (videoJson is not null)
        {
            try { videoInfo = JsonSerializer.Deserialize<VideoInfoModel>(videoJson, JsonOpts); }
            catch { }
        }

        List<string>? filePaths = null;
        if (filePathsJson is not null)
        {
            try { filePaths = JsonSerializer.Deserialize<List<string>>(filePathsJson, JsonOpts); }
            catch { }
        }

        // Parse enums with fallback
        Enum.TryParse<DownloadType>(r.GetString(r.GetOrdinal("type")), out var type);
        Enum.TryParse<DownloadState>(r.GetString(r.GetOrdinal("state")), out var state);

        // ── State normalisation ──────────────────────────
        // Transient "in-progress" states can't survive an app restart because
        // all processes were killed.  Mirror macOS DownloadStore behaviour:
        state = state switch
        {
            DownloadState.Active          => DownloadState.Paused,   // was downloading
            DownloadState.FetchingInfo    => DownloadState.Pending,  // re-fetch on resume
            DownloadState.SelectingFormat => DownloadState.Pending,
            DownloadState.SelectingFiles  => DownloadState.Pending,
            _                             => state,
        };

        DateTimeOffset.TryParse(r.GetString(r.GetOrdinal("created_at")), out var createdAt);

        return new PersistedDownloadData(
            Id:                     Guid.Parse(r.GetString(r.GetOrdinal("id"))),
            Url:                    r.GetString(r.GetOrdinal("url")),
            Type:                   type,
            SourcePlatform:         r.GetString(r.GetOrdinal("source_platform")),
            State:                  state,
            Progress:               r.GetDouble(r.GetOrdinal("progress")),
            FilePath:               NullableStr(r, "file_path"),
            FilePaths:              filePaths,
            ErrorMessage:           NullableStr(r, "error_message"),
            Title:                  NullableStr(r, "title"),
            CreatedAt:              createdAt == default ? DateTimeOffset.Now : createdAt,
            TorrentFiles:           torrentFiles,
            VideoInfo:              videoInfo,
            SelectedVideoFormatId:  NullableStr(r, "selected_video_format_id"),
            SelectedAudioFormatId:  NullableStr(r, "selected_audio_format_id"),
            MergeToMp4:             r.GetInt32(r.GetOrdinal("merge_to_mp4")) != 0,
            ItemOutputDirectory:    NullableStr(r, "item_output_directory"));
    }

    // ─────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────

    private static SqliteConnection OpenConn() => new($"Data Source={DbPath}");
}

// ─────────────────────────────────────────────────────
// DTO returned by LoadAllAsync
// ─────────────────────────────────────────────────────

public sealed record PersistedDownloadData(
    Guid                    Id,
    string                  Url,
    DownloadType            Type,
    string                  SourcePlatform,
    DownloadState           State,
    double                  Progress,
    string?                 FilePath,
    List<string>?           FilePaths,
    string?                 ErrorMessage,
    string?                 Title,
    DateTimeOffset          CreatedAt,
    List<TorrentFileEntry>? TorrentFiles,
    VideoInfoModel?         VideoInfo,
    string?                 SelectedVideoFormatId,
    string?                 SelectedAudioFormatId,
    bool                    MergeToMp4,
    string?                 ItemOutputDirectory);
