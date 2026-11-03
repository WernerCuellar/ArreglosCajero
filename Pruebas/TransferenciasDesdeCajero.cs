using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using CajeroRed.Aplicacion;
using CajeroRed.Infraestructura;
using CajeroRed.Protocolo;
using Microsoft.Data.SqlClient;

namespace CajeroRed.Pruebas;
internal static partial class Program
{
 private static async Task TransferenciasDesdeCajero()
 {
  string sql=Environment.GetEnvironmentVariable("CAJERO_SQL_SERVIDOR")??".";
  string[] nombres=Enumerable.Range(0,2).Select(_=>"BD_CajeroRed_Pruebas_"+Guid.NewGuid().ToString("N")[..12]).ToArray();
  string Cadena(string db)=>$"Server={sql};Database={db};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
  try
  {
   foreach(string nombre in nombres)
   {
    await using var c=new SqlConnection(Cadena("master"));await c.OpenAsync();
    foreach(string archivo in Directory.GetFiles(Path.Combine(raiz,"BaseDeDatos","Scripts"),"*.sql").Order())
     foreach(string lote in Regex.Split(File.ReadAllText(archivo).Replace("BD_CajeroRed_Fase2",nombre),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase).Where(s=>!string.IsNullOrWhiteSpace(s)))
     {await using var cmd=new SqlCommand(lote,c){CommandTimeout=30};await cmd.ExecuteNonQueryAsync();}
   }
   var a=new BaseDatos(Cadena(nombres[0]));var b=new BaseDatos(Cadena(nombres[1]));
   var pa=new BaseDatos(Cadena(nombres[0]),false);var pb=new BaseDatos(Cadena(nombres[1]),false);
   var adminA=new Administracion(a);var adminB=new Administracion(b);
   Verificar(await adminA.Login("admin","GrupoCajero#2026")&&await adminB.Login("admin","GrupoCajero#2026"),"dos bancos SQL independientes");
   int PuertoLibre(){var t=new TcpListener(IPAddress.Loopback,0);t.Start();int p=((IPEndPoint)t.LocalEndpoint).Port;t.Stop();return p;}
   int puertoA=PuertoLibre(),puertoB=PuertoLibre();
   await using var sa=new ServidorTcp(a);await using var sb=new ServidorTcp(b);
   await sa.Interbancario.Configurar(adminA.Id!.Value,"BANC90","NETGEAR","0.0.0.0",puertoA,true,true,25000);
   await sb.Interbancario.Configurar(adminB.Id!.Value,"BANC91","BANCO DESTINO","0.0.0.0",puertoB,true,true,25000);
   await sa.Interbancario.Configurar(adminA.Id.Value,"BANC91","BANCO DESTINO","127.0.0.1",puertoB,false,true,25000);
   await sb.Interbancario.Configurar(adminB.Id.Value,"BANC90","NETGEAR","127.0.0.1",puertoA,false,true,25000);
   await sa.Interbancario.AsignarCuenta(adminA.Id.Value,2,"20000002");await sb.Interbancario.AsignarCuenta(adminB.Id.Value,8,"29000008");
   await adminA.Accion("INICIALIZAR",new {cajero="ATM-01",desglose=new Dictionary<int,int>{{100,100}}});
   await adminA.Accion("INICIALIZAR",new {cajero="ATM-02",desglose=new Dictionary<int,int>{{100,100}}});
   await adminB.Accion("INICIALIZAR",new {cajero="ATM-01",desglose=new Dictionary<int,int>{{100,100}}});
   var tokens=new System.Collections.Concurrent.ConcurrentDictionary<string,string>();
   sa.TokenGenerado+=(c,t)=>tokens["A"+c]=t;sb.TokenGenerado+=(c,t)=>tokens["B"+c]=t;
   await sa.Iniciar(puertoA);await sb.Iniciar(puertoB);
   await using var c1=new ClienteTcp(){Direccion="127.0.0.1",Puerto=puertoA,Cajero="ATM-01"};
   await using var c2=new ClienteTcp(){Direccion="127.0.0.1",Puerto=puertoA,Cajero="ATM-02"};
   await using var receptor=new ClienteTcp(){Direccion="127.0.0.1",Puerto=puertoB,Cajero="ATM-01"};
   c1.Iniciar();c2.Iniciar();receptor.Iniciar();await Esperar(()=>c1.Conectado&&c2.Conectado&&receptor.Conectado);
   async Task Login(ClienteTcp c,int u,string banco)
   {
    string tarjeta=$"400000000000{u:0000}";
    if((await c.Enviar("LOGIN",new {tarjeta,pin="1234"})).Codigo!="00")throw new Exception("Login de prueba falló.");
    var r=await c.Enviar("VALIDAR_TOKEN",new {tarjeta,token=tokens[banco+c.Cajero]});
    if(r.Codigo!="00")throw new Exception("Token de prueba falló.");
    c.EstablecerSesion(r.Datos.GetProperty("sesion").GetString()!,r.Datos.GetProperty("usuarioId").GetInt32());
   }
   await Login(c1,1,"A");await Login(c2,2,"A");
   async Task<decimal> Saldo(BaseDatos d,int u)=>Convert.ToDecimal((await d.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=@u",("@u",u))).Rows[0][0]);
   async Task<Mensaje> VerificarCuenta(ClienteTcp c,string banco,string cuenta)=>await c.Enviar("VERIFICAR_DESTINO",new {bancoDestino=banco,cuentaDestino=cuenta});
   object Datos(string banco,string cuenta,string nombre,decimal monto,string concepto="Prueba")=>new {bancoDestino=banco,cuentaDestino=cuenta,nombreDestino=nombre,monto,concepto,usuarioId=999};
   var bancos=await c1.Enviar("BANCOS_TRANSFERENCIA");
   Verificar(bancos.Codigo=="00"&&bancos.Datos.GetArrayLength()==2,"cajero recibe banco propio y banco externo autorizado sin direcciones SQL");
   var destino=await VerificarCuenta(c1,"BANC90","20000002");string nombre2=destino.Datos.GetProperty("nombreTitular").GetString()!;
   Verificar(destino.Codigo=="00","destinatario interno verificado desde el cajero");
   decimal origen=await Saldo(a,1),otro=await Saldo(a,2),efectivo=Convert.ToDecimal((await a.Consultar("SELECT SUM(Denominacion*Cantidad) FROM dbo.InventarioCajero")).Rows[0][0]);
   string id=Guid.NewGuid().ToString("N");var datos=Datos("BANC90","20000002",nombre2,25.50m);
   var interna=await c1.Enviar("TRANSFERIR",datos,id);
   Verificar(interna.Codigo=="00"&&interna.Datos.GetProperty("tokenTransferencia").GetString()!.StartsWith("TRF-"),"transferencia interna con token y comprobante");
   Verificar(await Saldo(a,1)==origen-25.50m&&await Saldo(a,2)==otro+25.50m,"débito y crédito internos atómicos; usuarioId enviado por el cliente se ignora");
   Verificar((await c1.Enviar("TRANSFERIR",datos,id)).Codigo=="00"&&await Saldo(a,2)==otro+25.50m,"repetir referencia no duplica transferencia interna");
   Verificar((await c1.Enviar("TRANSFERIR",Datos("BANC90","20000002",nombre2,26),id)).Codigo=="94","misma referencia con otro monto se rechaza");
   var historia=(await c2.Enviar("HISTORIAL_TRANSFERENCIAS")).Datos;
   Verificar(historia.EnumerateArray().Any(t=>t.GetProperty("direccion").GetString()=="ENTRANTE"&&t.GetProperty("tokenTransferencia").GetString()==interna.Datos.GetProperty("tokenTransferencia").GetString()),"destinatario interno ve el abono recibido");
   Verificar((await VerificarCuenta(c1,"BANC90","4000000000000001")).Codigo=="30","no permite transferirse a la misma cuenta");
   Verificar((await VerificarCuenta(c1,"BANC90","999999")).Codigo=="14","cuenta inexistente rechazada");
   Verificar((await c1.Enviar("TRANSFERIR",Datos("BANC90","20000002",nombre2,-1))).Codigo=="30","transferencia negativa rechazada");
   Verificar((await c1.Enviar("TRANSFERIR",Datos("BANC90","20000002",nombre2,0.001m))).Codigo=="30","rechaza fracciones menores a un centavo");
   var externaDest=await VerificarCuenta(c1,"BANC91","29000008");string nombre8=externaDest.Datos.GetProperty("nombreTitular").GetString()!;
   Verificar(externaDest.Codigo=="00","cajero verifica una cuenta real del segundo banco");
   decimal previo8=await Saldo(b,8),previo2=await Saldo(a,2);
   var externa=await c1.Enviar("TRANSFERIR",Datos("BANC91","29000008",nombre8,15));
   Verificar(externa.Codigo=="00","envío interbancario iniciado por usuario del cajero");
   Verificar(await Saldo(b,8)==previo8+15&&await Saldo(a,2)==previo2,"segundo banco recibe sin sesión del destinatario y sin cargar a otro usuario");
   string tr=externa.Datos.GetProperty("tokenTransferencia").GetString()!;
   Verificar(Convert.ToInt32((await a.Consultar("SELECT COUNT(*) FROM dbo.TransferenciaBancaria WHERE TokenTransferencia=@t",("@t",tr))).Rows[0][0])==1&&
    Convert.ToInt32((await b.Consultar("SELECT COUNT(*) FROM dbo.TransferenciaBancaria WHERE TokenTransferencia=@t",("@t",tr))).Rows[0][0])==1,"mismo token persistido por ambos bancos");
   await Login(receptor,8,"B");
   Verificar((await receptor.Enviar("CONSULTA_SALDO")).Datos.GetProperty("saldo").GetDecimal()==previo8+15,"cajero destino muestra saldo acreditado");
   Verificar((await receptor.Enviar("HISTORIAL_TRANSFERENCIAS")).Datos.EnumerateArray().Any(t=>t.GetProperty("tokenTransferencia").GetString()==tr),"cajero destino consulta transferencia recibida");
   // Proxy que acredita en B y pierde solamente la respuesta del envío.
   using var proxyFin=new CancellationTokenSource();var proxy=new TcpListener(IPAddress.Loopback,0);proxy.Start();
   int puertoProxy=((IPEndPoint)proxy.LocalEndpoint).Port;
   var proxyTarea=Task.Run(async()=>
   {
    try
    {
     while(!proxyFin.IsCancellationRequested)
     {
      using var tcp=await proxy.AcceptTcpClientAsync(proxyFin.Token);
      using var reader=new StreamReader(tcp.GetStream());using var writer=new StreamWriter(tcp.GetStream(),new UTF8Encoding(false)){AutoFlush=true,NewLine="\n"};
      string? linea=await reader.ReadLineAsync(proxyFin.Token);if(linea==null)continue;
      var q=JsonSerializer.Deserialize<JsonElement>(linea);
      var r=await BancoInterbancario.Enviar("127.0.0.1",puertoB,q);
      if(q.GetProperty("tipo").GetString()=="IB_TRANSFERIR")continue;
      await writer.WriteLineAsync(JsonSerializer.Serialize(r,BancoInterbancario.Json));
     }
    }catch(OperationCanceledException){}catch(SocketException) when(proxyFin.IsCancellationRequested){}
   });
   try
   {
    await sa.Interbancario.Configurar(adminA.Id.Value,"BANC91","BANCO DESTINO","127.0.0.1",puertoProxy,false,true,25000);
    string incierto=Guid.NewGuid().ToString("N");decimal previo=await Saldo(a,1);
    var perdida=await c1.Enviar("TRANSFERIR",Datos("BANC91","29000008",nombre8,7.25m),incierto);
    Verificar(perdida.Codigo=="96"&&c1.SolicitudIncierta==incierto,"respuesta perdida conserva referencia del envío");
    bool bloqueado=false;try{await c1.Enviar("DEPOSITO",new {monto=1,desglose=new Dictionary<int,int>{{1,1}}});}catch(InvalidOperationException){bloqueado=true;}
    Verificar(bloqueado,"transferencia pendiente bloquea otros movimientos");
    var estado=await c1.Enviar("ESTADO_TRANSFERENCIA",new {idOriginal=incierto});
    Verificar(estado.Codigo=="00"&&estado.Datos.GetProperty("codigo").GetString()=="00","consulta recupera confirmación del banco sin reenviar dinero");
    c1.ConfirmarConsultaPendiente(estado);
    Verificar(c1.SolicitudIncierta==null&&await Saldo(a,1)==previo-7.25m&&await Saldo(b,8)==previo8+22.25m,"conciliación no duplica crédito ni devuelve fondos ya enviados");
   }
   finally{proxyFin.Cancel();proxy.Stop();await proxyTarea;}
   await sa.Interbancario.Configurar(adminA.Id.Value,"BANC91","BANCO DESTINO","127.0.0.1",puertoB,false,true,25000);
   decimal saldoA=await Saldo(a,1);
   await sb.Interbancario.Configurar(adminB.Id.Value,"BANC91","BANCO DESTINO","0.0.0.0",puertoB,true,true,22.25m);
   var rechazo=await c1.Enviar("TRANSFERIR",Datos("BANC91","29000008",nombre8,1));
   Verificar(rechazo.Codigo=="65"&&await Saldo(a,1)==saldoA,"rechazo de cupo remoto libera una sola reserva");
   await sb.Interbancario.Configurar(adminB.Id.Value,"BANC91","BANCO DESTINO","0.0.0.0",puertoB,true,true,25000);
   await Login(c2,1,"A");
   await pa.Ejecutar("UPDATE dbo.Usuario SET Saldo=100 WHERE Id=1");
   decimal anterior2=await Saldo(a,2);
   var simultaneas=await Task.WhenAll(c1.Enviar("TRANSFERIR",Datos("BANC90","20000002",nombre2,75)),c2.Enviar("TRANSFERIR",Datos("BANC90","20000002",nombre2,75)));
   Verificar(simultaneas.Count(r=>r.Codigo=="00")==1&&simultaneas.Count(r=>r.Codigo=="51")==1,"dos cajeros no transfieren más saldo del disponible");
   Verificar(await Saldo(a,1)==25&&await Saldo(a,2)==anterior2+75,"saldos consistentes tras concurrencia");
   Verificar(Convert.ToDecimal((await a.Consultar("SELECT SUM(Denominacion*Cantidad) FROM dbo.InventarioCajero")).Rows[0][0])==efectivo,"transferencias no alteran billetes");
   Verificar(Convert.ToDecimal((await a.Consultar("SELECT RetiradoHoy FROM dbo.vw_Usuarios WHERE Id=1")).Rows[0][0])==0,"transferencia no consume el límite de retiros físicos");
   // Consulta bloqueada en la misma conexión, mientras el heartbeat continúa.
   await using(var bloqueo=new SqlConnection(Cadena(nombres[0])))
   {
    await bloqueo.OpenAsync();await using var tx=(SqlTransaction)await bloqueo.BeginTransactionAsync();
    await using(var cmd=new SqlCommand("UPDATE dbo.Usuario SET Saldo=Saldo WHERE Id=1",bloqueo,tx))await cmd.ExecuteNonQueryAsync();
    var consulta=c1.Enviar("CONSULTA_SALDO");await Task.Delay(200);
    var reloj=Stopwatch.StartNew();var hb=await c1.Enviar("HEARTBEAT");
    Verificar(hb.Codigo is ("00" or "96")&&reloj.ElapsedMilliseconds<2000&&!consulta.IsCompleted,"heartbeat independiente de una consulta SQL bloqueada");
    var detectados=await DescubridorServidores.Escanear("127.0.0.1",puertoA,default,false);
    Verificar(detectados.Any(s=>s.Puerto==puertoA&&s.Compatible),"búsqueda TCP localiza el servidor sin esperar al saldo bloqueado");
    await tx.RollbackAsync();Verificar((await consulta).Codigo=="00","consulta continúa después de liberar el bloqueo");
   }
   var usuarios=await Task.WhenAll(Enumerable.Range(0,24).Select(_=>a.Consultar("SELECT USER_NAME() Usuario")));
   Verificar(usuarios.All(t=>(string)t.Rows[0][0]=="CajeroServidor"),"24 consultas concurrentes mantienen permisos limitados");
   Verificar((string)(await pa.Consultar("SELECT USER_NAME() Usuario")).Rows[0][0]!="CajeroServidor","REVERT restaura identidad antes de devolver conexiones al pool");
   var spids=new HashSet<int>();
   for(int i=0;i<12;i++)spids.Add(Convert.ToInt32((await a.Consultar("SELECT @@SPID Id")).Rows[0][0]));
   Verificar(spids.Count<12,"conexiones físicas reutilizadas por el pool");
   // Fallo después de ambos saldos actualizados debe revertir todo.
   await pa.Ejecutar("CREATE TRIGGER dbo.tr_PruebaTransferencia ON dbo.TransferenciaCajero AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Concepto='FALLAR_SQL' AND Estado='APROBADA') THROW 50499,'Fallo de prueba',1; END");
   decimal antes1=await Saldo(a,1),antes2=await Saldo(a,2);
   var fallo=await c1.Enviar("TRANSFERIR",Datos("BANC90","20000002",nombre2,1,"FALLAR_SQL"));
   Verificar(fallo.Codigo=="96"&&await Saldo(a,1)==antes1&&await Saldo(a,2)==antes2,"rollback de transferencia interna conserva ambos saldos");
   await pa.Ejecutar("DROP TRIGGER dbo.tr_PruebaTransferencia");
   Verificar(c1.Conectado&&c2.Conectado&&receptor.Conectado,"conexiones siguen activas durante transferencias y errores SQL");
   // El monitor debe detectar un cajero sin heartbeat realmente, no solo una fila SQL antigua.
   using var mudo=new TcpClient();await mudo.ConnectAsync("127.0.0.1",puertoA);
   using var mw=new StreamWriter(mudo.GetStream(),new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
   using var mr=new StreamReader(mudo.GetStream());
   await mw.WriteLineAsync(Mensaje.Solicitud("REGISTRAR_CAJERO","ATM-04",new {equipo="PRUEBA",version="2.2.0"}).Serializar());await mr.ReadLineAsync();
   Verificar(await mr.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(35))==null,"cajero que no envía heartbeat se desconecta a los 30 segundos");
   Verificar(c1.Conectado&&c2.Conectado&&receptor.Conectado,"otros cajeros permanecen conectados al desconectar el silencioso");
  }
  finally
  {
   SqlConnection.ClearAllPools();
   await using var con=new SqlConnection(Cadena("master"));await con.OpenAsync();
   foreach(var nombre in nombres)
   {
    if(!Regex.IsMatch(nombre,"^BD_CajeroRed_Pruebas_[a-f0-9]{12}$"))throw new Exception("Nombre inseguro.");
    await using var cmd=new SqlCommand($"IF DB_ID('{nombre}') IS NOT NULL BEGIN ALTER DATABASE [{nombre}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nombre}]; END",con);
    await cmd.ExecuteNonQueryAsync();
   }
   Console.WriteLine("Dos bases temporales eliminadas; no se modificó la base operativa.");
  }
 }
}
