using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ufo.Abstractions.Options;
using Ufo.Database.Repositories;

namespace Ufo.UnitTests;

[Collection("Sequential")]
public class FileSystemSqLiteRepositoryTests : BaseTest
{
    private static readonly Mock<ILogger<FileSystemSqLiteRepository>> _loggerMock = new();
    private static readonly DatabaseOptions _databaseOptions = new DatabaseOptions() { ConnectionString = "Data Source=d:\\Tmp\\dev\\test-indexed-file-system.db" };
    private static readonly Mock<IOptionsMonitor<DatabaseOptions>> _optionsMonitorMock = new();

    private readonly FileSystemSqLiteRepository _sut;


    public FileSystemSqLiteRepositoryTests()
    {
        _optionsMonitorMock.Setup(o => o.CurrentValue).Returns(_databaseOptions);

        _sut = new FileSystemSqLiteRepository(_optionsMonitorMock.Object, _loggerMock.Object);
    }


    [Fact]
    public void Test()
    {

    }
}