//using System.Text.Json;
//using System.Text.Json.Serialization;

//namespace Ufo.Server.Converters;

//public class UlidJsonConverter1 : JsonConverter<Ulid> // TODO LA - Delete
//{
//    public override Ulid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//    {
//        if (reader.TokenType == JsonTokenType.String)
//        {
//            if (Ulid.TryParse(reader.GetString(), out var ulid))
//            {
//                return ulid;
//            }
//        }
//        throw new JsonException($"Failed to parse Ulid from JSON: {reader.GetString()}");
//    }

//    public override void Write(Utf8JsonWriter writer, Ulid value, JsonSerializerOptions options)
//    {
//        writer.WriteStringValue(value.ToString());
//    }
//}
