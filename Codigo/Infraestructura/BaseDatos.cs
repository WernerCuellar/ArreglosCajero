using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using CajeroRed.Protocolo;

namespace CajeroRed.Infraestructura;

public sealed class BaseDatos
{
 private readonly bool permisosLimitados;
 private readonly string cadenaMonitoreo;
 public string Cadena {get;}
 public BaseDatos(string cadena,bool permisosLimitados=true)
 {
  this.permisosLimitados=permisosLimitados;
  var b=new SqlConnectionStringBuilder(cadena){Pooling=true,MaxPoolSize=100,MinPoolSize=0,ConnectTimeout=5,ApplicationName="NETGEAR"};
  Cadena=b.ConnectionString;cadenaMonitoreo=new SqlConnectionStringBuilder(Cadena){ApplicationName="NETGEAR.Monitoreo",MaxPoolSize=16}.ConnectionString;
 }
 private sealed class Conexion(SqlConnection sql,bool limitada):IAsyncDisposable
 {
  public SqlConnection Sql {get;}=sql;
  public async ValueTask DisposeAsync()
  {
   if(limitada)
   {
    try
    {
     await using var revertir=new SqlCommand("REVERT;",Sql){CommandTimeout=3};
     await revertir.ExecuteNonQueryAsync();
    }
    catch
    {
     // Nunca devolver al pool un contexto que no pudo restaurarse.
     SqlConnection.ClearPool(Sql);
    }
   }
   await Sql.DisposeAsync();
  }
 }
 private async Task<Conexion> Abrir(bool monitoreo=false)
 {
  var con=new SqlConnection(monitoreo?cadenaMonitoreo:Cadena);
  try
  {
   await con.OpenAsync();
   if(permisosLimitados)
   {
    await using var contexto=new SqlCommand("EXECUTE AS USER = 'CajeroServidor';",con){CommandTimeout=3};
    await contexto.ExecuteNonQueryAsync();
   }
   return new Conexion(con,permisosLimitados);
  }
  catch{SqlConnection.ClearPool(con);await con.DisposeAsync();throw;}
 }
 public Task<DataTable> Consultar(string sql,params (string,object?)[] parametros)=>ConsultarInterno(sql,false,parametros);
 public Task<DataTable> ConsultarMonitoreo(string sql,params (string,object?)[] parametros)=>ConsultarInterno(sql,true,parametros);
 private async Task<DataTable> ConsultarInterno(string sql,bool monitoreo,params (string,object?)[] parametros)
 {
  await using var conexion=await Abrir(monitoreo);
  await using var cmd=new SqlCommand(sql,conexion.Sql){CommandTimeout=monitoreo?3:8};
  foreach(var (nombre,valor) in parametros)cmd.Parameters.AddWithValue(nombre,valor??DBNull.Value);
  await using var reader=await cmd.ExecuteReaderAsync();
  var tabla=new DataTable();tabla.Load(reader);return tabla;
 }
 public async Task Ejecutar(string sql,params (string,object?)[] parametros)=>await Consultar(sql,parametros);
 public async Task<Mensaje> Atender(Mensaje m)
 {
  var nombres=new Dictionary<string,string>{
   ["REGISTRAR_CAJERO"]="sp_RegistrarCajero",["HEARTBEAT"]="sp_Heartbeat",
   ["LOGIN"]="sp_ValidarCredenciales",["VALIDAR_TOKEN"]="sp_ValidarToken",
   ["RETIRO"]="sp_Retiro",["DEPOSITO"]="sp_Deposito",["CAMBIO_PIN"]="sp_CambiarPin",
   ["CONSULTA_SALDO"]="sp_ConsultaUsuario",["ULTIMAS_TRANSACCIONES"]="sp_UltimasTransacciones",
   ["LOGOUT"]="sp_CerrarSesion",["ESTADO_SOLICITUD"]="sp_EstadoSolicitud"};
  if(!nombres.TryGetValue(m.Tipo,out var proc))return Mensaje.Respuesta(m,"30","Mensaje no reconocido.");
  bool monitoreo=m.Tipo is "HEARTBEAT" or "REGISTRAR_CAJERO";
  await using var con=await Abrir(monitoreo);
  await using var cmd=new SqlCommand(proc,con.Sql){CommandType=CommandType.StoredProcedure,CommandTimeout=monitoreo?3:8};
  cmd.Parameters.AddWithValue("@Json",m.Datos.GetRawText());cmd.Parameters.AddWithValue("@Cajero",m.Cajero);
  cmd.Parameters.AddWithValue("@Sesion",(object?)m.Sesion??DBNull.Value);cmd.Parameters.AddWithValue("@Solicitud",m.IdSolicitud);
  await using var rd=await cmd.ExecuteReaderAsync();
  if(!await rd.ReadAsync())throw new InvalidOperationException("Sin resultado de SQL.");
  return Mensaje.Respuesta(m,rd.GetString(0),rd.GetString(1),JsonSerializer.Deserialize<JsonElement>(rd.IsDBNull(2)?"{}":rd.GetString(2)));
 }
 public async Task Auditoria(string origen,string accion,string detalle)=>await Ejecutar(
  "INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@o,@a,@d)",("@o",origen),("@a",accion),("@d",detalle));
}
