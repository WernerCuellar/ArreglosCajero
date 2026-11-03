using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Sockets;
using CajeroRed.Infraestructura;
using CajeroRed.Protocolo;
using Microsoft.Data.SqlClient;

namespace CajeroRed.Aplicacion;

public sealed class TransferenciasCajero(BaseDatos bd)
{
 public static bool Atiende(string tipo)=>tipo is "BANCOS_TRANSFERENCIA" or "VERIFICAR_DESTINO" or "TRANSFERIR" or "ESTADO_TRANSFERENCIA" or "HISTORIAL_TRANSFERENCIAS";
 private async Task<DataRow> Operar(Mensaje m,string tipo,object datos)
 {
  var t=await bd.Consultar("EXEC dbo.sp_TransferenciaCajero @Tipo=@t,@Cajero=@c,@Sesion=@s,@Solicitud=@id,@Json=@j",
   ("@t",tipo),("@c",m.Cajero),("@s",m.Sesion),("@id",m.IdSolicitud),("@j",JsonSerializer.Serialize(datos,Mensaje.Json)));
  return t.Rows[0];
 }
 private static JsonElement Ruta(DataRow r)=>JsonSerializer.Deserialize<JsonElement>((string)r["Ruta"]);
 private static Mensaje Respuesta(Mensaje m,DataRow r)=>Mensaje.Respuesta(m,(string)r["Codigo"],(string)r["Mensaje"],JsonSerializer.Deserialize<JsonElement>((string)r["Datos"]));
 private static string Texto(JsonElement j,string campo,int max,bool opcional=false)
 {
  if(!j.TryGetProperty(campo,out var v)&&opcional)return "";
  if(v.ValueKind!=JsonValueKind.String)throw new ArgumentException("Campo inválido: "+campo);
  string s=v.GetString()??"";if(s.Length>max||(!opcional&&string.IsNullOrWhiteSpace(s)))throw new ArgumentException("Campo inválido: "+campo);
  return s;
 }
 private static bool ErrorRed(Exception e)=>e is IOException or SocketException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException;
 private static async Task<BancoInterbancario.Respuesta> VerificarRemoto(JsonElement ruta)
 {
  return await BancoInterbancario.Enviar(ruta.GetProperty("ip").GetString()!,ruta.GetProperty("puerto").GetInt32(),
   new {tipo="IB_VERIFICAR_CUENTA",cajero="SERVER",idMensaje=Guid.NewGuid().ToString("N"),timestamp=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    bancoOrigen=ruta.GetProperty("bancoOrigen").GetString(),bancoDestino=ruta.GetProperty("bancoDestino").GetString(),numeroCuenta=ruta.GetProperty("numeroCuenta").GetString()});
 }
 private static bool Confirmacion(BancoInterbancario.Respuesta r,JsonElement solicitud)=>r.Codigo=="00"&&r.Datos.ValueKind==JsonValueKind.Object&&
  r.Datos.TryGetProperty("tokenTransferencia",out var token)&&token.GetString()==solicitud.GetProperty("tokenTransferencia").GetString()&&
  r.Datos.TryGetProperty("monto",out var monto)&&monto.GetDecimal()==solicitud.GetProperty("monto").GetDecimal()&&
  r.Datos.TryGetProperty("bancoDestino",out var banco)&&banco.GetString()==solicitud.GetProperty("bancoDestino").GetString()&&
  r.Datos.TryGetProperty("cuentaDestino",out var cuenta)&&cuenta.GetString()==solicitud.GetProperty("cuentaDestino").GetString();
 public async Task<Mensaje> Atender(Mensaje m)
 {
  try
  {
   if(string.IsNullOrWhiteSpace(m.Sesion))return Mensaje.Respuesta(m,"57","Inicie sesión para transferir.");
   if(m.Datos.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Mensaje inválido.");
   if(m.Tipo=="BANCOS_TRANSFERENCIA")return Respuesta(m,await Operar(m,"BANCOS",new {}));
   if(m.Tipo=="HISTORIAL_TRANSFERENCIAS")return Respuesta(m,await Operar(m,"HISTORIAL",new {}));
   if(m.Tipo=="ESTADO_TRANSFERENCIA")
   {
    string referencia=Texto(m.Datos,"idOriginal",64);
    var fila=await Operar(m,"ESTADO",new {idOriginal=referencia});
    if((string)fila["Accion"]=="CONCILIAR")
    {
     var ruta=Ruta(fila);
     try
     {
      var consulta=new {tipo="IB_ESTADO_TRANSFERENCIA",idMensaje=Guid.NewGuid().ToString("N"),bancoOrigen=ruta.GetProperty("bancoOrigen").GetString(),
       bancoDestino=ruta.GetProperty("bancoDestino").GetString(),tokenTransferencia=ruta.GetProperty("tokenTransferencia").GetString()};
      var r=await BancoInterbancario.Enviar(ruta.GetProperty("ip").GetString()!,ruta.GetProperty("puerto").GetInt32(),consulta);
      if(Confirmacion(r,ruta))
       await Operar(m with {IdSolicitud=referencia},"FINALIZAR",new {codigo="00",mensaje="Transferencia confirmada por el banco destino."});
     }
     catch(Exception e) when(ErrorRed(e)){DiagnosticoRed.Registrar("TRANSFERENCIA",m.Cajero,"CONCILIACION_PENDIENTE",new {tipo=e.GetType().Name});}
     fila=await Operar(m,"ESTADO",new {idOriginal=referencia});
    }
    return Respuesta(m,fila);
   }
   string banco=Texto(m.Datos,"bancoDestino",15).ToUpperInvariant(),cuenta=Texto(m.Datos,"cuentaDestino",20);
   if(!cuenta.All(char.IsAsciiDigit))throw new ArgumentException("La cuenta solo admite dígitos.");
   if(m.Tipo=="VERIFICAR_DESTINO")
   {
    var fila=await Operar(m,"DESTINO",new {bancoDestino=banco,cuentaDestino=cuenta});
    if((string)fila["Accion"]!="VERIFICAR")return Respuesta(m,fila);
    var r=await VerificarRemoto(Ruta(fila));
    if(r.Codigo!="00")return Mensaje.Respuesta(m,r.Codigo,"El banco destino no confirmó la cuenta.");
    if(r.Datos.GetProperty("codigoBanco").GetString()!=banco||r.Datos.GetProperty("numeroCuenta").GetString()!=cuenta||
     !r.Datos.GetProperty("existe").GetBoolean())return Mensaje.Respuesta(m,"14","La cuenta no pudo verificarse.");
    return Mensaje.Respuesta(m,"00","Destinatario verificado.",new {existe=true,codigoBanco=banco,nombreTitular=r.Datos.GetProperty("nombreTitular").GetString(),cuentaDestino="**** "+cuenta[^Math.Min(4,cuenta.Length)..]});
   }
   if(m.Tipo!="TRANSFERIR")return Mensaje.Respuesta(m,"30","Operación no reconocida.");
   string concepto=Texto(m.Datos,"concepto",200,true),nombre=Texto(m.Datos,"nombreDestino",120);
   if(!m.Datos.TryGetProperty("monto",out var valor)||!valor.TryGetDecimal(out var monto)||monto<=0||decimal.Round(monto,2)!=monto)
    throw new ArgumentException("Ingrese un monto positivo con hasta dos decimales.");
   var solicitud=await Operar(m,"PREPARAR",new {bancoDestino=banco,cuentaDestino=cuenta,monto,concepto,nombreDestino=nombre});
   if((string)solicitud["Accion"]!="VERIFICAR_ENVIO")return Respuesta(m,solicitud);
   try
   {
    var verificacion=await VerificarRemoto(Ruta(solicitud));
    if(verificacion.Codigo!="00"||!verificacion.Datos.GetProperty("existe").GetBoolean())
     return Respuesta(m,await Operar(m,"FINALIZAR",new {codigo="14",mensaje="El banco destino no confirmó la cuenta; no se cargó dinero."}));
    if(verificacion.Datos.GetProperty("codigoBanco").GetString()!=banco||verificacion.Datos.GetProperty("numeroCuenta").GetString()!=cuenta||
     verificacion.Datos.GetProperty("nombreTitular").GetString()!=nombre)
     return Respuesta(m,await Operar(m,"FINALIZAR",new {codigo="30",mensaje="El destinatario cambió; verifique de nuevo. No se cargó dinero."}));
   }
   catch(Exception e) when(ErrorRed(e))
   {return Respuesta(m,await Operar(m,"FINALIZAR",new {codigo="91",mensaje="No se pudo verificar el destino. No se envió ni cargó dinero."}));}
   solicitud=await Operar(m,"RESERVAR",new {});
   if((string)solicitud["Accion"]!="ENVIAR")return Respuesta(m,solicitud);
   string codigo="96",mensaje="Resultado pendiente. No repita la transferencia.";
   try
   {
    var ruta=Ruta(solicitud);
    var wire=JsonNode.Parse(ruta.GetRawText())!.AsObject();wire.Remove("ip");wire.Remove("puerto");
    var r=await BancoInterbancario.Enviar(ruta.GetProperty("ip").GetString()!,ruta.GetProperty("puerto").GetInt32(),wire);
    if(r.Codigo=="00"&&!Confirmacion(r,ruta))throw new IOException("Confirmación incompleta.");
    codigo=r.Codigo;mensaje=codigo=="00"?"Transferencia interbancaria aprobada.":"El banco respondió código "+codigo+".";
   }
   catch(Exception e) when(ErrorRed(e)){DiagnosticoRed.Registrar("TRANSFERENCIA",m.Cajero,"ENVIO_INCIERTO",new {tipo=e.GetType().Name});}
   return Respuesta(m,await Operar(m,"FINALIZAR",new {codigo,mensaje}));
  }
  catch(ArgumentException e){return Mensaje.Respuesta(m,"30",e.Message);}
  catch(SqlException e){DiagnosticoRed.Registrar("TRANSFERENCIA",m.Cajero,"SQL_ERROR",new {e.Number});return Mensaje.Respuesta(m,"96","No se pudo confirmar SQL. Consulte la referencia antes de repetir.");}
  catch(Exception e) when(ErrorRed(e)){return Mensaje.Respuesta(m,"96","El destino no respondió correctamente. Consulte el estado antes de repetir.");}
 }
}
