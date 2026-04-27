using Microsoft.Extensions.Logging;
using Moq;
using Ufo.DataProviders;

namespace Ufo.UnitTests.Server.DataProviders;

public class SystemInfoProviderTests : BaseTest
{
    private readonly Mock<ILogger<SystemInfoProvider>> _loggerMock;
    private readonly SystemInfoProvider _sut;

    public SystemInfoProviderTests()
    {
        _loggerMock = new Mock<ILogger<SystemInfoProvider>>();
        _sut = new SystemInfoProvider(_loggerMock.Object);
    }

    // TODO LA: Implement tests for SystemInfoProvider
    //[Fact]
    //public async Task GetSystemInformation_Test()
    //{
    //    var path1 = "c:\\logs";
    //    var path2 = "d:\\Tmp";

    //    var result1 = _sut.GetSystemInformation(path1);
    //    var result2 = _sut.GetSystemInformation(path2);

    //    var json1 = JsonConvert.SerializeObject(result1);
    //    var json2 = JsonConvert.SerializeObject(result2);


    //    result1.Should().NotBe(result2);
    //}
}
