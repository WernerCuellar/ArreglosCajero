using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CajeroRed.Protocolo;

namespace CajeroRed.Aplicacion;

public sealed partial class ClienteTcp
{
 public sealed record PendienteOperacion(string Referencia,string Direccion,int Puerto,string Cajero,int? UsuarioId,string Tipo,decimal Monto,DateTimeOffset FechaUtc);
 private readonly SemaphoreSlim movimiento=new(1,1);
 private string? carpetaPendientes;
 private PendienteOperacion? pendienteActual;
 public int? UsuarioActual {get;private set;}
 public string? ErrorRecuperacion {get;private set;}
 public bool MovimientoEnCurso=>movimiento.CurrentCount==0;
 public bool MovimientosBloqueados=>MovimientoEnCurso||SolicitudIncierta!=null||ErrorRecuperacion!=null;
 public event Action? PendienteCambiado;
 public void ConfigurarPendientes(string carpeta)=>carpetaPendientes=Path.GetFullPath(carpeta);
 public void EstablecerSesion(string sesion,int usuarioId)
 {
  if(string.IsNullOrWhiteSpace(sesion)||usuarioId<=0)throw new ArgumentException("La sesión no contiene identidad válida.");
  Sesion=sesion;UsuarioActual=usuarioId;RestaurarPendiente();
 }
 public void CerrarSesionLocal(){Sesion=null;UsuarioActual=null;}
 private string Archivo(PendienteOperacion p)
 {
  string clave=$"{p.Direccion.Trim().ToUpperInvariant()}|{p.Puerto}|{p.Cajero.ToUpperInvariant()}|{p.UsuarioId}";
  return Path.Combine(carpetaPendientes!,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)))+".json");
 }
 private void RestaurarPendiente()
 {
  ErrorRecuperacion=null;pendienteActual=null;SolicitudIncierta=null;
  if(carpetaPendientes==null){PendienteCambiado?.Invoke();return;}
  var contexto=new PendienteOperacion("",Direccion,Puerto,Cajero,UsuarioActual,"",0,DateTimeOffset.UtcNow);
  try
  {
   string archivo=Archivo(contexto);
   if(File.Exists(archivo))
   {
    if(new FileInfo(archivo).Length>4096)throw new JsonException("Registro demasiado grande.");
    var p=JsonSerializer.Deserialize<PendienteOperacion>(File.ReadAllText(archivo),Mensaje.Json)??throw new JsonException();
    if(p.UsuarioId!=UsuarioActual||!p.Direccion.Equals(Direccion,StringComparison.OrdinalIgnoreCase)||p.Puerto!=Puerto||
      p.Cajero!=Cajero||string.IsNullOrWhiteSpace(p.Referencia)||p.Referencia.Length>64||p.Tipo is not ("RETIRO" or "DEPOSITO"))throw new JsonException();
    pendienteActual=p;SolicitudIncierta=p.Referencia;
   }
  }
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or NullReferenceException)
  {ErrorRecuperacion="No se pudo leer la referencia pendiente. Los movimientos quedan bloqueados; consulte al administrador.";}
  PendienteCambiado?.Invoke();
 }
 private void GuardarPendiente(PendienteOperacion p)
 {
  if(carpetaPendientes!=null)
  {
   if(p.UsuarioId==null)throw new InvalidOperationException("El servidor debe informar usuarioId al abrir la sesión. Actualice su servidor.");
   Directory.CreateDirectory(carpetaPendientes);
   string archivo=Archivo(p),temporal=archivo+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temporal,JsonSerializer.Serialize(p,Mensaje.Json));File.Move(temporal,archivo,true);}
   finally{if(File.Exists(temporal))File.Delete(temporal);}
  }
  pendienteActual=p;SolicitudIncierta=p.Referencia;PendienteCambiado?.Invoke();
 }
 private void BorrarPendiente(PendienteOperacion p)
 {
  try
  {
   if(carpetaPendientes!=null)File.Delete(Archivo(p));
   if(SolicitudIncierta==p.Referencia){pendienteActual=null;SolicitudIncierta=null;ErrorRecuperacion=null;}
  }
  catch(Exception e) when(e is IOException or UnauthorizedAccessException)
  {ErrorRecuperacion="El resultado está confirmado, pero no se pudo eliminar su referencia local. Consulte al administrador antes de otro movimiento.";}
  PendienteCambiado?.Invoke();
 }
 private async Task<Mensaje> EnviarConProteccion(Mensaje m)
 {
  if(m.Tipo is not ("RETIRO" or "DEPOSITO"))return await Solicitar(m,fin.Token);
  if(!movimiento.Wait(0))throw new InvalidOperationException("Ya hay un movimiento en curso.");
  try
  {
   if(SolicitudIncierta!=null||ErrorRecuperacion!=null)throw new InvalidOperationException("Consulte y aclare la operación pendiente antes de retirar o depositar.");
   if(Sesion==null)throw new InvalidOperationException("Inicie sesión antes de realizar un movimiento.");
   // Persistir ANTES de enviar cubre también un cierre inesperado tras COMMIT.
   var p=new PendienteOperacion(m.IdSolicitud,Direccion,Puerto,Cajero,UsuarioActual,m.Tipo,
    m.Datos.TryGetProperty("monto",out var monto)&&monto.TryGetDecimal(out decimal valor)?valor:0,DateTimeOffset.UtcNow);
   GuardarPendiente(p);
   try
   {
    var r=await Solicitar(m,fin.Token);
    if(r.Codigo!="96")BorrarPendiente(p);
    return r;
   }
   catch(Exception e) when(e is TimeoutException or IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException)
   {PendienteCambiado?.Invoke();throw;}
  }
  finally{movimiento.Release();PendienteCambiado?.Invoke();}
 }
 public void ConfirmarConsultaPendiente(Mensaje resultado)
 {
  if(pendienteActual==null||resultado.Codigo!="00"||
   !resultado.Datos.TryGetProperty("idOriginal",out var id)||id.GetString()!=pendienteActual.Referencia||
   !resultado.Datos.TryGetProperty("usuarioId",out var usuario)||usuario.GetInt32()!=UsuarioActual||
   !resultado.Datos.TryGetProperty("codigo",out var codigo)||codigo.GetString()=="96")
   throw new InvalidOperationException("La respuesta no confirma la operación del usuario actual.");
  BorrarPendiente(pendienteActual);
 }
}
