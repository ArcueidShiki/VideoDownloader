namespace AnyDownloader.Core;

/// <summary>
/// Video file formats
/// </summary>
public enum VideoFormat
{
    /// <summary>
    /// Auto-select format
    /// </summary>
    Auto = 0,

    /// <summary>
    /// MP4 container
    /// </summary>
    Mp4 = 1,

    /// <summary>
    /// MKV container
    /// </summary>
    Mkv = 2,

    /// <summary>
    /// WebM container
    /// </summary>
    WebM = 3,

    /// <summary>
    /// AVI container
    /// </summary>
    Avi = 4,

    /// <summary>
    /// MOV container
    /// </summary>
    Mov = 5,

    /// <summary>
    /// FLV container
    /// </summary>
    Flv = 6,

    /// <summary>
    /// MP3 audio only
    /// </summary>
    Mp3 = 7,

    /// <summary>
    /// M4A audio only
    /// </summary>
    M4a = 8,

    /// <summary>
    /// WAV audio only
    /// </summary>
    Wav = 9
}
