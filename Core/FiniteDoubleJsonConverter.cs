using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class FiniteDoubleJsonConverter : JsonConverter<double> {
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) return double.NaN;
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble();
        throw new JsonException($"Unexpected token {reader.TokenType} for double value.");
    }
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) {
        if (!double.IsFinite(value)) writer.WriteNullValue(); else writer.WriteNumberValue(value);
    }
}
