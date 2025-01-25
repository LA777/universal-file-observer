using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ufo.Abstractions.Options;
using Ufo.Database.Repositories;
using System.Windows.Input;
using Microsoft.Identity.Client;

namespace Ufo.UnitTests
{
    [Collection("Sequential")]
    public class FileSystemSqLiteRepositoryTests : BaseTest
    {
        private static readonly Mock<ILogger<FileSystemSqLiteRepository>> _loggerMock = new();
        private static readonly ApplicationSettings applicationSettings = new ApplicationSettings() { SqliteDbConnectionStrings = "Data Source=d:\\Tmp\\dev\\test-indexed-file-system.db" };
        private static readonly Mock<IOptionsMonitor<ApplicationSettings>> _optionsMonitorMock = new();

        private readonly FileSystemSqLiteRepository _sut;


        public FileSystemSqLiteRepositoryTests()
        {
            _optionsMonitorMock.Setup(o => o.CurrentValue).Returns(applicationSettings);

            _sut = new FileSystemSqLiteRepository(_optionsMonitorMock.Object, _loggerMock.Object);
        }


        [Fact]
        public void Test()
        {

        }
    }
}