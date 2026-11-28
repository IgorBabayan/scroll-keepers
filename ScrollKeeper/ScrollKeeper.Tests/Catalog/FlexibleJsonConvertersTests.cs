using System.Text.Json;
using System.Text.Json.Serialization;
using ScrollKeeper.Services.Catalog;

namespace ScrollKeeper.Tests.Catalog;

public sealed class FlexibleJsonConvertersTests
{
    private static readonly JsonSerializerOptions StringOptions = new() { Converters = { new FlexibleStringConverter() } };

    private sealed class ListHolder
    {
        [JsonConverter(typeof(FlexibleStringListConverter))]
        public List<string>? Items { get; set; }
    }

    [Theory]
    [InlineData("\"text\"", "text")]
    [InlineData("1234", "1234")]
    [InlineData("-7", "-7")]
    [InlineData("1.5", "1.5")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", null)]
    [InlineData("{\"a\":1}", null)]
    [InlineData("[1,2]", null)]
    public void FlexibleString_ReadsAnyScalarAsString(string json, string? expected)
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, string?>>($"{{\"v\":{json},\"next\":\"ok\"}}", StringOptions)!;

        Assert.Equal(expected, result["v"]);
        Assert.Equal("ok", result["next"]);
    }

    [Theory]
    [InlineData("value")]
    [InlineData(null)]
    public void FlexibleString_WritesStringOrNull(string? value)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string?> { ["v"] = value }, StringOptions);

        Assert.Equal(value is null ? "{\"v\":null}" : $"{{\"v\":\"{value}\"}}", json);
    }

    [Fact]
    public void FlexibleStringList_ReadsArrayWithNumbers_AndDropsBlankEntries()
    {
        var holder = JsonSerializer.Deserialize<ListHolder>("{\"Items\":[\"a\",2,null,\"\",\"  \",{\"x\":1},\"b\"]}")!;

        Assert.Equal(["a", "2", "b"], holder.Items);
    }

    [Fact]
    public void FlexibleStringList_ReadsSingleStringAsOneItemList()
    {
        var holder = JsonSerializer.Deserialize<ListHolder>("{\"Items\":\"only\"}")!;

        Assert.Equal(["only"], holder.Items);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("{\"a\":[1]}")]
    public void FlexibleStringList_ReturnsNull_ForOtherTokens(string json)
    {
        var holder = JsonSerializer.Deserialize<ListHolder>($"{{\"Items\":{json}}}")!;

        Assert.Null(holder.Items);
    }

    [Fact]
    public void FlexibleStringList_RoundTrips()
    {
        var json = JsonSerializer.Serialize(new ListHolder { Items = ["a", "b"] });

        Assert.Equal("{\"Items\":[\"a\",\"b\"]}", json);
        Assert.Equal(["a", "b"], JsonSerializer.Deserialize<ListHolder>(json)!.Items);
        Assert.Equal("{\"Items\":null}", JsonSerializer.Serialize(new ListHolder()));
    }

    [Theory]
    [InlineData("123", 123)]
    [InlineData("-5", -5)]
    [InlineData("abc", 0)]
    [InlineData("1.5", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ToInt64_ParsesOrReturnsZero(string? value, long expected)
    {
        Assert.Equal(expected, FlexibleParse.ToInt64(value));
    }

    [Fact]
    public void ToDate_ReadsMilliseconds_AndSeconds()
    {
        var expected = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        Assert.Equal(expected, FlexibleParse.ToDate("1700000000000"));
        Assert.Equal(expected, FlexibleParse.ToDate("1700000000"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("999999999999999999")]
    public void ToDate_ReturnsNull_ForInvalidValues(string? value)
    {
        Assert.Null(FlexibleParse.ToDate(value));
    }
}
