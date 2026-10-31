using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CajeroRed.Aplicacion;
using CajeroRed.Infraestructura;
using CajeroRed.Presentacion;
using CajeroRed.Protocolo;
using Microsoft.Data.SqlClient;

namespace CajeroRed.Pruebas;

internal static class Program
{
 private static int aprobadas;
 private static string raiz="";
 private static void Verificar(bool condicion,string nombre)
 {if(!condicion)throw new Exception("FALLÓ: "+nombre);aprobadas++;Console.WriteLine("OK · "+nombre);}
 [STAThread]
 public static int Main(string[] args)
 {
  raiz=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
  try
  {
   if(args.Contains("--revision-servidor")){RevisionServidor();return 0;}
   if(args.Contains("--entrada-tarjeta")){EntradaTarjeta();return 0;}
   if(args.Contains("--capturas")){Capturas();return 0;}
   if(args.Contains("--crear-login")){CrearLogin().GetAwaiter().GetResult();return 0;}
   if(args.Contains("--preparar-demo")){PrepararDemo().GetAwaiter().GetResult();return 0;}
   Basicas();
   if(args.Contains("--integracion"))Integracion().GetAwaiter().GetResult();
   Console.WriteLine($"RESULTADO: {aprobadas} comprobaciones correctas.");return 0;
  }
  catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
 private static void RevisionServidor()
 {
  var app=new Application();
  app.Resources.MergedDictionaries.Add(new ResourceDictionary(){Source=new Uri("/PresentacionCompartida;component/Tema.xaml",UriKind.Relative)});
  var w=new CajeroRed.Servidor.MainWindow();
  var panel=(StackPanel)((Border)((ContentControl)w.FindName("Contenido")).Content).Child;
  var campo=panel.Children.OfType<Grid>().Single();
  var secreto=campo.Children.OfType<PasswordBox>().Single();
  var visible=campo.Children.OfType<TextBox>().Single();
  var ojo=campo.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
  Verificar(visible.Visibility==Visibility.Collapsed,"contraseña oculta inicialmente");
  secreto.Password="prueba-A#123";ojo.IsChecked=true;
  Verificar(visible.Text=="prueba-A#123"&&secreto.Visibility==Visibility.Collapsed&&visible.Visibility==Visibility.Visible,"ojo muestra contraseña");
  visible.Text="corregida-B#456";ojo.IsChecked=false;
  Verificar(secreto.Password=="corregida-B#456"&&visible.Visibility==Visibility.Collapsed,"editar y ocultar conserva contraseña");
  var t=new DataTable();t.Columns.Add("Resultado");t.Columns.Add("Monto",typeof(decimal));t.Columns.Add("UsuarioId",typeof(int));t.Columns.Add("Cajero");
  t.Rows.Add("00",100m,1,"ATM-01");t.Rows.Add("00",200m,2,"ATM-02");t.Rows.Add("51",999m,3,"ATM-01");
  var tabla=Ui.Tabla(t.DefaultView);var filtro=new TextBox(){Text="ATM-01"};
  var tipo=w.GetType();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  tipo.GetField("pagina",flags)!.SetValue(w,"Depósitos");tipo.GetField("tabla",flags)!.SetValue(w,tabla);tipo.GetField("filtro",flags)!.SetValue(w,filtro);
  tipo.GetMethod("Filtrar",flags)!.Invoke(w,null);
  string pie=((TextBlock)w.FindName("EstadoPie")).Text;
  Verificar(t.DefaultView.Count==2&&pie.Contains(100m.ToString("N2"))&&!pie.Contains(999m.ToString("N2")),"totales del filtro excluyen rechazos");
  filtro.Text="inexistente";tipo.GetMethod("Filtrar",flags)!.Invoke(w,null);
  Verificar(t.DefaultView.Count==0&&((TextBlock)w.FindName("EstadoPie")).Text.Contains(0m.ToString("N2")),"sin resultados total cero");
  tipo.GetField("cerrando",flags)!.SetValue(w,true);w.Close();app.Shutdown();Console.WriteLine($"RESULTADO: {aprobadas} comprobaciones de interfaz correctas.");
 }
 private static void EntradaTarjeta()
 {
  var campo=Ui.EntradaTarjeta();
  bool RechazaEscritura(string texto)
  {
   var e=new TextCompositionEventArgs(Keyboard.PrimaryDevice,new TextComposition(InputManager.Current,campo,texto)){RoutedEvent=TextCompositionManager.PreviewTextInputEvent};
   campo.RaiseEvent(e);return e.Handled;
  }
  bool RechazaPegado(string texto)
  {
   var e=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,texto),false,DataFormats.UnicodeText){RoutedEvent=DataObject.PastingEvent};
   campo.RaiseEvent(e);return e.CommandCancelled;
  }
  Verificar(!RechazaPegado("4999999999990001"),"permite pegar una tarjeta de 16 dígitos");
  Verificar(RechazaPegado("499999999999999999999991"),"rechaza pegar más de 16 dígitos sin truncar la tarjeta");
  Verificar(RechazaEscritura("a")&&RechazaPegado("4999 999999990001"),"bloquea letras y espacios tanto al escribir como al pegar");
  campo.Text="4999999999990001";campo.Select(16,0);
  Verificar(RechazaEscritura("2")&&RechazaPegado("2"),"no permite agregar un dígito 17");
  campo.Select(15,1);
  Verificar(!RechazaEscritura("2")&&!RechazaPegado("2"),"permite reemplazar dígitos seleccionados sin superar 16");
 }
 private static void Basicas()
 {
  var alternativa=Billetes.Alternativa(60,new Dictionary<int,int>{{50,1},{20,3}});
  Verificar(alternativa!=null&&alternativa.GetValueOrDefault(20)==3,"desglose acotado encuentra solución cuando greedy simple falla");
  Verificar(Billetes.Alternativa(3,new Dictionary<int,int>{{5,10}})==null,"sin alternativa cuando no se puede representar el monto");
  Verificar(Billetes.Total(new Dictionary<int,int>{{200,1},{20,1},{10,1},{5,1},{1,4}})==239,"monto libre Q239");
  var m=Mensaje.Solicitud("RETIRO","ATM-01",new{monto=239,desglose=new Dictionary<int,int>{{1,239}}});
  Verificar(Mensaje.Leer(m.Serializar()).IdSolicitud==m.IdSolicitud,"JSON preserva correlación de solicitud");
  try{Ui.Monto("1,000");throw new Exception("Se aceptó monto ambiguo.");}catch(ArgumentException){Verificar(true,"montos sin separadores ambiguos");}
  var tabla=new DataTable();tabla.Columns.Add("Texto");tabla.Columns.Add("Monto",typeof(decimal));tabla.Rows.Add("=1+1",123.45m);
  var carpeta=Path.Combine(raiz,"Pruebas","Evidencias");Directory.CreateDirectory(carpeta);string xlsx=Path.Combine(carpeta,"reporte_prueba.xlsx");if(File.Exists(xlsx))File.Delete(xlsx);Exportador.Excel(tabla,xlsx);
  using var zip=System.IO.Compression.ZipFile.OpenRead(xlsx);using var rd=new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());string xml=rd.ReadToEnd();
  Verificar(xml.Contains("inlineStr")&&xml.Contains("=1+1")&&!xml.Contains("<f>"),"Excel real con datos tratados como texto y sin inyección de fórmulas");
 }
 private static async Task Integracion()
 {
  string nombre="BD_CajeroRed_Pruebas_"+Guid.NewGuid().ToString("N")[..12];
  string servidor=Environment.GetEnvironmentVariable("CAJERO_SQL_SERVIDOR")??".\\SQLEXPRESS";
  string master=$"Server={servidor};Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
  string cadena=$"Server={servidor};Database={nombre};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
  var pruebas=new List<string>();
  Console.WriteLine("Base AISLADA de pruebas: "+nombre);
  try
  {
   await using(var con=new SqlConnection(master))
   {
    await con.OpenAsync();
    foreach(var archivo in Directory.GetFiles(Path.Combine(raiz,"BaseDeDatos","Scripts"),"*.sql").Order())
    {
     var texto=File.ReadAllText(archivo).Replace("BD_CajeroRed_Fase2",nombre);
     foreach(var lote in Regex.Split(texto,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase).Where(x=>!string.IsNullOrWhiteSpace(x)))
     {await using var cmd=new SqlCommand(lote,con){CommandTimeout=30};await cmd.ExecuteNonQueryAsync();}
    }
   }
   var bd=new BaseDatos(cadena,false);var limitada=new BaseDatos(cadena);var admin=new Administracion(limitada);
   var contexto=await limitada.Consultar("SELECT USER_NAME() Nombre");Verificar((string)contexto.Rows[0][0]=="CajeroServidor","usuario propio con permisos limitados en cada conexión");
   try{await limitada.Ejecutar("UPDATE dbo.Usuario SET Saldo=Saldo WHERE Id=1");throw new Exception("Se permitió modificar saldos fuera del procedimiento.");}catch(SqlException){Verificar(true,"permisos impiden actualizar saldo fuera de procedimientos");}
   Verificar(await admin.Login("admin","GrupoCajero#2026"),"PBKDF2: autenticación administrativa");
   Verificar(!await new Administracion(bd).Login("admin","incorrecta"),"rechazo de contraseña administrativa incorrecta");
   var inventario=new Dictionary<int,int>{{200,20},{100,20},{50,20},{20,50},{10,50},{5,100},{1,1000}}; // Q10000
   Verificar(CajeroRed.Protocolo.Billetes.Total(inventario)==10000,"inventario de prueba Q10000");
   for(int i=1;i<=3;i++)await admin.Accion("INICIALIZAR",new {cajero=$"ATM-{i:00}",desglose=inventario});
   try{await admin.Accion("INICIALIZAR",new {cajero="ATM-01",desglose=inventario});throw new Exception("Se permitió reinicializar.");}catch(SqlException){Verificar(true,"inicialización solo una vez al día");}
   try{await admin.Accion("CARGAR",new {cajero="ATM-04",desglose=inventario});throw new Exception("Carga sin inicializar.");}catch(SqlException){Verificar(true,"rechazo de carga sin inicialización");}
   try{await admin.Accion("CARGAR",new {cajero="ATM-01",desglose=new Dictionary<int,int>{{200,150}}});throw new Exception("Capacidad superada.");}catch(SqlException){Verificar(true,"rechazo de capacidad mayor a Q30000");}
   var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int puerto=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
   await using var server=new ServidorTcp(limitada);await server.Iniciar(puerto);
   var tokens=new Dictionary<string,string>();server.TokenGenerado+=(c,t)=>{lock(tokens)tokens[c]=t;};
   await using var c1=new ClienteTcp(){Direccion="127.0.0.1",Puerto=puerto,Cajero="ATM-01"};
   await using var c2=new ClienteTcp(){Direccion="127.0.0.1",Puerto=puerto,Cajero="ATM-02"};
   await using var c3=new ClienteTcp(){Direccion="127.0.0.1",Puerto=puerto,Cajero="ATM-03"};
   c1.Iniciar();c2.Iniciar();c3.Iniciar();
   await Esperar(()=>c1.Conectado&&c2.Conectado&&c3.Conectado);
   Verificar(server.Conectados==3,"tres conexiones TCP persistentes simultáneas (locales)");
   await Task.Delay(TimeSpan.FromSeconds(35));
   Verificar(server.Conectados==3&&c1.Conectado&&c2.Conectado&&c3.Conectado,"tres conexiones permanecen estables más de 30 segundos con heartbeat");
   async Task<Mensaje> RegistroDirecto(string codigo)
   {
    using var socket=new TcpClient();await socket.ConnectAsync("127.0.0.1",puerto);
    using var w=new StreamWriter(socket.GetStream(),new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
    using var r=new StreamReader(socket.GetStream(),System.Text.Encoding.UTF8,false,4096,true);
    await w.WriteLineAsync(Mensaje.Solicitud("REGISTRAR_CAJERO",codigo,new{equipo="PRUEBA",version="2.0.0"}).Serializar());
    return Mensaje.Leer((await r.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
   }
   Verificar((await RegistroDirecto("ATM-99")).Codigo=="91","rechazo de cajero no dado de alta");
   Verificar((await RegistroDirecto("ATM-01")).Codigo=="94","rechazo de dos conexiones con el mismo código");
   async Task Login(ClienteTcp c,int usuario,string pin="1234")
   {
    string tarjeta=$"400000000000{usuario:0000}";var a=await c.Enviar("LOGIN",new {tarjeta,pin});
    Verificar(a.Codigo=="00"&&!a.Datos.TryGetProperty("tokenServidor",out _),"token visible solo en servidor · "+c.Cajero);
    string token;lock(tokens)token=tokens[c.Cajero];
    var b=await c.Enviar("VALIDAR_TOKEN",new {tarjeta,token});if(b.Codigo!="00")throw new Exception(b.Serializar());c.Sesion=b.Datos.GetProperty("sesion").GetString();
   }
   object Billetes(int monto)=>new {monto,desglose=new Dictionary<int,int>{{100,monto/100}}};
   await Login(c1,1);await Login(c2,1);
   Verificar((await c1.Enviar("RETIRO",Billetes(300))).Codigo=="00","retiro Q300 en ATM-01");
   Verificar((await c2.Enviar("RETIRO",Billetes(300))).Codigo=="61","límite diario global rechaza segundo retiro Q300");
   await bd.Ejecutar("UPDATE dbo.Usuario SET Saldo=300 WHERE Id=2");
   await Login(c1,2);await Login(c2,2);
   var simultaneos=await Task.WhenAll(c1.Enviar("RETIRO",Billetes(300)),c2.Enviar("RETIRO",Billetes(300)));
   Verificar(simultaneos.Count(x=>x.Codigo=="00")==1&&simultaneos.Count(x=>x.Codigo=="51")==1,"concurrencia: solo un retiro aprobado y saldo no negativo");
   var s=await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=2");Verificar((decimal)s.Rows[0][0]==0,"saldo final consistente tras concurrencia");
   await Login(c1,3);
   string referencia=Guid.NewGuid().ToString("N");var retiro=await c1.Enviar("RETIRO",Billetes(100),referencia);var repetido=await c1.Enviar("RETIRO",Billetes(100),referencia);
   Verificar(retiro.Codigo=="00"&&retiro.Datos.GetRawText()==repetido.Datos.GetRawText(),"idempotencia devuelve el mismo resultado sin doble cargo");
   Verificar((await c1.Enviar("RETIRO",Billetes(200),referencia)).Codigo=="94","rechazo del mismo ID con monto distinto");
   Verificar((await c1.Enviar("ESTADO_SOLICITUD",new {idOriginal=referencia})).Datos.GetProperty("codigo").GetString()=="00","consulta de estado después de incertidumbre");
   // Forzar existencias controladas en la base AISLADA para probar alternativas.
   await bd.Ejecutar("UPDATE dbo.InventarioCajero SET Cantidad=2 WHERE Cajero='ATM-01' AND Denominacion=100");
   var alternativa=await c1.Enviar("RETIRO",Billetes(300));
   Verificar(alternativa.Codigo=="92"&&alternativa.Datos.GetProperty("alternativa").ValueKind==JsonValueKind.Object,"desglose alternativo propuesto sin debitar");
   var antes=await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=3");
   var deposito=await c1.Enviar("DEPOSITO",new{monto=239,desglose=new Dictionary<int,int>{{200,1},{20,1},{10,1},{5,1},{1,4}}});
   Verificar(deposito.Codigo=="00"&&deposito.Datos.GetProperty("saldo").GetDecimal()==(decimal)antes.Rows[0][0]+239,"depósito actualiza saldo con monto libre Q239");
   Verificar((await c1.Enviar("CAMBIO_PIN",new{pinActual="1234",pinNuevo="5678"})).Codigo=="00","cambio de PIN");
   Verificar((await c2.Enviar("LOGIN",new{tarjeta="4000000000000003",pin="1234"})).Codigo=="55","PIN anterior rechazado en otro cajero");
   await Login(c2,3,"5678");
   for(int i=0;i<4;i++)Verificar((await c2.Enviar("DEPOSITO",new{monto=1,desglose=new Dictionary<int,int>{{1,1}}})).Codigo=="00","depósito de prueba para historial "+(i+1));
   var ultimas=await c2.Enviar("ULTIMAS_TRANSACCIONES");
   Verificar(ultimas.Datos.GetArrayLength()==5&&ultimas.Datos.EnumerateArray().Any(x=>x.GetProperty("cajero").GetString()=="ATM-01"),"últimas cinco transacciones del usuario incluyen distintos cajeros");
   var inv2=await bd.Consultar("SELECT Denominacion,Cantidad FROM dbo.InventarioCajero WHERE Cajero='ATM-02'");
   await bd.Ejecutar("UPDATE dbo.InventarioCajero SET Cantidad=CASE WHEN Denominacion=200 THEN 150 ELSE 0 END WHERE Cajero='ATM-02'");
   Verificar((await c2.Enviar("DEPOSITO",new{monto=1,desglose=new Dictionary<int,int>{{1,1}}})).Codigo=="93","depósito rechazado si supera Q30000");
   foreach(DataRow f in inv2.Rows)await bd.Ejecutar("UPDATE dbo.InventarioCajero SET Cantidad=@n WHERE Cajero='ATM-02' AND Denominacion=@d",("@n",f["Cantidad"]),("@d",f["Denominacion"]));
   for(int i=0;i<3;i++)
   {var c=i==0?c1:i==1?c2:c3;var r=await c.Enviar("LOGIN",new{tarjeta="4000000000000004",pin="0000"});Verificar(r.Codigo==(i==2?"75":"55"),"intento fallido global "+(i+1));}
   Verificar((await c1.Enviar("LOGIN",new{tarjeta="4000000000000004",pin="1234"})).Codigo=="75","bloqueo compartido por todos los cajeros");
   await admin.Accion("DESBLOQUEAR",new{usuarioId=4});await Login(c3,4);
   string usado;lock(tokens)usado=tokens[c3.Cajero];
   Verificar((await c3.Enviar("VALIDAR_TOKEN",new{tarjeta="4000000000000004",token=usado})).Codigo=="56","token de un solo uso: no puede reutilizarse");
   var login5=await c1.Enviar("LOGIN",new{tarjeta="4000000000000005",pin="1234"});string viejo;lock(tokens)viejo=tokens[c1.Cajero];
   await bd.Ejecutar("UPDATE dbo.Token SET Expira=DATEADD(second,-1,SYSDATETIME()) WHERE UsuarioId=5");
   Verificar((await c1.Enviar("VALIDAR_TOKEN",new{tarjeta="4000000000000005",token=viejo})).Codigo=="56","token expirado rechazado");
   await admin.Accion("CAMBIAR_TARJETA",new{usuarioId=1,tarjeta="4999999999990001",pin="9876"});
   Verificar((await c1.Enviar("LOGIN",new{tarjeta="4000000000000001",pin="1234"})).Codigo=="75","tarjeta reemplazada se rechaza como inactiva");
   var usuario=await bd.Consultar("SELECT RetiradoHoy,DisponibleHoy FROM dbo.vw_Usuarios WHERE Id=1");
   Verificar((decimal)usuario.Rows[0][0]==300&&(decimal)usuario.Rows[0][1]==200,"cambiar tarjeta conserva consumo del límite diario");
   var nueva=await c1.Enviar("LOGIN",new{tarjeta="4999999999990001",pin="9876"});string nuevoToken;lock(tokens)nuevoToken=tokens[c1.Cajero];
   var accesoTemporal=await c1.Enviar("VALIDAR_TOKEN",new{tarjeta="4999999999990001",token=nuevoToken});c1.Sesion=accesoTemporal.Datos.GetProperty("sesion").GetString();
   Verificar(accesoTemporal.Datos.GetProperty("debeCambiarPin").GetBoolean()&&(await c1.Enviar("CONSULTA_SALDO")).Codigo=="58","PIN temporal obliga a cambiar antes de operar");
   Verificar((await c1.Enviar("CAMBIO_PIN",new{pinActual="9876",pinNuevo="9877"})).Codigo=="00","cambio obligatorio de PIN temporal");
   Verificar((await c1.Enviar("CONSULTA_SALDO")).Datos.GetProperty("disponibleHoy").GetDecimal()==200,"nueva tarjeta operativa conserva disponible Q200");
   var comandos=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);c3.Comando+=m=>{if(m.Tipo=="COMANDO")comandos.TrySetResult(true);};
   await admin.Accion("FUERA_DE_SERVICIO",new{cajero="ATM-03"});await server.Comando("ATM-03","FUERA_DE_SERVICIO");await comandos.Task.WaitAsync(TimeSpan.FromSeconds(3));
   Verificar((await c3.Enviar("RETIRO",Billetes(100))).Codigo=="91","comando remoto y rechazo en servidor fuera de servicio");
   await admin.Accion("HABILITAR",new{cajero="ATM-03"});
   // Un fallo de SQL después de UPDATE debe revertir saldo e inventario.
   await Login(c2,6);
   await bd.Ejecutar("CREATE TRIGGER dbo.tr_PruebaRollback ON dbo.Transaccion AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE UsuarioId=6) THROW 50100,'Fallo simulado de prueba',1; END");
   var fallo=await c2.Enviar("RETIRO",Billetes(100));
   var saldo6=await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=6");
   Verificar(fallo.Codigo=="96"&&(decimal)saldo6.Rows[0][0]==5000,"rollback completo cuando falla el registro de la transacción");
   await bd.Ejecutar("DROP TRIGGER dbo.tr_PruebaRollback");
   await bd.Ejecutar("UPDATE dbo.Sesion SET UltimaActividad=DATEADD(second,-121,SYSDATETIME()) WHERE UsuarioId=6");
   Verificar((await c2.Enviar("CONSULTA_SALDO")).Codigo=="57","expiración de sesión en servidor");
   // El supervisor de heartbeats usa la hora de SQL, no reloj del cliente.
   await bd.Ejecutar("UPDATE dbo.Cajero SET UltimoHeartbeat=DATEADD(second,-31,SYSDATETIME()) WHERE Codigo='ATM-03'");
   await Esperar(()=>!c3.Conectado,5000);
   Verificar(true,"heartbeat vencido desconecta y registra alerta");
   await Esperar(()=>c3.Conectado,12000);Verificar(true,"reconexión automática conserva identidad del cajero");
   var logs=await bd.Consultar("SELECT COUNT(*) N FROM dbo.Bitacora WHERE Accion='CONEXION'");Verificar((int)logs.Rows[0][0]>=4,"auditoría de conexión y reconexión");
   var pines=await bd.Consultar("SELECT COUNT(*) N FROM dbo.Tarjeta WHERE DATALENGTH(PinHash)=32 AND LEN(Salt)=64");Verificar((int)pines.Rows[0][0]>=8,"PIN almacenado solo como SHA256 con salt");
   await ProbarInterbancario(bd,admin,server,puerto);
  }
  finally
  {
   SqlConnection.ClearAllPools();
   await using var con=new SqlConnection(master);await con.OpenAsync();
   // Nombre generado internamente, prefijo validado; nunca toca la base de entrega.
   if(!Regex.IsMatch(nombre,"^BD_CajeroRed_Pruebas_[a-f0-9]{12}$"))throw new Exception("Nombre de prueba inválido.");
   await using var cmd=new SqlCommand($"IF DB_ID('{nombre}') IS NOT NULL BEGIN ALTER DATABASE [{nombre}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nombre}]; END",con);await cmd.ExecuteNonQueryAsync();
   Console.WriteLine("Base aislada de pruebas eliminada.");
  }
 }
 private static async Task ProbarInterbancario(BaseDatos bd,Administracion admin,ServidorTcp servidor,int puerto)
 {
  var ib=servidor.Interbancario;
  await ib.Configurar(admin.Id!.Value,"BANC90","NETGEAR PRUEBAS","0.0.0.0",puerto,true,true,25000);
  await ib.Configurar(admin.Id.Value,"BANC01","PAR PRUEBAS","127.0.0.1",puerto,false,true,200);
  await ib.AsignarCuenta(admin.Id.Value,8,"2002000001");
  object Req(string tipo,string? token=null,decimal monto=50,string banco="BANC01")=>new {
   tipo,idMensaje=Guid.NewGuid().ToString("N"),bancoOrigen=banco,bancoDestino="BANC90",numeroCuenta="2002000001",
   cuentaOrigen="1234567890123456",cuentaDestino="2002000001",nombreOrigen="Origen de prueba",monto,
   tokenTransferencia=token??"TRF-20261031-000001",concepto="Prueba"};
  async Task<BancoInterbancario.Respuesta> Enviar(object m)=>await BancoInterbancario.Enviar("127.0.0.1",puerto,m);
  Verificar((await Enviar(Req("ib_ping"))).Datos.GetProperty("nombreBanco").GetString()=="NETGEAR PRUEBAS","IB_PING comparte listener TCP y admite tipo sin distinguir mayúsculas");
  var cuenta=await Enviar(new {tipo="IB_VERIFICAR_CUENTA",idMensaje="verificar-alias",bancoOrigen="BANC01",bancoDestino="BANC90",numeroCuenta="2002000001"});
  Verificar(cuenta.Codigo=="00"&&cuenta.Datos.GetProperty("existe").GetBoolean(),"IB_VERIFICAR_CUENTA acepta numeroCuenta");
  Verificar((await Enviar(Req("IB_TRANSFERIR",banco:"NO_AUTORIZADO"))).Codigo=="57","transferencia de banco no autorizado rechazada");
  var inicial=(decimal)(await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=8")).Rows[0][0];
  var aprobada=await Enviar(Req("IB_TRANSFERIR"));
  Verificar(aprobada.Codigo=="00","transferencia entrante acreditada");
  Verificar((await Enviar(Req("IB_TRANSFERIR"))).Codigo=="00","reintento de token devuelve confirmación original");
  Verificar((decimal)(await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=8")).Rows[0][0]==inicial+50,"token repetido no duplica saldo");
  Verificar((await Enviar(Req("IB_TRANSFERIR",monto:51))).Codigo=="94","token con monto diferente rechazado");
  Verificar((await Enviar(new {tipo="IB_VERIFICAR_CUENTA",idMensaje="alias-destino",bancoOrigen="BANC01",bancoDestino="BANC90",cuentaDestino="2002000001"})).Codigo=="00","consulta acepta alias cuentaDestino");
  Verificar((await Enviar(new {tipo="IB_VERIFICAR_CUENTA",idMensaje="cuenta-no-existe",bancoOrigen="BANC01",bancoDestino="BANC90",numeroCuenta="999"})).Codigo=="14","cuenta inexistente devuelve 14");
  Verificar((await Enviar(Req("IB_TRANSFERIR","TRF-20261031-000004",-1))).Codigo=="30","monto negativo no se acredita");
  var nulo=await ib.Atender("{\"tipo\":\"IB_TRANSFERIR\",\"idMensaje\":\"nulo\",\"cuentaDestino\":null}","127.0.0.1");
  Verificar(JsonDocument.Parse(nulo).RootElement.GetProperty("codigo").GetString()=="30","campos nulos devuelven error de formato");
  await bd.Ejecutar("CREATE TRIGGER dbo.tr_IB_Prueba ON dbo.TransferenciaBancaria AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE TokenTransferencia='TRF-20261031-999999') THROW 50300,'Fallo simulado interbancario',1; END");
  try
  {
   var fallo=await Enviar(Req("IB_TRANSFERIR","TRF-20261031-999999",10));
   Verificar(fallo.Codigo=="96","fallo SQL de transferencia no informa aprobación");
   Verificar((decimal)(await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=8")).Rows[0][0]==inicial+50,"rollback interbancario conserva saldo");
   Verificar((decimal)(await bd.Consultar("SELECT TransferidoHoy FROM dbo.BancoRed WHERE EsPropio=1")).Rows[0][0]==50,"rollback interbancario conserva cupo");
  }
  finally{await bd.Ejecutar("DROP TRIGGER dbo.tr_IB_Prueba");}

  var simultaneas=await Task.WhenAll(Enviar(Req("IB_TRANSFERIR","TRF-20261031-000002",150)),Enviar(Req("IB_TRANSFERIR","TRF-20261031-000003",150)));
  Verificar(simultaneas.Count(r=>r.Codigo=="00")==1&&simultaneas.Count(r=>r.Codigo=="65")==1,"concurrencia respeta cupo del banco");
  var estado=await Enviar(Req("IB_ESTADO_TRANSFERENCIA"));
  Verificar(estado.Codigo=="00"&&estado.Datos.GetProperty("tokenTransferencia").GetString()=="TRF-20261031-000001","extensión de consulta recupera confirmación persistida");
  Verificar(servidor.Conectados==3,"sondeos interbancarios no registran ni desconectan cajeros");
  await ib.IniciarDiscovery(puerto);
  using(var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
  {
   await udp.SendAsync(System.Text.Encoding.UTF8.GetBytes("DISCOVER_BANCO_ATM|BANC01|PRUEBA|127.0.0.1|5000"),new IPEndPoint(IPAddress.Loopback,5005));
   var respuesta=await udp.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
   string texto=System.Text.Encoding.UTF8.GetString(respuesta.Buffer);
   Verificar(texto.StartsWith("HERE_BANCO_ATM|BANC90|NETGEAR PRUEBAS|127.0.0.1|"),"descubrimiento UDP responde al remitente con formato acordado");
  }
  // Par simulado: responde a verificar; confirma el primer envío y pierde
  // la respuesta del segundo. Permite comprobar reservas sin dos bases reales.
  var remoto=new TcpListener(IPAddress.Loopback,0);remoto.Start();int portRemoto=((IPEndPoint)remoto.LocalEndpoint).Port;
  await ib.Configurar(admin.Id.Value,"BANC01","PAR PRUEBAS","127.0.0.1",portRemoto,false,true,25000);
  int transferidas=0;string? tokenPendiente=null;
  var tareaRemota=Task.Run(async()=>
  {
   for(int i=0;i<4;i++)
   {
    using var socket=await remoto.AcceptTcpClientAsync();
    using var reader=new StreamReader(socket.GetStream());
    using var writer=new StreamWriter(socket.GetStream(),new System.Text.UTF8Encoding(false)){AutoFlush=true,NewLine="\n"};
    using var doc=JsonDocument.Parse((await reader.ReadLineAsync())!);var q=doc.RootElement;
    string tipo=q.GetProperty("tipo").GetString()!;
    object datos;
    if(tipo=="IB_VERIFICAR_CUENTA")datos=new {nombreTitular="Destino simulado",existe=true};
    else
    {
     transferidas++;tokenPendiente=q.GetProperty("tokenTransferencia").GetString();
     if(transferidas==2)continue;
     datos=new {tokenTransferencia=tokenPendiente,monto=q.GetProperty("monto").GetDecimal(),bancoDestino=q.GetProperty("bancoDestino").GetString(),cuentaDestino=q.GetProperty("cuentaDestino").GetString()};
    }
    await writer.WriteLineAsync(JsonSerializer.Serialize(new {tipo="RESP_OK",idMensaje=q.GetProperty("idMensaje").GetString(),codigo="00",mensaje="OK",timestamp="2026-10-31 12:00:00",datos},BancoInterbancario.Json));
   }
  });
  try
  {
   var saldoAntes=(decimal)(await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=7")).Rows[0][0];
   var salida=await ib.Transferir(admin.Id.Value,7,"BANC01","1234567890123456",10,"Confirmada");
   Verificar(salida.Codigo=="00","transferencia saliente confirmada por el par");
   var incierta=await ib.Transferir(admin.Id.Value,7,"BANC01","1234567890123456",20,"Respuesta perdida");
   Verificar(incierta.Codigo=="96","respuesta perdida deja transferencia incierta");
   var fila=(await bd.Consultar("SELECT Estado FROM dbo.TransferenciaBancaria WHERE TokenTransferencia=@t",("@t",tokenPendiente))).Rows[0];
   Verificar((string)fila[0]=="INCIERTA"&&(decimal)(await bd.Consultar("SELECT Saldo FROM dbo.Usuario WHERE Id=7")).Rows[0][0]==saldoAntes-30,"timeout conserva reserva y no devuelve dinero posiblemente acreditado");
   await tareaRemota.WaitAsync(TimeSpan.FromSeconds(5));
  }
  finally{remoto.Stop();}
 }

 private static async Task Esperar(Func<bool> condicion,int milisegundos=10000)
 {var fin=DateTime.UtcNow.AddMilliseconds(milisegundos);while(!condicion()){if(DateTime.UtcNow>fin)throw new TimeoutException("No se alcanzó el estado de prueba.");await Task.Delay(80);}}
 private static async Task CrearLogin()
 {
  var bd=new BaseDatos("Server=.\\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",false);
  var estado=await bd.Consultar("SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) SoloWindows");
  if((int)estado.Rows[0][0]!=0)throw new Exception("SQL Server usa solo autenticación Windows. Configure un login Windows dedicado según el manual o habilite modo mixto con el DBA.");
  string clave=Convert.ToBase64String(RandomNumberGenerator.GetBytes(36))+"aA1!";
  await bd.Ejecutar("IF SUSER_ID('CajeroRed_Fase2App') IS NOT NULL THROW 50030,'El login ya existe; no se cambia su contraseña.',1; DECLARE @sql nvarchar(max)=N'CREATE LOGIN CajeroRed_Fase2App WITH PASSWORD='+QUOTENAME(@p,'''')+N', CHECK_POLICY=ON'; EXEC(@sql);",("@p",clave));
  var appbd=new BaseDatos("Server=.\\SQLEXPRESS;Database=BD_CajeroRed_Fase2;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",false);
  await appbd.Ejecutar("CREATE USER CajeroRed_Fase2App FOR LOGIN CajeroRed_Fase2App; ALTER ROLE rol_CajeroServidor ADD MEMBER CajeroRed_Fase2App;");
  var builder=new SqlConnectionStringBuilder(){DataSource=".\\SQLEXPRESS",InitialCatalog="BD_CajeroRed_Fase2",UserID="CajeroRed_Fase2App",Password=clave,Encrypt=true,TrustServerCertificate=true};
  var carpeta=Path.Combine(raiz,"Configuracion");Directory.CreateDirectory(carpeta);
  File.WriteAllText(Path.Combine(carpeta,"servidor.local.json"),JsonSerializer.Serialize(new{cadenaSql=builder.ConnectionString},new JsonSerializerOptions(){WriteIndented=true}));
  Console.WriteLine("Login propio creado. Configuración privada guardada; no se imprimió la contraseña.");
 }
 private static void Capturas()
 {
  var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary(){Source=new Uri("/PresentacionCompartida;component/Tema.xaml",UriKind.Relative)});
  var ventana=new CajeroRed.Servidor.MainWindow();ventana.Show();
  ventana.Dispatcher.InvokeAsync(async()=>
  {
   try
   {
    if(!await ventana.IniciarSesion("admin","GrupoCajero#2026"))throw new Exception("No se pudo iniciar sesión para las capturas.");
    await Task.Delay(300);Guardar(ventana,"servidor_resumen.png");
    var controles=(StackPanel)ventana.FindName("ControlesServicio");controles.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(500);
    var cajero=new CajeroRed.Cajero.MainWindow();cajero.Show();await Task.Delay(1500);Guardar(cajero,"cajero_acceso.png");
    var pantalla=(ContentControl)cajero.FindName("Pantalla");
    if(pantalla.Content is System.Windows.Controls.Border borde&&borde.Child is StackPanel acceso)
    {
     acceso.Children.OfType<TextBox>().First().Text="4000000000000001";acceso.Children.OfType<PasswordBox>().First().Password="1234";
     acceso.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(500);
     Guardar(cajero,"cajero_token.png");
     var tokenWindow=app.Windows.OfType<Window>().First(w=>w.Title.StartsWith("Token del usuario"));
     var codigo=((StackPanel)tokenWindow.Content).Children.OfType<TextBlock>().Single(t=>t.FontSize==42).Text;
     var tokenPanel=pantalla.Content is Border tokenBorder?(StackPanel)tokenBorder.Child:(StackPanel)pantalla.Content;tokenPanel.Children.OfType<PasswordBox>().First().Password=codigo;
     tokenPanel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(500);Guardar(cajero,"cajero_menu.png");tokenWindow.Close();
    }
    var menu=(StackPanel)ventana.FindName("Menu");
    foreach(string nombre in new[]{"Cajeros","Usuarios","Transacciones"})
    {
     menu.Children.OfType<Button>().First(b=>(string)b.Content==nombre).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     var contenido=(ContentControl)ventana.FindName("Contenido");
     await Esperar(()=>contenido.Content is DockPanel panel&&panel.Children.OfType<DataGrid>().FirstOrDefault()?.ItemsSource is DataView);
     if(nombre=="Usuarios")Verificar(((DataView)((DockPanel)contenido.Content).Children.OfType<DataGrid>().Single().ItemsSource).Count>=8,"navegación carga la tabla de usuarios sin esperar al siguiente refresco");
     await Task.Delay(200);Guardar(ventana,"servidor_"+nombre.ToLowerInvariant()+".png");
    }
    typeof(CajeroRed.Cajero.MainWindow).GetField("actividad",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(cajero,DateTime.UtcNow.AddSeconds(-61));
    await Task.Delay(1600);
    var salida=(TextBlock)cajero.FindName("Aviso");Verificar(salida.Text.Contains("60 segundos"),"interfaz cierra sesión cuando el reloj supera 60 segundos de inactividad");
    cajero.Close();ventana.Close();app.Shutdown();
   }catch(Exception e){Console.Error.WriteLine(e);app.Shutdown(1);}
  });
  app.Run();
 }
 private static async Task PrepararDemo()
 {
  var bd=new BaseDatos("Server=.\\SQLEXPRESS;Database=BD_CajeroRed_Fase2;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
  var admin=new Administracion(bd);if(!await admin.Login("admin","GrupoCajero#2026"))throw new Exception("Credenciales demo no disponibles.");
  var inv=new Dictionary<int,int>{{200,20},{100,20},{50,20},{20,50},{10,50},{5,100},{1,1000}};
  for(int i=1;i<=3;i++)
  {
   string cajero=$"ATM-{i:00}";var estado=await bd.Consultar("SELECT FechaInicializacion FROM dbo.Cajero WHERE Codigo=@c",("@c",cajero));
   if(estado.Rows[0][0] is DBNull)await admin.Accion("INICIALIZAR",new{cajero,desglose=inv});
  }
  Console.WriteLine("Primeros tres cajeros inicializados con Q10000 cada uno. Datos ficticios de demostración.");
 }
 private static void Guardar(Window w,string nombre)
 {
  w.UpdateLayout();var visual=(FrameworkElement)w.Content;var b=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);b.Render(visual);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));var carpeta=Path.Combine(raiz,"Documentacion","06_Manuales","Capturas");Directory.CreateDirectory(carpeta);using var f=File.Create(Path.Combine(carpeta,nombre));png.Save(f);Console.WriteLine(nombre);
 }
}
