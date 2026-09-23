using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class ExportArchiveWriterTests : BaseTest
{
    [Fact]
    public void Write_ProducesAZipWithOneJsonEntryNamedAfterTheBaseName()
    {
        var document = new ExportDocument { Scope = "user", ApplicationVersion = "1.2.3" };

        var bytes = ExportArchiveWriter.Write(document, "ufo-export-user-20260913-170311");

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.Entries.Should().ContainSingle().Which.FullName.Should().Be("ufo-export-user-20260913-170311.json");
    }

    [Fact]
    public void Write_WritesTheDocumentInTheApisJsonConventions()
    {
        var tagId = Ulid.NewUlid();
        var document = new ExportDocument
        {
            Scope = "filesystem",
            ApplicationVersion = "1.2.3",
            FileSystem = new FileSystemExport
            {
                FlaggedPaths = ["/data/report.pdf"],
                Tags = [new TagDto { Id = tagId, Name = "Important", ColorHex = "#00ff00" }]
            }
        };

        var json = ReadJson(ExportArchiveWriter.Write(document, "export"));

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        // camelCase, as every API response is.
        root.GetProperty("format").GetString().Should().Be(ExportDocument.FormatName);
        root.GetProperty("formatVersion").GetInt32().Should().Be(ExportDocument.CurrentFormatVersion);
        root.GetProperty("fileSystem").GetProperty("flaggedPaths")[0].GetString().Should().Be("/data/report.pdf");
        // Ulids as their 26-character strings, not as objects.
        root.GetProperty("fileSystem").GetProperty("tags")[0].GetProperty("id").GetString().Should().Be(tagId.ToString());
        // Sections not asked for are absent, not null.
        root.TryGetProperty("user", out _).Should().BeFalse();
        root.TryGetProperty("snapshots", out _).Should().BeFalse();
        // Indented: a person will open this.
        json.Should().Contain("\n  ");
    }

    [Fact]
    public void Write_RoundTripsThroughTheSameOptions()
    {
        var document = new ExportDocument
        {
            Scope = "all",
            ApplicationVersion = "0.1.1",
            ExportedAt = new DateTimeOffset(2026, 9, 13, 17, 3, 11, TimeSpan.Zero),
            User = new UserExport { Id = Ulid.NewUlid(), Name = "alex", IsAdmin = true },
            Snapshots = new SnapshotsExport()
        };

        var json = ReadJson(ExportArchiveWriter.Write(document, "export"));
        var readBack = JsonSerializer.Deserialize<ExportDocument>(json, ExportArchiveWriter.JsonOptions);

        readBack.Should().BeEquivalentTo(document);
    }

    [Fact]
    public void Write_RefusesAnEmptyBaseName()
    {
        var write = () => ExportArchiveWriter.Write(new ExportDocument(), " ");

        write.Should().Throw<ArgumentException>();
    }

    private static string ReadJson(byte[] zipBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.Entries.Single().Open());

        return reader.ReadToEnd();
    }
}
