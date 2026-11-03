using System.Data;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CajeroRed.Infraestructura;
using CajeroRed.Protocolo;

namespace CajeroRed.Aplicacion;

// Adaptador del protocolo PROTOCOLO_CONEXION_BANCOS_LLM.md.
// Las transacciones financieras se confirman en SQL; nunca se mantiene una
// transacción SQL abierta mientras se espera a otro banco por la red.
public sealed class BancoInterbancario(BaseDatos bd) : IAsyncDisposable
{
 private CancellationTokenSource? fin;
 private UdpClient? udp;
 private Task? receptor;
 public bool DiscoveryActivo=>udp!=null;
 public static readonly JsonSerializerOptions Json=new(Mensaje.Json){PropertyNameCaseInsensitive=true};
 public sealed record Peticion
 {
  public string Tipo {get;init;}="";
  public string IdMensaje {get;init;}="";
  public string BancoOrigen {get;init;}="";
  public string BancoDestino {get;init;}="";
  public string NumeroCuenta {get;init;}="";
  public string CuentaOrigen {get;init;}="";
  public string CuentaDestino {get;init;}="";
  public string NombreOrigen {get;init;}="";
  public string NombreDestino {get;init;}="";
  public string TokenTransferencia {get;init;}="";
  public string Concepto {get;init;}="";
  public decimal Monto {get;init;}
 }
 public sealed record Respuesta(string Tipo,string IdMensaje,string Codigo,string Mensaje,string Timestamp,JsonElement Datos);
 private async Task<DataRow> Operar(string tipo,object datos,int? administrador=null,string ip="")
 {
  var t=await bd.Consultar("EXEC dbo.sp_IB_Operacion @Tipo=@t,@Json=@j,@IP=@ip,@Administrador=@a",
   ("@t",tipo),("@j",JsonSerializer.Serialize(datos,Json)),("@ip",ip),("@a",administrador));
  return t.Rows[0];
 }
 private static Respuesta Convertir(DataRow r,string id)=>new("RESP_OK",id,(string)r["Codigo"],(string)r["Mensaje"],(string)r["Fecha"],
  JsonSerializer.Deserialize<JsonElement>((string)r["Datos"]));
 private static void Exigir(DataRow r){if((string)r["Codigo"]!="00")throw new InvalidOperationException((string)r["Mensaje"]);}
 public static bool EsInterbancario(string linea)
 {
  using var d=JsonDocument.Parse(linea);
  return d.RootElement.ValueKind==JsonValueKind.Object && d.RootElement.EnumerateObject()
   .Any(p=>p.Name.Equals("tipo",StringComparison.OrdinalIgnoreCase)&&p.Value.ValueKind==JsonValueKind.String&&p.Value.GetString()!.StartsWith("IB_",StringComparison.OrdinalIgnoreCase));
 }
 public async Task<string> Atender(string linea,string ip)
 {
  Peticion? m=null;
  try
  {
   m=JsonSerializer.Deserialize<Peticion>(linea,Json)??throw new JsonException();
   if(m.Tipo is null||m.IdMensaje is null||m.BancoOrigen is null||m.BancoDestino is null||m.CuentaOrigen is null||m.CuentaDestino is null||m.NumeroCuenta is null||m.NombreOrigen is null||m.NombreDestino is null||m.Concepto is null||m.TokenTransferencia is null)throw new ArgumentException("Campos nulos inválidos.");
   if(string.IsNullOrWhiteSpace(m.IdMensaje)||m.IdMensaje.Length>64)throw new ArgumentException("idMensaje requerido (máximo 64 caracteres).");
   if(m.BancoOrigen.Length>15||m.BancoDestino.Length>15||m.NumeroCuenta.Length>20||m.CuentaDestino.Length>20||m.CuentaOrigen.Length>20||
    m.NombreOrigen.Length>120||m.NombreDestino.Length>120||m.Concepto.Length>200||m.TokenTransferencia.Length>40)
    throw new ArgumentException("Campos fuera del tamaño permitido.");
   string tipo=m.Tipo.ToUpperInvariant();
   if(tipo=="IB_TRANSFERIR"&&(m.Monto<=0||decimal.Round(m.Monto,2)!=m.Monto||!Regex.IsMatch(m.TokenTransferencia,@"^TRF-\d{8}-\d{6}$")||
    !Regex.IsMatch(m.CuentaOrigen,@"^[0-9]{1,20}$")||!Regex.IsMatch(m.CuentaDestino,@"^[0-9]{1,20}$")))
    throw new ArgumentException("Monto, token o cuentas inválidos.");
   object datos=m;
   // numeroCuenta y cuentaDestino son alias, no permitir que el valor vacío
   // del otro alias oculte la cuenta recibida.
   if(tipo=="IB_VERIFICAR_CUENTA")
    datos=new {m.BancoOrigen,m.BancoDestino,cuentaDestino=string.IsNullOrEmpty(m.CuentaDestino)?m.NumeroCuenta:m.CuentaDestino};
   return JsonSerializer.Serialize(Convertir(await Operar(tipo,datos,ip:ip),m.IdMensaje),Json);
  }
  catch(Exception e) when(e is JsonException or ArgumentException or Microsoft.Data.SqlClient.SqlException)
  {
   DiagnosticoRed.Registrar("INTERBANCARIO",m?.BancoOrigen??"DESCONOCIDO","ERROR",new {tipo=e.GetType().Name,sqlNumero=(e as Microsoft.Data.SqlClient.SqlException)?.Number});
   return JsonSerializer.Serialize(new Respuesta("RESP_OK",m?.IdMensaje??"",e is Microsoft.Data.SqlClient.SqlException?"96":"30",
    e is Microsoft.Data.SqlClient.SqlException?"Operación sin confirmar. Consulte el estado; no reintente con otro token.":"Mensaje interbancario inválido.",
    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),JsonSerializer.SerializeToElement(new {})),Json);
  }
 }
 public async Task Configurar(int administrador,string codigo,string nombre,string ip,int puerto,bool propio,bool confiable,decimal limite)
 {
  if(codigo.Length is <1 or >15||nombre.Length is <1 or >100||nombre.Contains('|')||!IPAddress.TryParse(ip,out _)||puerto is <1 or >65535||limite<=0||decimal.Round(limite,2)!=limite)
   throw new ArgumentException("Revise código, nombre, IP, puerto y límite.");
  Exigir(await Operar("CONFIGURAR",new {codigoBanco=codigo.ToUpperInvariant(),nombreBanco=nombre,ip,puerto,esPropio=propio,confiable,limiteDiario=limite},administrador));
 }
 public async Task AsignarCuenta(int administrador,int usuarioId,string numeroCuenta)=>Exigir(await Operar("ASIGNAR_CUENTA",new {usuarioId,numeroCuenta},administrador));
 public async Task<DataTable> Bancos()=>await bd.Consultar("SELECT CodigoBanco,NombreBanco,IP,Puerto,EsPropio,Confiable,Activo,LimiteDiario,CASE WHEN FechaUltimoReinicio=CAST(SYSDATETIME() AS date) THEN TransferidoHoy ELSE 0 END TransferidoHoy,LimiteDiario-CASE WHEN FechaUltimoReinicio=CAST(SYSDATETIME() AS date) THEN TransferidoHoy ELSE 0 END CupoDisponibleHoy FROM dbo.BancoRed ORDER BY EsPropio DESC,CodigoBanco");
 private async Task<DataRow> Propio()
 {
  var t=await Bancos();return t.AsEnumerable().SingleOrDefault(r=>(bool)r["EsPropio"])??throw new InvalidOperationException("Configure el banco propio.");
 }
 private async Task<DataRow> Destino(string banco)
 {
  var t=await Bancos();return t.AsEnumerable().SingleOrDefault(r=>(string)r["CodigoBanco"]==banco&&!(bool)r["EsPropio"]&&(bool)r["Confiable"]&&(bool)r["Activo"])
   ??throw new ArgumentException("Registre y autorice el banco destino antes de transferir.");
 }
 public static async Task<Respuesta> Enviar(string ip,int puerto,object mensaje,CancellationToken ct=default)
 {
  using var limite=CancellationTokenSource.CreateLinkedTokenSource(ct);limite.CancelAfter(TimeSpan.FromSeconds(10));
  using var tcp=new TcpClient(){NoDelay=true};await tcp.ConnectAsync(ip,puerto,limite.Token);
  using var writer=new StreamWriter(tcp.GetStream(),new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
  string json=JsonSerializer.Serialize(mensaje,Json);
  if(Encoding.UTF8.GetByteCount(json)>32768)throw new ArgumentException("Mensaje demasiado grande.");
  await writer.WriteLineAsync(json.AsMemory(),limite.Token);
  // Una respuesta por conexión; lectura limitada también frente a pares defectuosos.
  var bytes=new List<byte>();var buffer=new byte[1];
  while(true)
  {
   int n=await tcp.GetStream().ReadAsync(buffer,limite.Token);
   if(n==0)throw new IOException("El banco cerró sin confirmar.");
   if(buffer[0]==10)break;
   if(bytes.Count>=32768)throw new IOException("Respuesta demasiado grande.");
   bytes.Add(buffer[0]);
  }
  var r=JsonSerializer.Deserialize<Respuesta>(new UTF8Encoding(false,true).GetString(bytes.ToArray()),Json)??throw new JsonException();
  using var original=JsonDocument.Parse(json);
  string id=original.RootElement.GetProperty("idMensaje").GetString()!;
  if(r.IdMensaje!=id||r.Tipo!="RESP_OK"||string.IsNullOrEmpty(r.Codigo)||r.Codigo.Length!=2)throw new IOException("Respuesta sin correlación válida.");
  return r;
 }
 public async Task<Respuesta> Ping(string ip,int puerto)
 {
  var p=await Propio();
  return await Enviar(ip,puerto,new {tipo="IB_PING",cajero="SERVER",idMensaje=Guid.NewGuid().ToString("N"),timestamp=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
   bancoOrigen=p["CodigoBanco"],nombreBancoOrigen=p["NombreBanco"]});
 }
 public async Task<Respuesta> Verificar(string banco,string cuenta)
 {
  var d=await Destino(banco);var p=await Propio();
  return await Enviar((string)d["IP"],(int)d["Puerto"],new {tipo="IB_VERIFICAR_CUENTA",cajero="SERVER",idMensaje=Guid.NewGuid().ToString("N"),
   bancoOrigen=p["CodigoBanco"],bancoDestino=banco,numeroCuenta=cuenta,timestamp=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")});
 }
 public async Task<Respuesta> Transferir(int administrador,int usuarioId,string banco,string cuenta,decimal monto,string concepto)
 {
  if(!Regex.IsMatch(cuenta,@"^[0-9]{1,20}$")||monto<=0||decimal.Round(monto,2)!=monto||concepto.Length>200)throw new ArgumentException("Cuenta, monto o concepto inválidos.");
  var destino=await Destino(banco);var verificada=await Verificar(banco,cuenta);
  if(verificada.Codigo!="00")return verificada;
  string nombre=verificada.Datos.GetProperty("nombreTitular").GetString()??"";
  // El día oficial y el cargo se fijan en SQL, no en el cajero.
  var hoy=await bd.Consultar("SELECT CONVERT(char(8),SYSDATETIME(),112) Dia");
  string token="TRF-"+hoy.Rows[0][0]+"-"+RandomNumberGenerator.GetInt32(1000000).ToString("D6",CultureInfo.InvariantCulture);
  var reserva=await Operar("RESERVAR",new {usuarioId,bancoDestino=banco,cuentaDestino=cuenta,nombreDestino=nombre,monto,concepto,tokenTransferencia=token},administrador);
  if((string)reserva["Codigo"]!="00")return Convertir(reserva,"");
  var solicitud=JsonSerializer.Deserialize<JsonElement>((string)reserva["Datos"]);
  Respuesta respuesta;
  try
  {
   respuesta=await Enviar((string)destino["IP"],(int)destino["Puerto"],solicitud);
   if(respuesta.Codigo=="00"&&(!respuesta.Datos.TryGetProperty("tokenTransferencia",out var tr)||tr.GetString()!=token||
      !respuesta.Datos.TryGetProperty("monto",out var mr)||mr.GetDecimal()!=monto||
      !respuesta.Datos.TryGetProperty("bancoDestino",out var br)||br.GetString()!=banco||
      !respuesta.Datos.TryGetProperty("cuentaDestino",out var cr)||cr.GetString()!=cuenta))
    throw new IOException("Confirmación financiera incompleta.");
  }
  catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or JsonException or InvalidOperationException or FormatException)
  {
   DiagnosticoRed.Registrar("INTERBANCARIO",banco,"RESPUESTA_INCIERTA",new {token,tipo=e.GetType().Name});
   respuesta=new("RESP_OK","","96","Transferencia pendiente de conciliación. Conserve el token y no genere otra para reemplazarla.",
    (string)reserva["Fecha"],JsonSerializer.SerializeToElement(new {tokenTransferencia=token,estado="INCIERTA"}));
  }
  await Operar("FINALIZAR",new {tokenTransferencia=token,codigo=respuesta.Codigo},administrador);
  return respuesta;
 }
 public async Task<Respuesta> Conciliar(int administrador,string token)
 {
  var t=await bd.Consultar("SELECT * FROM dbo.TransferenciaBancaria WHERE TokenTransferencia=@t AND Direccion='SALIENTE'",("@t",token));
  if(t.Rows.Count!=1)throw new ArgumentException("Seleccione una transferencia saliente.");
  var tr=t.Rows[0];var d=await Destino((string)tr["BancoDestino"]);
  var r=await Enviar((string)d["IP"],(int)d["Puerto"],new {tipo="IB_ESTADO_TRANSFERENCIA",idMensaje=Guid.NewGuid().ToString("N"),
   bancoOrigen=tr["BancoOrigen"],bancoDestino=tr["BancoDestino"],tokenTransferencia=token});
  if(r.Codigo=="00"&&r.Datos.TryGetProperty("tokenTransferencia",out var tok)&&tok.GetString()==token&&
   r.Datos.TryGetProperty("monto",out var m)&&m.GetDecimal()==(decimal)tr["Monto"]&&
   r.Datos.TryGetProperty("bancoDestino",out var br)&&br.GetString()==(string)tr["BancoDestino"]&&
   r.Datos.TryGetProperty("cuentaDestino",out var cr)&&cr.GetString()==(string)tr["CuentaDestino"])
   await Operar("FINALIZAR",new {tokenTransferencia=token,codigo="00"},administrador);
  // No encontrarla o no soportar la extensión no demuestra rechazo.
  return r;
 }
 public async Task IniciarDiscovery(int puerto,int puertoUdp=5005)
 {
  if(udp!=null)return;
  await Propio();
  var nuevo=new UdpClient(new IPEndPoint(IPAddress.Any,puertoUdp));udp=nuevo;fin=new();
  var ct=fin.Token;
  receptor=Task.Run(async()=>
  {
   while(!ct.IsCancellationRequested)
   {
    try
    {
     var paquete=await nuevo.ReceiveAsync(ct);
     if(paquete.Buffer.Length>1024)continue;
     string[] campos=Encoding.UTF8.GetString(paquete.Buffer).Split('|');
     if(campos.Length!=5||campos[0]!="DISCOVER_BANCO_ATM")continue;
     var ping=await Operar("IB_PING",new {});
     if((string)ping["Codigo"]!="00")continue;
     var datos=JsonSerializer.Deserialize<JsonElement>((string)ping["Datos"]);
     var p=await Propio();
     if(campos[1]==(string)p["CodigoBanco"])continue;
     string ip=DireccionLocal(paquete.RemoteEndPoint.Address);
     string texto=string.Join("|","HERE_BANCO_ATM",p["CodigoBanco"],p["NombreBanco"],ip,puerto,
      Convert.ToDecimal(p["LimiteDiario"]).ToString("F2",CultureInfo.InvariantCulture),
      (Convert.ToDecimal(p["LimiteDiario"])-datos.GetProperty("cupoDisponibleHoy").GetDecimal()).ToString("F2",CultureInfo.InvariantCulture));
     await nuevo.SendAsync(Encoding.UTF8.GetBytes(texto),paquete.RemoteEndPoint,ct);
    }
    catch(OperationCanceledException){break;}
    catch(ObjectDisposedException){break;}
    catch(Exception e) when(e is SocketException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
    {DiagnosticoRed.Registrar("DISCOVERY","","ERROR",new {tipo=e.GetType().Name});}
   }
  });
 }
 private static string DireccionLocal(IPAddress destino)
 {
  using var ruta=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
  ruta.Connect(new IPEndPoint(destino,5005));return ((IPEndPoint)ruta.LocalEndPoint!).Address.ToString();
 }
 public async Task<DataTable> Escanear()
 {
  var p=await Propio();
  var resultado=new DataTable();resultado.Columns.Add("CodigoBanco");resultado.Columns.Add("NombreBanco");resultado.Columns.Add("IP");resultado.Columns.Add("Puerto",typeof(int));resultado.Columns.Add("CupoDisponibleHoy",typeof(decimal));
  using var explorador=new UdpClient(new IPEndPoint(IPAddress.Any,0)){EnableBroadcast=true};
  var objetivos=new HashSet<IPAddress>{IPAddress.Broadcast};
  foreach(var n in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up))
   foreach(var a in n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address)))
   {
    var ip=a.Address.GetAddressBytes();var mask=a.IPv4Mask.GetAddressBytes();
    objetivos.Add(new IPAddress(ip.Zip(mask,(b,m)=>(byte)(b|~m)).ToArray()));
   }
  foreach(var objetivo in objetivos)
  {
   try
   {
    string local=DireccionLocal(objetivo);
    string mensaje=$"DISCOVER_BANCO_ATM|{p["CodigoBanco"]}|{p["NombreBanco"]}|{local}|{p["Puerto"]}";
    await explorador.SendAsync(Encoding.UTF8.GetBytes(mensaje),new IPEndPoint(objetivo,5005));
   }catch(SocketException){}
  }
  using var limite=new CancellationTokenSource(TimeSpan.FromSeconds(3));var vistos=new HashSet<string>();
  while(!limite.IsCancellationRequested)
  {
   try
   {
    var respuesta=await explorador.ReceiveAsync(limite.Token);
    if(respuesta.Buffer.Length>1024)continue;
    string[] c=Encoding.UTF8.GetString(respuesta.Buffer).Split('|');
    if(c.Length!=7||c[0]!="HERE_BANCO_ATM"||c[1]==(string)p["CodigoBanco"]||!int.TryParse(c[4],out int puerto)||puerto is <1 or >65535||
     !decimal.TryParse(c[5],NumberStyles.Number,CultureInfo.InvariantCulture,out decimal max)||
     !decimal.TryParse(c[6],NumberStyles.Number,CultureInfo.InvariantCulture,out decimal usado))continue;
    string ip=respuesta.RemoteEndPoint.Address.ToString();
    if(vistos.Add(c[1]+"|"+ip))resultado.Rows.Add(c[1],c[2],ip,puerto,Math.Max(0,max-usado));
   }catch(OperationCanceledException){break;}
  }
  // En redes sin broadcast (por ejemplo algunas VPN), los bancos ya registrados
  // se prueban por TCP. No se barre indiscriminadamente una /8 de Radmin.
  foreach(DataRow b in (await Bancos()).Rows)
  {
   if((bool)b["EsPropio"]||!(bool)b["Activo"])continue;
   try
   {
    var ping=await Ping((string)b["IP"],(int)b["Puerto"]);
    if(ping.Codigo=="00"&&vistos.Add((string)b["CodigoBanco"]+"|"+b["IP"]))
     resultado.Rows.Add(ping.Datos.GetProperty("codigoBanco").GetString(),ping.Datos.GetProperty("nombreBanco").GetString(),b["IP"],b["Puerto"],ping.Datos.GetProperty("cupoDisponibleHoy").GetDecimal());
   }catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or JsonException){}
  }
  return resultado;
 }
 public async ValueTask DisposeAsync()
 {
  fin?.Cancel();udp?.Dispose();udp=null;
  if(receptor!=null)try{await receptor;}catch(OperationCanceledException){}
  receptor=null;fin?.Dispose();fin=null;
 }
}
