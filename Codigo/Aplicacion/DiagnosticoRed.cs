using System.Text.Json;
namespace CajeroRed.Aplicacion;
// Solo metadatos: nunca JSON recibido, PIN, token ni contraseñas.
internal static class DiagnosticoRed
{
 private static readonly object bloqueo=new();
 public static void Registrar(string origen,string cajero,string evento,object datos)
 {
  try
  {
   string carpeta=Path.Combine(AppContext.BaseDirectory,"Logs");
   lock(bloqueo)
   {
    Directory.CreateDirectory(carpeta);
    File.AppendAllText(Path.Combine(carpeta,$"conexiones-{DateTime.UtcNow:yyyyMMdd}.jsonl"),
      JsonSerializer.Serialize(new {fechaUtc=DateTimeOffset.UtcNow,origen,cajero,evento,datos})+Environment.NewLine);
   }
  }
  catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
 }
}
