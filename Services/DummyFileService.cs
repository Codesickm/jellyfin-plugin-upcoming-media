using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JellyfinUpcomingMedia.Models;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.Services;

/// <summary>
/// Creates, manages, and swaps dummy MKV files in the Jellyfin library.
/// A small valid Matroska (.mkv) container is created so Jellyfin recognises
/// the file and fetches full metadata automatically.
/// </summary>
public class DummyFileService
{
    private readonly ILogger<DummyFileService> _logger;

    /// <summary>
    /// Minimal valid MKV (Matroska) file bytes.
    /// Enough for Jellyfin/ffprobe to detect it as a video file and trigger metadata fetch.
    /// </summary>
    private static readonly byte[] MinimalMkv = GenerateMinimalMkv();

    /// <summary>
    /// Initializes a new instance of the <see cref="DummyFileService"/> class.
    /// </summary>
    public DummyFileService(ILogger<DummyFileService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates just the library folder for the given item without a dummy file.
    /// Returns the folder path, or null if creation failed.
    /// </summary>
    public string? CreateLibraryFolder(UpcomingItem item, string libraryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                _logger.LogWarning("Library path is not configured. Cannot create folder.");
                return null;
            }

            if (!Directory.Exists(libraryPath))
            {
                _logger.LogWarning("Library path does not exist: {Path}", libraryPath);
                return null;
            }

            var folderName = GetFolderName(item);
            var folderPath = Path.Combine(libraryPath, folderName);
            Directory.CreateDirectory(folderPath);

            _logger.LogInformation("Created library folder: {Path}", folderPath);
            return folderPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create library folder for '{Title}'", item.Title);
            return null;
        }
    }

    /// <summary>
    /// Creates a dummy MKV file in the configured library path for the given item.
    /// Uses Jellyfin naming convention: "Title (Year)/Title (Year).mkv".
    /// Returns the full path to the dummy file, or null if creation failed.
    /// </summary>
    public string? CreateDummyFile(UpcomingItem item, string libraryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                _logger.LogWarning("Library path is not configured. Cannot create dummy file.");
                return null;
            }

            if (!Directory.Exists(libraryPath))
            {
                _logger.LogWarning("Library path does not exist: {Path}", libraryPath);
                return null;
            }

            var folderName = GetFolderName(item);
            var folderPath = Path.Combine(libraryPath, folderName);
            Directory.CreateDirectory(folderPath);

            var fileName = folderName + ".mkv";
            var filePath = Path.Combine(folderPath, fileName);

            // Don't overwrite if already exists
            if (File.Exists(filePath))
            {
                _logger.LogInformation("Dummy file already exists: {Path}", filePath);
                return filePath;
            }

            File.WriteAllBytes(filePath, MinimalMkv);
            _logger.LogInformation("Created dummy MKV file: {Path} ({Bytes} bytes)", filePath, MinimalMkv.Length);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create dummy file for '{Title}'", item.Title);
            return null;
        }
    }

    /// <summary>
    /// Deletes the dummy file and its folder (if the folder is empty after deletion).
    /// </summary>
    public bool DeleteDummyFile(string? dummyFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dummyFilePath) || !File.Exists(dummyFilePath))
            {
                return false;
            }

            var folder = Path.GetDirectoryName(dummyFilePath);
            File.Delete(dummyFilePath);
            _logger.LogInformation("Deleted dummy file: {Path}", dummyFilePath);

            // Clean up empty folder
            if (!string.IsNullOrEmpty(folder)
                && Directory.Exists(folder)
                && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
                _logger.LogInformation("Removed empty folder: {Path}", folder);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete dummy file: {Path}", dummyFilePath);
            return false;
        }
    }

    /// <summary>
    /// Activates the real file by removing the .real extension.
    /// If a dummy file exists, it is deleted first.
    /// Works with or without a dummy — just needs the .real file in the library folder.
    /// </summary>
    public bool SwapFiles(UpcomingItem item)
    {
        try
        {
            var realPath = item.RealFilePath;

            if (string.IsNullOrWhiteSpace(realPath) || !File.Exists(realPath))
            {
                _logger.LogWarning("Real file not found at '{Path}' for item '{Title}'", realPath, item.Title);
                return false;
            }

            // Step 1: Delete the dummy file if it exists
            if (!string.IsNullOrWhiteSpace(item.DummyFilePath) && File.Exists(item.DummyFilePath))
            {
                File.Delete(item.DummyFilePath);
                _logger.LogInformation("Deleted dummy: {Path}", item.DummyFilePath);
            }

            // Step 2: Remove the .real extension → e.g. "Movie.mkv.real" → "Movie.mkv"
            // The target name is the path without the trailing .real
            var targetPath = realPath.EndsWith(".real", StringComparison.OrdinalIgnoreCase)
                ? realPath[..^5]  // strip ".real"
                : realPath;       // safety fallback

            if (targetPath != realPath)
            {
                // If target already exists (e.g. dummy had the same name), remove it
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    _logger.LogInformation("Removed existing file at target path: {Path}", targetPath);
                }

                File.Move(realPath, targetPath);
                _logger.LogInformation("Renamed real file: {From} → {To}", realPath, targetPath);
            }

            // Update the item to reflect the new state
            item.DummyFilePath = null;
            item.DummyCreated = false;
            item.RealFilePath = targetPath;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to swap files for '{Title}'", item.Title);
            return false;
        }
    }

    /// <summary>
    /// Looks for a ".real" file in the item's library folder (or the dummy file's folder).
    /// Returns the path to the first .real file found, or null.
    /// </summary>
    public string? FindRealFile(UpcomingItem item)
    {
        try
        {
            // Prefer LibraryFolderPath, fall back to dummy file's parent folder
            var folder = item.LibraryFolderPath;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                folder = string.IsNullOrWhiteSpace(item.DummyFilePath)
                    ? null
                    : Path.GetDirectoryName(item.DummyFilePath);
            }

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return null;
            }

            // Look for any file with a .real extension in the dummy's folder
            var realFile = Directory
                .EnumerateFiles(folder, "*.real", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (realFile != null)
            {
                _logger.LogInformation("Found .real file for '{Title}': {Path}", item.Title, realFile);
            }

            return realFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan for .real file for '{Title}'", item.Title);
            return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string SanitiseFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";

        // Remove characters that are invalid in file names
        var invalid = Path.GetInvalidFileNameChars();
        var sanitised = new string(name.Where(c => !invalid.Contains(c)).ToArray());

        // Also remove colons and other common problematic chars
        sanitised = Regex.Replace(sanitised, @"[:\?\*""<>\|]", "");

        return sanitised.Trim();
    }

    private static int? GetYear(UpcomingItem item)
    {
        var date = item.ReleaseDate ?? item.AvailableDate;
        return date?.Year;
    }

    /// <summary>Builds the standard folder name: "Title (Year)" or just "Title".</summary>
    private static string GetFolderName(UpcomingItem item)
    {
        var sanitisedTitle = SanitiseFileName(item.Title);
        var year = GetYear(item);
        return year.HasValue
            ? $"{sanitisedTitle} ({year.Value})"
            : sanitisedTitle;
    }

    /// <summary>
    /// Generates a minimal but valid Matroska (.mkv) file.
    /// Contains EBML header + Segment with SegmentInfo and a single video Track.
    /// This is the smallest file Jellyfin/ffprobe will recognise as a video.
    /// </summary>
    private static byte[] GenerateMinimalMkv()
    {
        using var ms = new MemoryStream();

        // ── EBML Header ──
        var ebmlContent = new MemoryStream();
        WriteEbmlUInt(ebmlContent, 0x4286, 1);            // EBMLVersion = 1
        WriteEbmlUInt(ebmlContent, 0x42F7, 1);            // EBMLReadVersion = 1
        WriteEbmlUInt(ebmlContent, 0x42F2, 4);            // EBMLMaxIDLength = 4
        WriteEbmlUInt(ebmlContent, 0x42F3, 8);            // EBMLMaxSizeLength = 8
        WriteEbmlString(ebmlContent, 0x4282, "matroska"); // DocType
        WriteEbmlUInt(ebmlContent, 0x4287, 4);            // DocTypeVersion = 4
        WriteEbmlUInt(ebmlContent, 0x4285, 2);            // DocTypeReadVersion = 2
        WriteMasterElement(ms, 0x1A45DFA3, ebmlContent.ToArray());

        // ── Segment ──
        // Build from innermost elements outward so sizes are known.

        // Video settings
        var videoContent = new MemoryStream();
        WriteEbmlUInt(videoContent, 0xB0, 1920);          // PixelWidth
        WriteEbmlUInt(videoContent, 0xBA, 1080);          // PixelHeight

        // TrackEntry
        var trackContent = new MemoryStream();
        WriteEbmlUInt(trackContent, 0xD7, 1);             // TrackNumber = 1
        WriteEbmlUInt(trackContent, 0x73C5, 1);           // TrackUID = 1
        WriteEbmlUInt(trackContent, 0x83, 1);             // TrackType = video
        WriteEbmlString(trackContent, 0x86, "V_MPEG4/ISO/AVC"); // CodecID
        WriteMasterElement(trackContent, 0xE0, videoContent.ToArray()); // Video sub-element

        // Tracks
        var tracksContent = new MemoryStream();
        WriteMasterElement(tracksContent, 0xAE, trackContent.ToArray()); // TrackEntry

        // SegmentInfo
        var infoContent = new MemoryStream();
        WriteEbmlUInt(infoContent, 0x2AD7B1, 1000000);           // TimecodeScale = 1 ms
        WriteEbmlString(infoContent, 0x4D80, "UpcomingMedia");   // MuxingApp
        WriteEbmlString(infoContent, 0x5741, "UpcomingMedia");   // WritingApp
        WriteEbmlFloat(infoContent, 0x4489, 0.0);                // Duration = 0

        // Segment = SegmentInfo + Tracks
        var segContent = new MemoryStream();
        WriteMasterElement(segContent, 0x1549A966, infoContent.ToArray());
        WriteMasterElement(segContent, 0x1654AE6B, tracksContent.ToArray());

        WriteMasterElement(ms, 0x18538067, segContent.ToArray());

        return ms.ToArray();
    }

    // ── EBML writing helpers ─────────────────────────────────

    /// <summary>Writes a master (container) EBML element: ID + data-size + content bytes.</summary>
    private static void WriteMasterElement(MemoryStream ms, uint id, byte[] content)
    {
        WriteEbmlId(ms, id);
        WriteEbmlSize(ms, content.Length);
        ms.Write(content, 0, content.Length);
    }

    private static void WriteEbmlId(MemoryStream ms, uint id)
    {
        if (id <= 0x7F) { ms.WriteByte((byte)id); }
        else if (id <= 0x3FFF) { ms.WriteByte((byte)(id >> 8)); ms.WriteByte((byte)id); }
        else if (id <= 0x1FFFFF) { ms.WriteByte((byte)(id >> 16)); ms.WriteByte((byte)(id >> 8)); ms.WriteByte((byte)id); }
        else { ms.WriteByte((byte)(id >> 24)); ms.WriteByte((byte)(id >> 16)); ms.WriteByte((byte)(id >> 8)); ms.WriteByte((byte)id); }
    }

    private static void WriteEbmlSize(MemoryStream ms, int size)
    {
        if (size < 0x7F) { ms.WriteByte((byte)(size | 0x80)); }
        else if (size < 0x3FFF) { ms.WriteByte((byte)((size >> 8) | 0x40)); ms.WriteByte((byte)size); }
        else if (size < 0x1FFFFF) { ms.WriteByte((byte)((size >> 16) | 0x20)); ms.WriteByte((byte)(size >> 8)); ms.WriteByte((byte)size); }
        else { ms.WriteByte((byte)((size >> 24) | 0x10)); ms.WriteByte((byte)(size >> 16)); ms.WriteByte((byte)(size >> 8)); ms.WriteByte((byte)size); }
    }

    private static void WriteEbmlUInt(MemoryStream ms, uint id, long value)
    {
        WriteEbmlId(ms, id);
        int bytes = 1;
        long v = value;
        while (v > 0xFF) { bytes++; v >>= 8; }
        WriteEbmlSize(ms, bytes);
        for (int i = bytes - 1; i >= 0; i--)
        {
            ms.WriteByte((byte)((value >> (i * 8)) & 0xFF));
        }
    }

    private static void WriteEbmlString(MemoryStream ms, uint id, string value)
    {
        var strBytes = System.Text.Encoding.ASCII.GetBytes(value);
        WriteEbmlId(ms, id);
        WriteEbmlSize(ms, strBytes.Length);
        ms.Write(strBytes, 0, strBytes.Length);
    }

    private static void WriteEbmlFloat(MemoryStream ms, uint id, double value)
    {
        WriteEbmlId(ms, id);
        WriteEbmlSize(ms, 8);
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        ms.Write(bytes, 0, 8);
    }
}
