using Newtonsoft.Json;

namespace BlogEngine.Web.Api;

/// <summary>
/// L'amministrazione AngularJS crea gli oggetti nuovi con <c>"Id": ""</c>: la vecchia Web API lo
/// trattava come "nessun id", qui lo stesso. Accetta anche gli id nel formato con o senza trattini.
/// </summary>
public sealed class LenientGuidConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(Guid) || objectType == typeof(Guid?);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var text = reader.TokenType switch
        {
            JsonToken.Null or JsonToken.Undefined => null,
            JsonToken.String => (string?)reader.Value,
            _ => throw new JsonSerializationException($"Valore non valido per un id: {reader.TokenType}."),
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return objectType == typeof(Guid?) ? null : Guid.Empty;
        }

        return Guid.TryParse(text, out var id) ? id : throw new JsonSerializationException($"Id non valido: \"{text}\".");
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => writer.WriteValue(value?.ToString());
}

/// <summary>
/// Alcuni oggetti iniziali dell'amministrazione hanno gli elenchi (tag, categorie, commenti) impostati
/// a <c>""</c> invece che a <c>[]</c>: una stringa vuota diventa un elenco vuoto.
/// </summary>
public sealed class LenientListConverter : JsonConverter
{
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType) => ElementType(objectType) is not null;

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType is JsonToken.Null or JsonToken.Undefined)
        {
            return null;
        }

        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ElementType(objectType)!))!;
        if (reader.TokenType == JsonToken.String && string.IsNullOrWhiteSpace((string?)reader.Value))
        {
            return list;
        }

        // Il contenuto viene letto normalmente (gli altri convertitori valgono anche per gli elementi).
        serializer.Populate(reader, list);
        return list;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();

    /// <summary>Tipo degli elementi per List&lt;T&gt; e per le interfacce che List&lt;T&gt; implementa.</summary>
    private static Type? ElementType(Type type)
    {
        if (!type.IsGenericType || type.GetGenericArguments().Length != 1)
        {
            return null;
        }

        var element = type.GetGenericArguments()[0];
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) || (type.IsInterface && type.IsAssignableFrom(typeof(List<>).MakeGenericType(element)))
            ? element
            : null;
    }
}
