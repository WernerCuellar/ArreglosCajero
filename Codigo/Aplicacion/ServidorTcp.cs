using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CajeroRed.Infraestructura;
using CajeroRed.Protocolo;

namespace CajeroRed.Aplicacion;

public sealed class ServidorTcp(BaseDatos bd):IAsyncDisposable
{
 private sealed class Conexion(TcpClient cliente)
 {
  public TcpClient Cliente=cliente;
  public StreamWriter Escritor=new(cliente.GetStream(),new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
  public SemaphoreSlim Escritura=new(1,1),Turnos=new(4,4);
  public string? Codigo;
  public string Motivo="REMOTO_CERRO",UltimoTipo="SIN_REGISTRO";
  public long UltimaRecepcion=Stopwatch.GetTimestamp(),UltimoHeartbeat;
  public int HeartbeatActivo,Pendientes;
  public async Task Enviar(string linea,CancellationToken ct)
  {
   using var plazo=CancellationTokenSource.CreateLinkedTokenSource(ct);plazo.CancelAfter(TimeSpan.FromSeconds(8));
   await Escritura.WaitAsync(plazo.Token);
   try{await Escritor.WriteLineAsync(linea.AsMemory(),plazo.Token);}finally{Escritura.Release();}
  }
 }
 public BancoInterbancario Interbancario {get;}=new(bd);
 private readonly TransferenciasCajero transferencias=new(bd);
 private TcpListener? listener;
 private CancellationTokenSource? cancelacion;
 private readonly ConcurrentDictionary<string,Conexion> cajeros=new();
 private readonly ConcurrentDictionary<int,TcpClient> sockets=new();
 private readonly ConcurrentDictionary<int,Task> tareas=new(),trabajos=new();
 private int secuencia,puertoActual;
 public int Conectados=>cajeros.Count;
 public bool Activo=>listener!=null;
 public event Action<string>? Evento;
 public event Action<string,string>? TokenGenerado;
 private void Emitir(string texto){try{Evento?.Invoke(texto);}catch(InvalidOperationException){}}
 public async Task Iniciar(int puerto)
 {
  if(Activo)return;
  await bd.Ejecutar("UPDATE dbo.Cajero SET Conectado=0; UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE Fin IS NULL;");
  var nuevo=new TcpListener(IPAddress.Any,puerto);nuevo.Start(128);
  listener=nuevo;puertoActual=puerto;cancelacion=new();var ct=cancelacion.Token;
  _=Task.Run(()=>Aceptar(nuevo,ct));_=Task.Run(()=>Vigilar(ct));
  try{await Interbancario.IniciarDiscovery(puerto);}catch(Exception e) when(e is SocketException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
  {Emitir("TCP activo; revise la identidad del banco o UDP 5005.");}
  Emitir("Servidor escuchando en TCP "+puerto+".");
 }
 private void Registrar(ConcurrentDictionary<int,Task> lista,int id,Task tarea)
 {
  lista[id]=tarea;
  _=tarea.ContinueWith(t=>{
   lista.TryRemove(id,out _);
   if(t.IsFaulted)DiagnosticoRed.Registrar("SERVIDOR","","TAREA_ERROR",new {tipo=t.Exception!.GetBaseException().GetType().Name});
  },TaskScheduler.Default);
 }
 private async Task Aceptar(TcpListener servidor,CancellationToken ct)
 {
  while(!ct.IsCancellationRequested)
  {
   try
   {
    var tcp=await servidor.AcceptTcpClientAsync(ct);SocketRed.Configurar(tcp);
    if(sockets.Count>=128){tcp.Dispose();Emitir("Se alcanzó el máximo de 128 conexiones TCP.");continue;}
    int id=Interlocked.Increment(ref secuencia);sockets[id]=tcp;
    Registrar(tareas,id,Procesar(tcp,ct).ContinueWith(async t=>{
     sockets.TryRemove(id,out _);await t;
    },TaskScheduler.Default).Unwrap());
   }
   catch(OperationCanceledException){break;}
   catch(SocketException e)
   {
    if(ct.IsCancellationRequested)break;
    DiagnosticoRed.Registrar("SERVIDOR","","ACEPTAR_ERROR",new {codigo=e.SocketErrorCode.ToString()});
    await Task.Delay(500,ct);
   }
  }
 }
 private async Task Procesar(TcpClient tcp,CancellationToken ct)
 {
  var con=new Conexion(tcp);string remoto=tcp.Client.RemoteEndPoint?.ToString()??"";
  try
  {
   using var registro=CancellationTokenSource.CreateLinkedTokenSource(ct);registro.CancelAfter(TimeSpan.FromSeconds(15));
   var buffer=new byte[4096];var linea=new List<byte>();
   while(!ct.IsCancellationRequested)
   {
    int n=await tcp.GetStream().ReadAsync(buffer,con.Codigo==null?registro.Token:ct);if(n==0)break;
    for(int i=0;i<n;i++)
    {
     if(buffer[i]!=10){if(linea.Count>=32768)throw new IOException("Mensaje demasiado grande.");linea.Add(buffer[i]);continue;}
     string crudo=new UTF8Encoding(false,true).GetString(linea.ToArray()).TrimEnd('\r');linea.Clear();
     if(con.Codigo==null&&BancoInterbancario.EsInterbancario(crudo))
     {con.Motivo="INTERBANCARIO_FINALIZADO";await con.Enviar(await Interbancario.Atender(crudo,((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString()),ct);return;}
     var m=Mensaje.Leer(crudo);
     if(string.IsNullOrWhiteSpace(m.IdSolicitud)||m.IdSolicitud.Length>64||m.Cajero==null||m.Cajero.Length>24)throw new IOException("Cabecera inválida.");
     if(con.Codigo==null&&m.Tipo=="NETGEAR_INFO")
     {
      await con.Enviar(Mensaje.Respuesta(m,"00","Servidor NETGEAR disponible.",new {nombreBanco="NETGEAR",protocoloCajero="NETGEAR-ATM/2.2",puerto=puertoActual}).Serializar(),ct);return;
     }
     if(con.Codigo==null)
     {
      if(m.Tipo!="REGISTRAR_CAJERO")throw new IOException("Debe registrarse primero.");
      if(!cajeros.TryAdd(m.Cajero,con)){con.Motivo="CODIGO_DUPLICADO";await con.Enviar(Mensaje.Respuesta(m,"94","Código de cajero ya conectado. Use un código distinto en cada PC.").Serializar(),ct);return;}
      con.Codigo=m.Cajero;con.UltimaRecepcion=Stopwatch.GetTimestamp();con.UltimoTipo=m.Tipo;
      var r=await bd.Atender(m with {Datos=JsonSerializer.SerializeToElement(new {equipo=m.Datos.GetProperty("equipo").GetString(),version=m.Datos.GetProperty("version").GetString(),ip=((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString()})});
      await con.Enviar(r.Serializar(),ct);if(r.Codigo!="00"){con.Motivo="REGISTRO_RECHAZADO_"+r.Codigo;return;}continue;
     }
     if(m.Cajero!=con.Codigo||m.Tipo=="REGISTRAR_CAJERO")
     {await con.Enviar(Mensaje.Respuesta(m,"91","Identidad del cajero inválida.").Serializar(),ct);continue;}
     con.UltimaRecepcion=Stopwatch.GetTimestamp();con.UltimoTipo=m.Tipo;
     if(m.Tipo=="HEARTBEAT")con.UltimoHeartbeat=con.UltimaRecepcion;
     if(Interlocked.Increment(ref con.Pendientes)>8)
     {
      Interlocked.Decrement(ref con.Pendientes);
      await con.Enviar(Mensaje.Respuesta(m,"96","Servidor ocupado; consulte el estado antes de repetir un movimiento.").Serializar(),ct);continue;
     }
     int trabajo=Interlocked.Increment(ref secuencia);
     Registrar(trabajos,trabajo,Atender(con,m,ct));
    }
   }
  }
  catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or JsonException or KeyNotFoundException or ObjectDisposedException or DecoderFallbackException or Microsoft.Data.SqlClient.SqlException)
  {
   if(con.Motivo=="REMOTO_CERRO")con.Motivo=ct.IsCancellationRequested?"PARADA_SERVIDOR":
    e is OperationCanceledException&&con.Codigo==null?"REGISTRO_TIMEOUT":
    ((e as SocketException)??(e.InnerException as SocketException)) is SocketException se?"SOCKET_"+se.SocketErrorCode:e.GetType().Name;
  }
  finally
  {
   tcp.Dispose();
   DiagnosticoRed.Registrar("SERVIDOR",con.Codigo??"SIN_REGISTRO","CIERRE",new {remoto,motivo=con.Motivo,ultimoTipo=con.UltimoTipo,
    msSinRecepcion=(long)Stopwatch.GetElapsedTime(con.UltimaRecepcion).TotalMilliseconds});
   if(con.Codigo!=null&&cajeros.TryGetValue(con.Codigo,out var actual)&&ReferenceEquals(actual,con))
   {
    try
    {
     await bd.ConsultarMonitoreo("EXEC dbo.sp_Desconectar @c",("@c",con.Codigo));

    }catch(Exception){Emitir("No se pudo persistir la desconexión en SQL.");}
    cajeros.TryRemove(con.Codigo,out _);Emitir(con.Codigo+" desconectado.");
    try{await bd.Auditoria(con.Codigo,"CONEXION_MOTIVO",con.Motivo+"; último tipo "+con.UltimoTipo);}catch(Exception){}
   }
  }
 }
 private async Task Atender(Conexion con,Mensaje m,CancellationToken ct)
 {
  bool turno=false,hb=m.Tipo=="HEARTBEAT",hbPropio=false;var reloj=Stopwatch.StartNew();
  try
  {
   if(hb)
   {
    hbPropio=Interlocked.CompareExchange(ref con.HeartbeatActivo,1,0)==0;
    if(!hbPropio){await con.Enviar(Mensaje.Respuesta(m,"96","Monitoreo en curso.").Serializar(),ct);return;}
   }
   else{await con.Turnos.WaitAsync(ct);turno=true;}
   Mensaje r;
   try{r=TransferenciasCajero.Atiende(m.Tipo)?await transferencias.Atender(m):await bd.Atender(m);}
   catch(Exception e) when(e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
   {
    DiagnosticoRed.Registrar("SERVIDOR",m.Cajero,"SQL_ERROR",new {tipo=m.Tipo,numero=(e as Microsoft.Data.SqlClient.SqlException)?.Number,ms=reloj.ElapsedMilliseconds});
    r=Mensaje.Respuesta(m,"96","SQL no confirmó la operación. Consulte su referencia antes de repetir.");
   }
   if(reloj.ElapsedMilliseconds>=2000)DiagnosticoRed.Registrar("SERVIDOR",m.Cajero,"OPERACION_LENTA",new {tipo=m.Tipo,ms=reloj.ElapsedMilliseconds});
   if(m.Tipo=="LOGIN"&&r.Codigo=="00")
   {
    TokenGenerado?.Invoke(m.Cajero,r.Datos.GetProperty("tokenServidor").GetString()!);
    r=r with {Datos=JsonSerializer.SerializeToElement(new {requiereToken=true})};
   }
   if(m.Tipo=="RETIRO"&&r.Codigo=="92")
   {
    var inv=r.Datos.GetProperty("inventario").EnumerateArray().ToDictionary(x=>x.GetProperty("denominacion").GetInt32(),x=>x.GetProperty("cantidad").GetInt32());
    int monto=m.Datos.TryGetProperty("monto",out var valor)&&valor.TryGetInt32(out int total)?total:0;
    r=r with {Datos=JsonSerializer.SerializeToElement(new {alternativa=Billetes.Alternativa(monto,inv)})};
   }
   await con.Enviar(r.Serializar(),ct);if(!hb)Emitir($"{m.Cajero} · {m.Tipo} · {r.Codigo}");
  }
  catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
  {con.Motivo=ct.IsCancellationRequested?"PARADA_SERVIDOR":"ESCRITURA_"+(((e as SocketException)??(e.InnerException as SocketException))?.SocketErrorCode.ToString()??e.GetType().Name);con.Cliente.Dispose();}
  finally
  {
   if(turno)con.Turnos.Release();if(hbPropio)Interlocked.Exchange(ref con.HeartbeatActivo,0);Interlocked.Decrement(ref con.Pendientes);
  }
 }
 private async Task Vigilar(CancellationToken ct)
 {
  string anterior="";
  try
  {
   while(!ct.IsCancellationRequested)
   {
    await Task.Delay(1000,ct);
    foreach(var c in cajeros.Values)
    {
     long ultimo=c.UltimoHeartbeat==0?c.UltimaRecepcion:c.UltimoHeartbeat;
     if(Stopwatch.GetElapsedTime(ultimo)>TimeSpan.FromSeconds(30)){c.Motivo="HEARTBEAT_VENCIDO_30S";c.Cliente.Dispose();}
    }
    try{await bd.ConsultarMonitoreo("UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE Fin IS NULL AND UltimaActividad<DATEADD(second,-120,SYSDATETIME())");}
    catch(Exception){Emitir("Conexión TCP activa; SQL no responde al monitoreo.");}
    try
    {
    string red=string.Join(" | ",NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
     .Select(n=>n.Name+": "+n.OperationalStatus+" "+string.Join(",",n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Select(a=>a.Address))));
    if(red!=anterior)
    {
     anterior=red;DiagnosticoRed.Registrar("SERVIDOR","","CAMBIO_RED",new {red});
     if(red.Contains("169.254."))Emitir("Hay un adaptador sin IPv4 del router (169.254.x.x). Revise DHCP/cable; el listener sigue activo.");
    }
    }catch(NetworkInformationException e){DiagnosticoRed.Registrar("SERVIDOR","","CONSULTA_RED_ERROR",new {e.ErrorCode});}
   }
  }
  catch(OperationCanceledException){}
 }
 public async Task Comando(string cajero,string comando,string texto="")
 {
  if(cajeros.TryGetValue(cajero,out var c))await c.Enviar(Mensaje.Solicitud("COMANDO",cajero,new {comando,texto}).Serializar(),CancellationToken.None);
 }
 public async ValueTask DisposeAsync()
 {
  cancelacion?.Cancel();listener?.Stop();listener=null;await Interbancario.DisposeAsync();
  foreach(var c in cajeros.Values)c.Motivo="PARADA_SERVIDOR";foreach(var tcp in sockets.Values)tcp.Dispose();
  try{await Task.WhenAll(tareas.Values.Concat(trabajos.Values)).WaitAsync(TimeSpan.FromSeconds(20));}catch(Exception){}
  cancelacion?.Dispose();cancelacion=null;
 }
}
