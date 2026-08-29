using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Ufo.Extensions;

namespace Ufo.UnitTests.Server.Extensions;

public class HttpContextExtensionTests : BaseTest
{
    private readonly DefaultHttpContext _context = new();

    [Fact]
    public void SetUserId_WithUlid_RoundTripsThroughGetUserIdAsUlid()
    {
        var userId = Ulid.NewUlid();

        _context.SetUserId(userId);

        _context.GetUserIdAsUlid().Should().Be(userId);
    }

    [Fact]
    public void SetUserId_WithString_RoundTripsThroughGetUserId()
    {
        var userId = Ulid.NewUlid().ToString();

        _context.SetUserId(userId);

        _context.GetUserId().Should().Be(userId);
    }

    [Fact]
    public void GetUserIdAsUlid_WithUlidStoredDirectly_ReturnsIt()
    {
        var userId = Ulid.NewUlid();
        _context.Items["UserId"] = userId;

        _context.GetUserIdAsUlid().Should().Be(userId);
    }

    [Fact]
    public void GetUserIdAsUlid_WhenMissing_ThrowsUnauthorizedAccessException()
    {
        var act = () => _context.GetUserIdAsUlid();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetUserIdAsUlid_WithInvalidString_ThrowsUnauthorizedAccessException()
    {
        _context.Items["UserId"] = "not-a-ulid";

        var act = () => _context.GetUserIdAsUlid();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetUserIdAsUlid_WithEmptyUlid_ThrowsUnauthorizedAccessException()
    {
        _context.Items["UserId"] = Ulid.Empty;

        var act = () => _context.GetUserIdAsUlid();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetUserId_WhenMissing_ReturnsNull()
    {
        _context.GetUserId().Should().BeNull();
    }

    [Fact]
    public void HasUserId_ReflectsPresence()
    {
        _context.HasUserId().Should().BeFalse();

        _context.SetUserId(Ulid.NewUlid());

        _context.HasUserId().Should().BeTrue();
    }
}
