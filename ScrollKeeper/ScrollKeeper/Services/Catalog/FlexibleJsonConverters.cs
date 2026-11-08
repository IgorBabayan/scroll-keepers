using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrollKeeper.Services.Catalog;

/// <summary>
/// The ESOUI API sends most numbers as strings ("UIDownloadTotal": "1234") but some as numbers:
/// read either into a string.
/// </summary>
internal sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => SkipAndReturnNull(ref reader)
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }

    private static string? SkipAndReturnNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}

/// <summary>Reads a list of strings that may be missing, <c>null</c>, a single string or contain numbers.</summary>
internal sealed class FlexibleStringListConverter : JsonConverter<List<string>?>
{
    public override bool HandleNull => true;

    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return [reader.GetString()!];
            case JsonTokenType.StartArray:
                var list = new List<string>();
                var item = new FlexibleStringConverter();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var value = item.Read(ref reader, typeof(string), options);
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                }

                return list;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

internal static class FlexibleParse
{
    public static long ToInt64(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    /// <summary>ESOUI dates are Unix times in milliseconds.</summary>
    public static DateTimeOffset? ToDate(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) || ms <= 0)
            return null;

        // Some older entries use seconds.
        if (ms < 100_000_000_000)
            ms *= 1000;

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
