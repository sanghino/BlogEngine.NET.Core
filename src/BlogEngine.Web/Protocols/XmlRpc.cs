using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BlogEngine.Web.Protocols;

/// <summary>Richiesta XML-RPC: nome del metodo e parametri già convertiti in tipi .NET.</summary>
public sealed record XmlRpcCall(string Method, IReadOnlyList<object?> Parameters)
{
    public string String(int index) => index < Parameters.Count ? Parameters[index]?.ToString() ?? string.Empty : string.Empty;
}

/// <summary>Errore XML-RPC con il suo codice numerico.</summary>
public sealed class XmlRpcFaultException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// Lettura e scrittura del protocollo XML-RPC (usato da pingback e MetaWeblog). I valori diventano
/// string, int, bool, double, DateTime, byte[] (base64), Dictionary (struct) e List (array).
/// </summary>
public static class XmlRpc
{
    public static XmlRpcCall Parse(string xml)
    {
        XDocument document;
        try
        {
            // Nessuna DTD né entità esterne: il testo arriva da chiunque.
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new XmlRpcFaultException(-32700, "Parse error: " + ex.Message);
        }

        var method = document.Root?.Element("methodName")?.Value.Trim();
        if (string.IsNullOrEmpty(method))
        {
            throw new XmlRpcFaultException(-32600, "Invalid request: methodName missing.");
        }

        var parameters = document.Root!.Element("params")?.Elements("param")
            .Select(p => ReadValue(p.Element("value")))
            .ToList() ?? [];
        return new XmlRpcCall(method, parameters);
    }

    public static string Response(object? value) => Write(writer =>
    {
        writer.WriteStartElement("methodResponse");
        writer.WriteStartElement("params");
        writer.WriteStartElement("param");
        WriteValue(writer, value);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    public static string Fault(int code, string message) => Write(writer =>
    {
        writer.WriteStartElement("methodResponse");
        writer.WriteStartElement("fault");
        WriteValue(writer, new Dictionary<string, object?> { ["faultCode"] = code, ["faultString"] = message });
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static object? ReadValue(XElement? value)
    {
        if (value is null)
        {
            return null;
        }

        var typed = value.Elements().FirstOrDefault();
        if (typed is null)
        {
            return value.Value;
        }

        return typed.Name.LocalName switch
        {
            "string" => typed.Value,
            "int" or "i4" or "i8" => int.TryParse(typed.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0,
            "boolean" => typed.Value.Trim() is "1" or "true",
            "double" => double.TryParse(typed.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d,
            "dateTime.iso8601" => ParseDate(typed.Value.Trim()),
            "base64" => Convert.FromBase64String(typed.Value.Trim()),
            "struct" => typed.Elements("member").ToDictionary(
                m => m.Element("name")?.Value ?? string.Empty,
                m => ReadValue(m.Element("value")),
                StringComparer.OrdinalIgnoreCase),
            "array" => typed.Element("data")?.Elements("value").Select(ReadValue).ToList() ?? [],
            "nil" => null,
            _ => typed.Value,
        };
    }

    /// <summary>Date XML-RPC ("20250309T10:30:00", con o senza fuso e trattini).</summary>
    private static DateTime ParseDate(string text)
    {
        string[] formats = ["yyyyMMdd'T'HH:mm:ss", "yyyyMMdd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ssK", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HH:mm:ss'Z'"];
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out date) ? date : DateTime.UtcNow;
    }

    private static void WriteValue(XmlWriter writer, object? value)
    {
        writer.WriteStartElement("value");
        switch (value)
        {
            case null:
                writer.WriteElementString("string", string.Empty);
                break;
            case string text:
                writer.WriteElementString("string", text);
                break;
            case bool flag:
                writer.WriteElementString("boolean", flag ? "1" : "0");
                break;
            case int or long or short:
                writer.WriteElementString("int", Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case double or float or decimal:
                writer.WriteElementString("double", Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case DateTime date:
                writer.WriteElementString("dateTime.iso8601", date.ToUniversalTime().ToString("yyyyMMdd'T'HH:mm:ss", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteElementString("base64", Convert.ToBase64String(bytes));
                break;
            case IDictionary<string, object?> map:
                writer.WriteStartElement("struct");
                foreach (var (name, member) in map)
                {
                    writer.WriteStartElement("member");
                    writer.WriteElementString("name", name);
                    WriteValue(writer, member);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                break;
            case System.Collections.IEnumerable list:
                writer.WriteStartElement("array");
                writer.WriteStartElement("data");
                foreach (var item in list)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                break;
            default:
                writer.WriteElementString("string", value.ToString());
                break;
        }

        writer.WriteEndElement();
    }

    private static string Write(Action<XmlWriter> write) => FeedWriter.Write(write);
}
