using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CajeroRed.Aplicacion;
using CajeroRed.Presentacion;
using CajeroRed.Protocolo;

namespace CajeroRed.Cajero;

public partial class MainWindow : Window
{
 private sealed record Configuracion(string Direccion="127.0.0.1",int Puerto=5000,string CodigoCajero="ATM-01",string NombreBanco="Sistema bancario");
 private Configuracion config=new();private readonly ClienteTcp cliente=new();
 private readonly string ruta=Path.Combine(AppContext.BaseDirectory,"cajero.local.json");
 private readonly DispatcherTimer reloj=new(){Interval=TimeSpan.FromSeconds(1)};
 private DateTime actividad=DateTime.UtcNow;private bool habilitado,inicializado,cerrando,ocupado,debeCambiarPin;
 private string tarjeta="",nombre="";private TextBox? campoTarjeta;private PasswordBox? campoPin;
 public MainWindow()
 {
  InitializeComponent();
  try
  {
   var archivo=File.Exists(ruta)?ruta:Path.Combine(AppContext.BaseDirectory,"cajero.json");
   if(File.Exists(archivo))config=JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(archivo),Mensaje.Json)??new();
   var args=Environment.GetCommandLineArgs();
   for(int i=1;i<args.Length-1;i++)
   {if(args[i]=="--cajero")config=config with {CodigoCajero=args[++i]};else if(args[i]=="--servidor")config=config with {Direccion=args[++i]};else if(args[i]=="--puerto")config=config with {Puerto=int.Parse(args[++i])};}
  }catch(Exception e){Ui.Error(e);}
  AplicarConfig();
  cliente.Estado+=estado=>Dispatcher.BeginInvoke(()=>
  {
   Conexion.Text=estado;Conexion.Foreground=cliente.Conectado?Brushes.DarkGreen:Brushes.Firebrick;
   if(!cliente.Conectado){habilitado=false;cliente.Sesion=null;MostrarInicio();}
  });
  cliente.Comando+=m=>Dispatcher.BeginInvoke(()=>AtenderComando(m));
  Pie.Children.Add(Ui.Boton("Configuración de conexión",Configurar));
  Pie.Children.Add(Ui.BotonAsync("Probar conexión",ProbarConexion));
  Pie.Children.Add(Ui.BotonAsync("Consultar operación pendiente",ConsultarPendiente));
  PreviewKeyDown+=(_,_)=>actividad=DateTime.UtcNow;PreviewMouseDown+=(_,_)=>actividad=DateTime.UtcNow;
  reloj.Tick+=async(_,_)=>
  {
   if(cliente.Sesion!=null&&DateTime.UtcNow-actividad>TimeSpan.FromSeconds(60))
   {var cierre=cliente.Enviar("LOGOUT");cliente.Sesion=null;MostrarInicio();Aviso.Text="Sesión cerrada por 60 segundos sin actividad.";try{await cierre;}catch(Exception){} }
  };
  Closing+=async(_,e)=>{if(cerrando)return;e.Cancel=true;cerrando=true;reloj.Stop();await cliente.DisposeAsync();Close();};
  MostrarInicio();reloj.Start();cliente.Iniciar();
 }
 private void AplicarConfig()
 {
  cliente.Direccion=config.Direccion;cliente.Puerto=config.Puerto;cliente.Cajero=config.CodigoCajero;
  Identidad.Text=config.CodigoCajero;Banco.Text=config.NombreBanco.ToUpperInvariant();
 }
 private void Configurar()
 {
  if(cliente.Sesion!=null){Ui.Mostrar(this,"Configuración","Cierre su sesión antes de cambiar la conexión.");return;}
  var f=Ui.Formulario(this,"Conexión del cajero",("direccion","IP o nombre del servidor",config.Direccion,false),("puerto","Puerto",config.Puerto.ToString(),false),("cajero","Código de cajero",config.CodigoCajero,false));
  if(f==null)return;
  if(string.IsNullOrWhiteSpace(f["direccion"])||!int.TryParse(f["puerto"],out int puerto)||puerto is <1 or >65535||string.IsNullOrWhiteSpace(f["cajero"])||f["cajero"].Length>24){Ui.Error(new ArgumentException("Dirección, puerto o código inválidos."));return;}
  config=config with {Direccion=f["direccion"],Puerto=puerto,CodigoCajero=f["cajero"]};
  File.WriteAllText(ruta,JsonSerializer.Serialize(config,new JsonSerializerOptions(Mensaje.Json){WriteIndented=true}));
  AplicarConfig();cliente.ReconectarAhora();
 }
 private async Task ProbarConexion()
 {
  using var tcp=new TcpClient();await tcp.ConnectAsync(config.Direccion,config.Puerto).WaitAsync(TimeSpan.FromSeconds(5));
  Ui.Mostrar(this,"Conexión TCP correcta",$"Servidor accesible en {config.Direccion}:{config.Puerto}. El registro también debe ser aceptado por el servidor.");
 }
 private void AtenderComando(Mensaje m)
 {
  if(m.Datos.TryGetProperty("habilitado",out var h))
  {
   bool antes=habilitado&&inicializado;habilitado=h.GetBoolean();inicializado=m.Datos.GetProperty("inicializado").GetBoolean();
   if(!habilitado||!inicializado)cliente.Sesion=null;
   if(antes!=(habilitado&&inicializado))MostrarInicio();
   return;
  }
  if(m.Tipo!="COMANDO")return;
  string comando=m.Datos.GetProperty("comando").GetString()!;
  if(comando=="FUERA_DE_SERVICIO"){habilitado=false;cliente.Sesion=null;MostrarInicio();}
  if(comando=="HABILITAR"){habilitado=true;MostrarInicio();}
  if(comando=="MENSAJE")Ui.Mostrar(this,"Mensaje de administración",m.Datos.GetProperty("texto").GetString()??"");
  if(comando=="ACTUALIZAR_INVENTARIO")_=ActualizarEstado();
 }
 private async Task ActualizarEstado(){try{var r=await cliente.Enviar("HEARTBEAT");AtenderComando(r);}catch(Exception e){Aviso.Text=e.Message;}}
 private void MostrarInicio()
 {
  if(cliente.Sesion!=null&&habilitado&&inicializado){MostrarMenu();return;}
  if(!cliente.Conectado||!habilitado||!inicializado)
  {
   var fuera=new StackPanel();fuera.Children.Add(Ui.Titulo("Fuera de servicio"));fuera.Children.Add(Ui.Texto(!cliente.Conectado?"Estamos intentando conectar con el servidor.":!habilitado?"Este cajero fue deshabilitado por administración.":"Este cajero requiere su inicialización diaria desde el servidor."));
   Pantalla.Content=Ui.Tarjeta(fuera);return;
  }
  var p=new StackPanel(){MaxWidth=430,HorizontalAlignment=HorizontalAlignment.Center};
  p.Children.Add(Ui.Titulo("Bienvenido"));p.Children.Add(Ui.Texto("Ingrese su tarjeta y PIN para recibir el token de acceso."));
  p.Children.Add(Ui.Texto("Número de tarjeta"));campoTarjeta=new TextBox(){MaxLength=16};p.Children.Add(campoTarjeta);
  p.Children.Add(Ui.Texto("PIN"));campoPin=new PasswordBox(){MaxLength=4};p.Children.Add(campoPin);
  var b=Ui.BotonAsync("Continuar",Login);b.IsDefault=true;p.Children.Add(b);Pantalla.Content=Ui.Tarjeta(p);
 }
 private async Task Login()
 {
  if(campoTarjeta==null||campoPin==null)return;
  tarjeta=campoTarjeta.Text.Trim();string pin=campoPin.Password;campoPin.Clear();
  if(tarjeta.Length!=16||!tarjeta.All(char.IsAsciiDigit)||pin.Length!=4||!pin.All(char.IsAsciiDigit))throw new ArgumentException("La tarjeta debe tener 16 dígitos y el PIN 4.");
  var r=await cliente.Enviar("LOGIN",new {tarjeta,pin});if(!Exito(r))return;
  var p=new StackPanel(){MaxWidth=430,HorizontalAlignment=HorizontalAlignment.Center};p.Children.Add(Ui.Titulo("Validar token"));p.Children.Add(Ui.Texto("Solicite el token de 6 dígitos mostrado en la ventana del servidor. Vence en 2 minutos y solo se usa una vez."));
  var token=new PasswordBox(){MaxLength=6};p.Children.Add(token);
  p.Children.Add(Ui.BotonAsync("Ingresar",async()=>
  {
   string valor=token.Password;token.Clear();if(valor.Length!=6||!valor.All(char.IsAsciiDigit))throw new ArgumentException("Ingrese los 6 dígitos del token.");
   var respuesta=await cliente.Enviar("VALIDAR_TOKEN",new {tarjeta,token=valor});if(!Exito(respuesta))return;
   cliente.Sesion=respuesta.Datos.GetProperty("sesion").GetString();nombre=respuesta.Datos.GetProperty("nombre").GetString()!;
   debeCambiarPin=respuesta.Datos.GetProperty("debeCambiarPin").GetBoolean();actividad=DateTime.UtcNow;MostrarMenu();
  }));
  p.Children.Add(Ui.Boton("Cancelar",MostrarInicio));Pantalla.Content=Ui.Tarjeta(p);
 }
 private bool Exito(Mensaje r)
 {
  if(r.Codigo=="00")return true;
  Ui.Mostrar(this,"Operación rechazada · "+r.Codigo,r.MensajeTexto);
  if(r.Codigo is "75" or "57" or "91"){cliente.Sesion=null;MostrarInicio();}
  return false;
 }
 private void MostrarMenu()
 {
  var p=new StackPanel();p.Children.Add(Ui.Titulo("Hola, "+nombre));p.Children.Add(Ui.Texto("Tarjeta **** "+(tarjeta.Length>=4?tarjeta[^4..]:"")));
  var opciones=new WrapPanel();
  void Opcion(string titulo,Func<Task> accion,bool disponible=true)
  {var b=Ui.BotonAsync(titulo,()=>Operacion(accion));b.Width=260;b.Height=70;b.IsEnabled=disponible;opciones.Children.Add(b);}
  if(debeCambiarPin)p.Children.Add(Ui.Texto("Debe cambiar el PIN temporal antes de realizar otras operaciones."));
  Opcion("Retirar efectivo",()=>Movimiento("RETIRO"),!debeCambiarPin&&cliente.SolicitudIncierta==null);
  Opcion("Depositar efectivo",()=>Movimiento("DEPOSITO"),!debeCambiarPin&&cliente.SolicitudIncierta==null);
  Opcion("Consultar saldo",Saldo,!debeCambiarPin);Opcion("Últimas 5 transacciones",Historial,!debeCambiarPin);Opcion("Cambiar PIN",CambiarPin);
  Opcion("Cerrar sesión",async()=>{await cliente.Enviar("LOGOUT");cliente.Sesion=null;tarjeta="";MostrarInicio();});
  p.Children.Add(opciones);Pantalla.Content=p;
 }
 private async Task Operacion(Func<Task> accion)
 {
  if(ocupado)return;ocupado=true;
  try{if(cliente.Sesion==null)throw new ArgumentException("Inicie sesión.");actividad=DateTime.UtcNow;await accion();}
  finally{ocupado=false;}
 }
 private async Task Movimiento(string tipo)
 {
  var f=Ui.Formulario(this,tipo=="RETIRO"?"Retiro de efectivo":"Depósito de efectivo",("monto","Monto en quetzales enteros (ej. 239)","",false));if(f==null)return;
  decimal monto=Ui.Monto(f["monto"]);if(monto<=0||monto!=decimal.Truncate(monto)||monto>30000)throw new ArgumentException("Ingrese quetzales enteros entre 1 y 30000.");
  var desglose=Ui.Desglose(this,"Desglose de billetes");if(desglose==null)return;if(Billetes.Total(desglose)!=monto)throw new ArgumentException("El desglose no suma el monto ingresado.");
  if(MessageBox.Show(this,$"{tipo}: Q {monto:N2}\n"+string.Join(" · ",desglose.Where(x=>x.Value>0).Select(x=>$"Q {x.Key} × {x.Value}"))+"\n¿Confirmar?","Confirmar operación",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
  var r=await cliente.Enviar(tipo,new {monto,desglose});
  if(r.Codigo=="92"&&r.Datos.TryGetProperty("alternativa",out var alternativa)&&alternativa.ValueKind==JsonValueKind.Object)
  {
   var alterno=alternativa.EnumerateObject().ToDictionary(x=>int.Parse(x.Name),x=>x.Value.GetInt32());
   if(MessageBox.Show(this,"No hay billetes para su desglose. Alternativa:\n"+string.Join("\n",alterno.Select(x=>$"Q {x.Key} × {x.Value}"))+"\n¿Aceptar? Se volverán a validar las existencias.","Desglose alternativo",MessageBoxButton.YesNo)==MessageBoxResult.Yes)
   r=await cliente.Enviar(tipo,new {monto,desglose=alterno});else return;
  }
  if(Exito(r))Comprobante(r,$"{tipo}\nMonto: Q {monto:N2}");
 }
 private async Task Saldo()
 {
  var r=await cliente.Enviar("CONSULTA_SALDO");if(!Exito(r))return;
  var d=r.Datos;Comprobante(r,$"Saldo: Q {d.GetProperty("saldo").GetDecimal():N2}\nLímite diario: Q {d.GetProperty("limiteDiario").GetDecimal():N2}\nRetirado hoy: Q {d.GetProperty("retiradoHoy").GetDecimal():N2}\nDisponible del límite: Q {d.GetProperty("disponibleHoy").GetDecimal():N2}");
 }
 private async Task Historial()
 {
  var r=await cliente.Enviar("ULTIMAS_TRANSACCIONES");if(!Exito(r))return;
  var p=new StackPanel();p.Children.Add(Ui.Titulo("Últimas 5 transacciones"));
  var filas=r.Datos.EnumerateArray().Select(d=>new {Fecha=d.GetProperty("fecha").GetDateTime().ToString("yyyy-MM-dd HH:mm:ss"),Tipo=d.GetProperty("tipo").GetString(),Monto=d.GetProperty("monto").GetDecimal(),Cajero=d.GetProperty("cajero").GetString()}).ToArray();
  var tabla=Ui.Tabla(filas);tabla.MinHeight=220;p.Children.Add(tabla);p.Children.Add(Ui.Boton("Volver",MostrarMenu));Pantalla.Content=p;
 }
 private async Task CambiarPin()
 {
  var f=Ui.Formulario(this,"Cambiar PIN",("actual","PIN actual","",true),("nuevo","Nuevo PIN de 4 dígitos","",true),("confirmacion","Confirmar nuevo PIN","",true));if(f==null)return;
  if(f["nuevo"]!=f["confirmacion"]||f["nuevo"].Length!=4||!f["nuevo"].All(char.IsAsciiDigit))throw new ArgumentException("El nuevo PIN debe tener 4 dígitos y coincidir con su confirmación.");
  var r=await cliente.Enviar("CAMBIO_PIN",new {pinActual=f["actual"],pinNuevo=f["nuevo"]});if(Exito(r)){debeCambiarPin=false;Comprobante(r,"Su PIN se actualizó en todos los cajeros.");}
 }
 private void Comprobante(Mensaje r,string texto)
 {
  var p=new StackPanel();p.Children.Add(Ui.Titulo("Operación completada"));p.Children.Add(Ui.Texto(texto,20));
  if(r.Datos.TryGetProperty("idTransaccion",out var id))p.Children.Add(Ui.Texto("Transacción: "+id));
  if(r.Datos.TryGetProperty("fecha",out var fecha))p.Children.Add(Ui.Texto("Fecha del servidor: "+fecha.GetString()));
  if(r.Datos.TryGetProperty("saldo",out var saldo))p.Children.Add(Ui.Texto($"Saldo actual: Q {saldo.GetDecimal():N2}"));
  p.Children.Add(Ui.Texto($"Cajero: {config.CodigoCajero}\nTarjeta: **** {tarjeta[^4..]}\nReferencia: {r.IdSolicitud}"));
  p.Children.Add(Ui.Boton("Continuar",MostrarMenu));Pantalla.Content=Ui.Tarjeta(p);
 }
 private async Task ConsultarPendiente()
 {
  if(cliente.SolicitudIncierta==null){Ui.Mostrar(this,"Estado de operación","No hay una operación pendiente de consultar en esta instancia.");return;}
  if(cliente.Sesion==null){Ui.Mostrar(this,"Inicie sesión","Vuelva a ingresar con el mismo usuario antes de consultar la operación. También puede revisarla con administración.");return;}
  var r=await cliente.Enviar("ESTADO_SOLICITUD",new {idOriginal=cliente.SolicitudIncierta});
  if(Exito(r))
  {
   Ui.Mostrar(this,"Resultado confirmado",$"Código: {r.Datos.GetProperty("codigo").GetString()}\n{r.Datos.GetProperty("mensaje").GetString()}\n{r.Datos.GetProperty("datos").GetRawText()}");
   cliente.ConfirmarConsultaPendiente();MostrarMenu();
  }
 }
}
