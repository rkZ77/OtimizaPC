using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rkzfps.Core.Json;

public static class RkzfpsJson
{
    // snake_case nas propriedades e MAIUSCULO nos enums, o mesmo formato que a
    // API em FastAPI vai ler e escrever: o catálogo sai do banco pelo admin e
    // chega aqui sem nenhuma tradução no meio.
    public static readonly JsonSerializerOptions Options = Create(indented: true);

    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            WriteIndented = indented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        return o;
    }
}
