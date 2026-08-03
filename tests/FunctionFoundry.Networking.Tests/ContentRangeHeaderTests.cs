using FunctionFoundry.Networking;

namespace FunctionFoundry.Networking.Tests;

public sealed class ContentRangeHeaderTests
{
    [Theory]
    [InlineData("bytes 0-99/1234", 0L, 99L, 1234L, false)]
    [InlineData("bytes 100-199/*", 100L, 199L, null, true)]
    public void Parses_valid_ranges(string header, long? start, long? end, long? total, bool unknown)
    {
        Assert.True(ContentRangeHeader.TryParse(header, out ContentRangeInfo info));
        Assert.Equal(start, info.Start);
        Assert.Equal(end, info.End);
        Assert.Equal(total, info.TotalLength);
        Assert.Equal(unknown, info.IsLengthUnknown);
    }

    [Fact]
    public void Parses_star_form_and_formats()
    {
        Assert.True(ContentRangeHeader.TryParse("bytes */500", out ContentRangeInfo info));
        Assert.Null(info.Start);
        Assert.Equal(500, info.TotalLength);
        Assert.Equal("bytes 0-9/100", ContentRangeHeader.Format(0, 9, 100));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bytes abc")]
    [InlineData("bytes 10-5/20")]
    public void Rejects_invalid(string header)
    {
        Assert.False(ContentRangeHeader.TryParse(header, out _));
    }
}
