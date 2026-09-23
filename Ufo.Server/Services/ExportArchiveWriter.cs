using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cysharp.Serialization.Json;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Services;

/// <summary>
/// Turns an <see cref="ExportDocument"/> into the bytes of a zip archive holding
/// one JSON file.
/// </summary>
/// <remarks>
/// Zipped rather than served as bare JSON because a snapshot export of a large
/// tree is tens of megabytes of very repetitive text, and compresses to a small
/// fraction of that. The JSON is written with the same conventions as the API -
/// camelCase, Ulids as strings, nulls left out - so a reader who knows one
/// knows the other. Indented, because a person will open it.
/// </remarks>
public static class ExportArchiveWriter
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new UlidJsonConverter() }
    };

    /// <param name="baseFileName">The name without an extension; the entry inside is <c>baseFileName.json</c>.</param>
    public static byte[] Write(ExportDocument document, string baseFileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);

        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry($"{baseFileName}.json", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            JsonSerializer.Serialize(entryStream, document, JsonOptions);
        }

        return archiveStream.ToArray();
    }
}
