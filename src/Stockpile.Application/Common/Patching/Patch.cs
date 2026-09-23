using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stockpile.Application.Common.Patching;

/// <summary>
/// Tells "the client omitted this field" apart from "the client sent null to clear it".
/// <para>
/// A plain <c>string?</c> collapses both into null, so <c>command.Email ?? supplier.Email</c>
/// cannot do anything but keep the old value. The only way left to clear a supplier's email
/// is to send <c>""</c>, which works by accident — UpdateDetails treats whitespace as null —
/// while the spelling RFC 7386 defines for it returns 200 and changes nothing.
/// </para>
/// <para>
/// System.Text.Json only invokes a converter for a property that is PRESENT in the payload.
/// An omitted field therefore leaves <c>default(Patch&lt;T&gt;)</c>, whose IsSet is false.
/// That is the entire mechanism.
/// </para>
/// </summary>
[JsonConverter(typeof(PatchJsonConverterFactory))]
public readonly record struct Patch<T>(bool IsSet, T? Value)
{
    /// <summary>
    /// The value the client sent — null included — or <paramref name="current"/> when they
    /// sent nothing at all.
    /// </summary>
    public T? Or(T? current) => IsSet ? Value : current;
}

public sealed class PatchJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(PatchJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class PatchJsonConverter<T> : JsonConverter<Patch<T>>
{
    // Without this, System.Text.Json handles a null token itself and hands over
    // default(Patch&lt;T&gt;) — IsSet false — which is precisely the case this type exists to
    // distinguish. Reaching Read at all is what proves the property was present.
    public override bool HandleNull => true;

    public override Patch<T> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? new Patch<T>(IsSet: true, Value: default)
            : new Patch<T>(IsSet: true, JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options)
    {
        if (value.Value is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
