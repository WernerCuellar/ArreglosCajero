using System.Text.Json;
using System.Text.Json.Serialization;

namespace CajeroRed.Protocolo;

public sealed record Mensaje
{
    public string Tipo { get; init; } = "";
    public string IdSolicitud { get; init; } = Guid.NewGuid().ToString("N");
    public string Cajero { get; init; } = "";
    public string? Sesion { get; init; }
    public string Codigo { get; init; } = "";
    [JsonPropertyName("mensaje")]
    public string MensajeTexto { get; init; } = "";
    public JsonElement Datos { get; init; } = JsonSerializer.SerializeToElement(new { });

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public string Serializar() => JsonSerializer.Serialize(this, Json);
    public static Mensaje Leer(string s) => JsonSerializer.Deserialize<Mensaje>(s, Json)
        ?? throw new JsonException("Mensaje vacío.");
    public static Mensaje Solicitud(string tipo, string cajero, object? datos = null, string? sesion = null) => new()
    { Tipo = tipo, Cajero = cajero, Sesion = sesion, Datos = JsonSerializer.SerializeToElement(datos ?? new { }, Json) };
    public static Mensaje Respuesta(Mensaje solicitud, string codigo, string texto, object? datos = null) => new()
    { Tipo = "RESPUESTA", IdSolicitud = solicitud.IdSolicitud, Cajero = solicitud.Cajero, Codigo = codigo,
      MensajeTexto = texto, Datos = JsonSerializer.SerializeToElement(datos ?? new { }, Json) };
}

public static class Billetes
{
    public static readonly int[] Denominaciones = [200, 100, 50, 20, 10, 5, 1];
    public static decimal Total(IReadOnlyDictionary<int, int> desglose)
    {
        if (desglose.Count == 0 || desglose.Any(x => !Denominaciones.Contains(x.Key) || x.Value < 0 || x.Value > 30000))
            throw new ArgumentException("Desglose inválido.");
        return desglose.Sum(x => (decimal)x.Key * x.Value);
    }
    // Búsqueda acotada con memoización: greedy puro falla con existencias limitadas.
    public static Dictionary<int,int>? Alternativa(int monto, IReadOnlyDictionary<int,int> inventario)
    {
        if (monto is <= 0 or > 30000) return null;
        var fallos = new HashSet<(int,int)>();
        var resultado = new Dictionary<int,int>();
        bool Buscar(int i, int resto)
        {
            if (resto == 0) return true;
            if (i == Denominaciones.Length || !fallos.Add((i, resto))) return false;
            int d = Denominaciones[i];
            for (int n = Math.Min(resto/d, inventario.GetValueOrDefault(d)); n >= 0; n--)
            { resultado[d] = n; if (Buscar(i+1, resto-n*d)) return true; }
            resultado.Remove(d); return false;
        }
        return Buscar(0, monto) ? resultado.Where(x => x.Value > 0).ToDictionary() : null;
    }
}
