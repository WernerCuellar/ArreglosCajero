using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CajeroRed.Aplicacion;
using CajeroRed.Presentacion;
using CajeroRed.Protocolo;

namespace CajeroRed.Cajero;

public partial class MainWindow : Window
{
 private sealed record Configuracion(string Direccion="127.0.0.1",int Puerto=5000,string CodigoCajero="ATM-01",string NombreBanco="NETGEAR");
 private Configuracion config=new();
 private readonly ClienteTcp cliente=new();
 private readonly string ruta;
 private readonly bool inicioAutomatico;
 private readonly DispatcherTimer reloj=new(){Interval=TimeSpan.FromSeconds(1)};
 private readonly HashSet<Window> formularios=[];
 private readonly List<Button> botonesMovimientos=[];
 private DateTime actividad=DateTime.UtcNow;
 private bool habilitado,inicializado,cerrando,ocupado,debeCambiarPin,configValida=true;
 private int generacion;
 private string tarjeta="",nombre="";
 private TextBox? campoTarjeta;
 private PasswordBox? campoPin;
 public MainWindow():this(true){}
 public MainWindow(bool conectar,string? configuracion=null,string? pendientes=null)
 {
  InitializeComponent();inicioAutomatico=conectar;
  string datos=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NETGEAR","Cajero");
  string baseConfig=Path.GetDirectoryName(Environment.ProcessPath)??AppContext.BaseDirectory;
  if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))baseConfig=AppContext.BaseDirectory;
  ruta=configuracion??Path.Combine(baseConfig,"cajero.local.json");
  string respaldo=configuracion??Path.Combine(datos,"config-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(baseConfig)))[..16]+".json");
  string aviso="";
  try
  {
   string archivo=File.Exists(respaldo)?respaldo:File.Exists(ruta)?ruta:Path.Combine(AppContext.BaseDirectory,"cajero.json");
   if(File.Exists(archivo))config=JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(archivo),Mensaje.Json)??throw new JsonException("Configuración vacía.");
   var args=Environment.GetCommandLineArgs();
   for(int i=1;i<args.Length;i++)
   {
    if(args[i] is not ("--cajero" or "--servidor" or "--puerto"))continue;
    if(i+1>=args.Length)throw new ArgumentException("Falta valor para "+args[i]);
    string opcion=args[i],valor=args[++i];
    config=opcion switch{"--cajero"=>config with {CodigoCajero=valor},"--servidor"=>config with {Direccion=valor},_=>config with {Puerto=int.Parse(valor)}};
   }
   if(config.NombreBanco=="Sistema bancario")config=config with {NombreBanco="NETGEAR"};
   ValidarConfig(config);
  }
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException or OverflowException)
  {configValida=false;config=new();aviso="Configuración inválida o ilegible. Use Configuración de conexión. "+e.Message;}
  // Al no poder escribir junto al ejecutable se usa el perfil local del usuario.
  rutaRespaldo=respaldo;
  cliente.ConfigurarPendientes(pendientes??Path.Combine(datos,"Pendientes"));
  AplicarConfig();
  cliente.Estado+=estado=>Dispatcher.BeginInvoke(()=>
  {
   if(cerrando)return;
   Conexion.Text=estado;Conexion.Foreground=cliente.Conectado?Brushes.DarkGreen:Brushes.Firebrick;
   if(!cliente.Conectado){habilitado=false;LimpiarSesion("Sin conexión con el servidor.");}
  });
  cliente.Comando+=m=>Dispatcher.BeginInvoke(()=>{if(!cerrando)AtenderComando(m);});
  cliente.PendienteCambiado+=()=>Dispatcher.BeginInvoke(()=>{if(!cerrando)ActualizarBloqueo();});
  Pie.Children.Add(Ui.Boton("Buscar servidor",BuscarServidor));
  Pie.Children.Add(Ui.Boton("Configuración de conexión",Configurar));
  Pie.Children.Add(Ui.BotonAsync("Probar conexión",()=>Acceso(ProbarConexion)));
  Pie.Children.Add(Ui.BotonAsync("Consultar operación pendiente",()=>Operacion(ConsultarPendiente)));
  PreviewKeyDown+=(_,_)=>RegistrarActividad();PreviewMouseDown+=(_,_)=>RegistrarActividad();
  reloj.Tick+=(_,_)=>RevisarInactividad();
  Closing+=async(_,e)=>{
   if(cerrando)return;e.Cancel=true;cerrando=true;reloj.Stop();
   foreach(var w in formularios.ToArray())w.Close();
   await cliente.DisposeAsync();_=Dispatcher.BeginInvoke(new Action(Close));
  };
  if(conectar&&!File.Exists(ruta)&&!File.Exists(respaldo)&&!Environment.GetCommandLineArgs().Contains("--servidor"))Loaded+=(_,_)=>BuscarServidor();
  MostrarInicio();if(aviso!="")Aviso.Text=aviso;
  reloj.Start();if(conectar&&configValida)cliente.Iniciar();
 }
 private readonly string rutaRespaldo;
 private static void ValidarConfig(Configuracion c)
 {
  if(string.IsNullOrWhiteSpace(c.Direccion)||Uri.CheckHostName(c.Direccion.Trim())==UriHostNameType.Unknown||
   c.Puerto is <1 or >65535||string.IsNullOrWhiteSpace(c.CodigoCajero)||
   !System.Text.RegularExpressions.Regex.IsMatch(c.CodigoCajero,@"^[A-Za-z0-9_-]{1,24}$")||string.IsNullOrWhiteSpace(c.NombreBanco))
   throw new ArgumentException("Revise IP/nombre, puerto (1–65535), nombre del banco y código único del cajero.");
 }
 private void AplicarConfig()
 {
  cliente.Direccion=config.Direccion;cliente.Puerto=config.Puerto;cliente.Cajero=config.CodigoCajero;
  Identidad.Text=config.CodigoCajero;Banco.Text=config.NombreBanco.ToUpperInvariant();
 }
 private void Configurar()
 {
  if(cliente.Sesion!=null){Aviso.Text="Cierre su sesión antes de cambiar la conexión.";return;}
  try
  {
   var f=Ui.FormularioValidado(this,"Conexión del cajero",
    [("direccion","IP o nombre del servidor",config.Direccion,false),("puerto","Puerto",config.Puerto.ToString(),false),("cajero","Código único de cajero",config.CodigoCajero,false)],
    valores=>ValidarConfig(config with {Direccion=valores["direccion"],Puerto=int.Parse(valores["puerto"]),CodigoCajero=valores["cajero"]}));
   if(f==null)return;
   var nueva=config with {Direccion=f["direccion"],Puerto=int.Parse(f["puerto"]),CodigoCajero=f["cajero"]};
   string texto=JsonSerializer.Serialize(nueva,new JsonSerializerOptions(Mensaje.Json){WriteIndented=true});
   try{File.WriteAllText(ruta,texto);}
   catch(Exception e) when(e is IOException or UnauthorizedAccessException)
   {Directory.CreateDirectory(Path.GetDirectoryName(rutaRespaldo)!);File.WriteAllText(rutaRespaldo,texto);}
   // Si existía un respaldo de configuración, actualizarlo evita cargar uno viejo.
   if(rutaRespaldo!=ruta&&File.Exists(rutaRespaldo))File.WriteAllText(rutaRespaldo,texto);
   config=nueva;configValida=true;LimpiarSesion("Configuración guardada.");AplicarConfig();
   cliente.ReconectarAhora();if(inicioAutomatico)cliente.Iniciar();
  }
  catch(Exception e){Aviso.Text="No se pudo guardar la configuración. "+e.Message;}
 }
 private async Task ProbarConexion()
 {
  ValidarConfig(config);
  using var tcp=new TcpClient();await tcp.ConnectAsync(config.Direccion,config.Puerto).WaitAsync(TimeSpan.FromSeconds(5));
  Aviso.Text=$"Servidor accesible en {config.Direccion}:{config.Puerto}. Debe aceptar también el registro del cajero.";
 }
 private void RegistrarActividad()=>actividad=DateTime.UtcNow;
 private void PrepararFormulario(Window w)
 {
  formularios.Add(w);w.PreviewKeyDown+=(_,_)=>RegistrarActividad();w.PreviewMouseDown+=(_,_)=>RegistrarActividad();
  w.PreviewTextInput+=(_,_)=>RegistrarActividad();w.Closed+=(_,_)=>formularios.Remove(w);
 }
 private void RevisarInactividad()
 {
  if(cliente.Sesion!=null&&DateTime.UtcNow-actividad>TimeSpan.FromSeconds(60))
   CerrarSesion("Sesión cerrada por 60 segundos sin actividad.");
 }
 private void CerrarSesion(string aviso="Sesión cerrada.")
 {
  Task? notificar=null;try{if(cliente.Sesion!=null)notificar=cliente.Enviar("LOGOUT");}catch(Exception){}
  LimpiarSesion(aviso);
  if(notificar!=null)_=ObservarCierre(notificar);
 }
 private static async Task ObservarCierre(Task tarea){try{await tarea;}catch(Exception){}}
 private void LimpiarSesion(string aviso)
 {
  generacion++;cliente.CerrarSesionLocal();ocupado=false;debeCambiarPin=false;
  nombre="";tarjeta="";campoPin?.Clear();campoTarjeta?.Clear();
  foreach(var w in formularios.ToArray())w.Close();
  MostrarInicio();Aviso.Text=aviso;
 }
 private void AtenderComando(Mensaje m)
 {
  if(m.Datos.ValueKind!=JsonValueKind.Object)return;
  if(m.Datos.TryGetProperty("habilitado",out var h))
  {
   bool antes=habilitado&&inicializado;habilitado=h.GetBoolean();inicializado=m.Datos.GetProperty("inicializado").GetBoolean();
   if(!habilitado||!inicializado)
   {
    if(cliente.Sesion!=null||formularios.Count>0||antes)LimpiarSesion(!habilitado?"Cajero deshabilitado por administración.":"El cajero necesita inicialización diaria.");
    else MostrarInicio();
   }
   else if(!antes){MostrarInicio();Aviso.Text="Cajero disponible. Ingrese su tarjeta y PIN.";}
   return;
  }
  if(m.Tipo!="COMANDO")return;
  string comando=m.Datos.GetProperty("comando").GetString()!;
  if(comando=="FUERA_DE_SERVICIO"){habilitado=false;LimpiarSesion("Cajero fuera de servicio por administración.");}
  if(comando=="HABILITAR")_=ActualizarEstado();
  if(comando=="MENSAJE")Aviso.Text=m.Datos.GetProperty("texto").GetString()??"";
  if(comando=="ACTUALIZAR_INVENTARIO")_=ActualizarEstado();
 }
 private async Task ActualizarEstado(){try{AtenderComando(await cliente.Enviar("HEARTBEAT"));}catch(Exception e){Aviso.Text=e.Message;}}
 private void MostrarInicio()
 {
  botonesMovimientos.Clear();
  if(cliente.Sesion!=null&&habilitado&&inicializado){MostrarMenu();return;}
  if(!configValida||!cliente.Conectado||!habilitado||!inicializado)
  {
   var fuera=new StackPanel();fuera.Children.Add(Ui.Titulo("Fuera de servicio"));
   fuera.Children.Add(Ui.Texto(!configValida?"Revise Configuración de conexión.":!cliente.Conectado?"Estamos intentando conectar con el servidor.":!habilitado?"El cajero fue deshabilitado por administración.":"Este cajero requiere su inicialización diaria desde el servidor."));
   Pantalla.Content=Ui.Tarjeta(fuera);return;
  }
  var p=new StackPanel(){MaxWidth=430,HorizontalAlignment=HorizontalAlignment.Center};
  p.Children.Add(Ui.Titulo("Bienvenido"));p.Children.Add(Ui.Texto("Ingrese su tarjeta y PIN para recibir el token de acceso."));
  p.Children.Add(Ui.Texto("Número de tarjeta"));campoTarjeta=Ui.EntradaTarjeta();p.Children.Add(campoTarjeta);
  p.Children.Add(Ui.Texto("PIN"));campoPin=Ui.EntradaSecretaNumerica(4);p.Children.Add(campoPin);
  var b=Ui.BotonAsync("Continuar",()=>Acceso(Login));b.IsDefault=true;p.Children.Add(b);Pantalla.Content=Ui.Tarjeta(p);
 }
 private async Task Acceso(Func<Task> accion)
 {int version=generacion;try{await accion();}catch(Exception e){if(version==generacion)Aviso.Text=e.Message;}}
 private bool SigueVigente(int version,string? sesion)=>version==generacion&&sesion!=null&&sesion==cliente.Sesion&&habilitado&&inicializado;
 private async Task Login()
 {
  if(campoTarjeta==null||campoPin==null)return;
  string numero=campoTarjeta.Text,pin=campoPin.Password;campoPin.Clear();int version=generacion;
  if(numero.Length!=16||!numero.All(char.IsAsciiDigit)||pin.Length!=4||!pin.All(char.IsAsciiDigit))throw new ArgumentException("Tarjeta: 16 dígitos. PIN: 4 dígitos.");
  var r=await cliente.Enviar("LOGIN",new {tarjeta=numero,pin});
  if(version!=generacion||!Exito(r))return;
  tarjeta=numero;
  var p=new StackPanel(){MaxWidth=430,HorizontalAlignment=HorizontalAlignment.Center};
  p.Children.Add(Ui.Titulo("Validar token"));p.Children.Add(Ui.Texto("Ingrese los 6 dígitos del token mostrado en el servidor. Vence en 2 minutos y es de un solo uso."));
  var token=Ui.EntradaSecretaNumerica(6);p.Children.Add(token);
  p.Children.Add(Ui.BotonAsync("Ingresar",()=>Acceso(async()=>
  {
   string valor=token.Password;token.Clear();
   if(valor.Length!=6||!valor.All(char.IsAsciiDigit))throw new ArgumentException("Ingrese los seis dígitos del token.");
   var respuesta=await cliente.Enviar("VALIDAR_TOKEN",new {tarjeta=numero,token=valor});
   if(version!=generacion||!Exito(respuesta))return;
   if(!respuesta.Datos.TryGetProperty("usuarioId",out var identidad))throw new InvalidOperationException("Actualice el servidor para recuperar operaciones pendientes por usuario.");
   cliente.EstablecerSesion(respuesta.Datos.GetProperty("sesion").GetString()!,identidad.GetInt32());
   nombre=respuesta.Datos.GetProperty("nombre").GetString()!;debeCambiarPin=respuesta.Datos.GetProperty("debeCambiarPin").GetBoolean();
   RegistrarActividad();Aviso.Text="Sesión iniciada. No comparta su PIN.";MostrarMenu();
  })));
  p.Children.Add(Ui.Boton("Cancelar",()=>LimpiarSesion("Acceso cancelado.")));Pantalla.Content=Ui.Tarjeta(p);
 }
 private bool Exito(Mensaje r)
 {
  if(r.Codigo=="00")return true;
  if(r.Codigo is "75" or "57" or "91")LimpiarSesion(r.MensajeTexto);
  else Aviso.Text="Operación rechazada · "+r.Codigo+": "+r.MensajeTexto;
  return false;
 }
 private void ActualizarBloqueo()
 {
  foreach(var boton in botonesMovimientos)boton.IsEnabled=!debeCambiarPin&&!cliente.MovimientosBloqueados;
  if(cliente.ErrorRecuperacion!=null)Aviso.Text=cliente.ErrorRecuperacion;
  else if(cliente.SolicitudIncierta!=null)Aviso.Text="Movimiento pendiente. Consulte su referencia antes de retirar, depositar o transferir.";
 }
 private void MostrarMenu()
 {
  botonesMovimientos.Clear();
  var p=new StackPanel();p.Children.Add(Ui.Titulo("Hola, "+nombre));p.Children.Add(Ui.Texto("Tarjeta **** "+(tarjeta.Length>=4?tarjeta[^4..]:"")));
  var opciones=new WrapPanel();
  void Opcion(string titulo,Func<Task> accion,bool disponible=true,bool dinero=false)
  {var b=Ui.BotonAsync(titulo,()=>Operacion(accion));b.Width=260;b.Height=70;b.IsEnabled=disponible;opciones.Children.Add(b);if(dinero)botonesMovimientos.Add(b);}
  if(debeCambiarPin)p.Children.Add(Ui.Texto("Debe cambiar el PIN temporal antes de operar."));
  Opcion("Retirar efectivo",()=>Movimiento("RETIRO"),!debeCambiarPin,true);
  Opcion("Depositar efectivo",()=>Movimiento("DEPOSITO"),!debeCambiarPin,true);
  Opcion("Transferir dinero",TransferirDinero,!debeCambiarPin,true);
  Opcion("Mis transferencias",HistorialTransferencias,!debeCambiarPin);
  Opcion("Consultar saldo",Saldo,!debeCambiarPin);Opcion("Últimas 5 transacciones",Historial,!debeCambiarPin);Opcion("Cambiar PIN",CambiarPin);
  var salir=Ui.Boton("Cerrar sesión",()=>CerrarSesion());salir.Width=260;salir.Height=70;opciones.Children.Add(salir);
  p.Children.Add(opciones);Pantalla.Content=p;ActualizarBloqueo();
 }
 private async Task Operacion(Func<Task> accion)
 {
  if(cliente.Sesion==null){Aviso.Text="Inicie sesión antes de consultar u operar.";return;}
  if(ocupado)return;ocupado=true;int version=generacion;
  try{RegistrarActividad();await accion();}
  catch(Exception e){if(version==generacion)Aviso.Text=e.Message;ActualizarBloqueo();}
  finally{if(version==generacion)ocupado=false;}
 }
}
