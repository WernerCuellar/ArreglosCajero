using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CajeroRed.Aplicacion;
using CajeroRed.Infraestructura;
using CajeroRed.Presentacion;
using CajeroRed.Protocolo;
using Microsoft.Win32;

namespace CajeroRed.Servidor;

public partial class MainWindow : Window
{
 private BaseDatos bd=null!;private Administracion admin=null!;private ServidorTcp servidor=null!;
 private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(4)};
 private string pagina="Resumen",cajeroGrafica="ATM-01";private DataGrid? tabla;private TextBox? filtro;private StackPanel? metricas;private WrapPanel? graficas;
 private DatePicker? desde,hasta;private bool refrescando,cerrando;private int puerto=5000;
 private string cadena="Server=.\\SQLEXPRESS;Database=BD_CajeroRed_Fase2;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
  private static string CarpetaConfiguracion=>string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase)?AppContext.BaseDirectory:Path.GetDirectoryName(Environment.ProcessPath)??AppContext.BaseDirectory;
 private readonly string local=Path.Combine(CarpetaConfiguracion,"servidor.local.json");
 private readonly List<Window> tokens=[];
 public MainWindow()
 {
  InitializeComponent();Title="NETGEAR · Servidor 2.2.1";
   try
   {
    var args=Environment.GetCommandLineArgs();for(int i=1;i<args.Length-1;i++)if(args[i]=="--config")local=Path.GetFullPath(args[i+1]);
    var ruta=File.Exists(local)?local:Path.Combine(CarpetaConfiguracion,"servidor.json");if(File.Exists(ruta))cadena=JsonDocument.Parse(File.ReadAllText(ruta)).RootElement.GetProperty("cadenaSql").GetString()!;
   }catch(Exception e){Ui.Error(e);}
  InicializarServicios();MostrarLogin();ActualizarDirecciones();
  timer.Tick+=async(_,_)=>await Refrescar();
  Closing+=async(_,e)=>{if(cerrando)return;e.Cancel=true;cerrando=true;timer.Stop();foreach(var w in tokens.ToArray())w.Close();await servidor.DisposeAsync();_=Dispatcher.BeginInvoke(new Action(Close));};
 }
 private void InicializarServicios()
 {
  bd=new BaseDatos(cadena);admin=new Administracion(bd);servidor=new ServidorTcp(bd);
  servidor.Evento+=texto=>Dispatcher.BeginInvoke(()=>EstadoPie.Text=texto);
  servidor.TokenGenerado+=(c,t)=>Dispatcher.BeginInvoke(()=>MostrarToken(c,t));
 }
 private void MostrarLogin()
 {
  var p=new StackPanel(){MaxWidth=440,HorizontalAlignment=HorizontalAlignment.Left};
  p.Children.Add(Ui.Titulo("Administración de la red"));p.Children.Add(Ui.Texto("Ingrese con su usuario administrativo para supervisar y gestionar los cajeros."));
  p.Children.Add(Ui.Texto("Usuario"));var usuario=new TextBox(){Text="admin"};p.Children.Add(usuario);
  p.Children.Add(Ui.Texto("Contraseña"));
  var campo=new Grid();campo.ColumnDefinitions.Add(new ColumnDefinition());campo.ColumnDefinitions.Add(new ColumnDefinition(){Width=GridLength.Auto});
  var clave=new PasswordBox();var visible=new TextBox(){Visibility=Visibility.Collapsed};
  campo.Children.Add(clave);campo.Children.Add(visible);
  var ojo=new System.Windows.Controls.Primitives.ToggleButton(){Content="👁",Width=48,Margin=new Thickness(6,5,0,12),ToolTip="Mostrar contraseña"};
  System.Windows.Automation.AutomationProperties.SetName(ojo,"Mostrar contraseña");
  Grid.SetColumn(ojo,1);campo.Children.Add(ojo);p.Children.Add(campo);
  bool sincronizando=false;
  clave.PasswordChanged+=(_,_)=>{if(sincronizando)return;sincronizando=true;visible.Text=clave.Password;sincronizando=false;};
  visible.TextChanged+=(_,_)=>{if(sincronizando)return;sincronizando=true;clave.Password=visible.Text;sincronizando=false;};
  void Ocultar(){ojo.IsChecked=false;visible.Visibility=Visibility.Collapsed;clave.Visibility=Visibility.Visible;ojo.ToolTip="Mostrar contraseña";System.Windows.Automation.AutomationProperties.SetName(ojo,"Mostrar contraseña");}
  ojo.Checked+=(_,_)=>{clave.Visibility=Visibility.Collapsed;visible.Visibility=Visibility.Visible;ojo.ToolTip="Ocultar contraseña";System.Windows.Automation.AutomationProperties.SetName(ojo,"Ocultar contraseña");visible.Focus();visible.CaretIndex=visible.Text.Length;};
  ojo.Unchecked+=(_,_)=>{Ocultar();clave.Focus();};
  Deactivated+=(_,_)=>Ocultar();
  p.Children.Add(Ui.BotonAsync("Iniciar sesión",async()=>
  {
   bool valido;try{Ocultar();valido=await IniciarSesion(usuario.Text.Trim(),clave.Password);}finally{clave.Clear();visible.Clear();}
   if(!valido)throw new ArgumentException("Usuario o contraseña incorrectos.");
  }));
  p.Children.Add(Ui.Boton("Configurar SQL Server",()=>
  {
   var f=Ui.Formulario(this,"Conexión del servidor",("cadena","Cadena SQL (solo en el servidor)",cadena,false));
   if(f==null)return;cadena=f["cadena"];
   File.WriteAllText(local,JsonSerializer.Serialize(new {cadenaSql=cadena},new JsonSerializerOptions(){WriteIndented=true}));
   InicializarServicios();MostrarLogin();
  }));
  p.Children.Add(Ui.Texto("Primera instalación: siga LEEME_PRIMERO.md para restaurar el respaldo o crear una base nueva. Las credenciales de demostración están en el LEEME.",12));
  Contenido.Content=Ui.Tarjeta(p);
 }
 public async Task<bool> IniciarSesion(string usuario,string clave)
 {if(!await admin.Login(usuario,clave))return false;await AbrirDashboard();return true;}
 private async Task AbrirDashboard()
 {
  UsuarioActual.Text=admin.Usuario+" · "+admin.Rol;
  var param=await admin.Consultar("SELECT Valor FROM dbo.Parametro WHERE Clave='Puerto'");puerto=Convert.ToInt32(param.Rows[0][0]);
  string[] paginas=["Resumen","Cajeros","Usuarios","Transferencias","Transacciones","Depósitos","Retiros","Cambios de PIN","Alertas","Auditoría","Arqueo","Resumen diario","Bancos de la red","Configuración"];
  Menu.Children.Clear();foreach(var item in paginas)Menu.Children.Add(Ui.BotonAsync(item,()=>MostrarPagina(item)));
  ControlesServicio.Children.Clear();
  ControlesServicio.Children.Add(Ui.BotonAsync("Iniciar servicio",async()=>{if(servidor.Activo)return;var valor=await admin.Consultar("SELECT Valor FROM dbo.Parametro WHERE Clave='Puerto'");puerto=Convert.ToInt32(valor.Rows[0][0]);await servidor.Iniciar(puerto);await Refrescar();}));
  ControlesServicio.Children.Add(Ui.BotonAsync("Detener",async()=>{await servidor.DisposeAsync();await Refrescar();}));
  ActualizarDirecciones();
  await MostrarPagina("Resumen");timer.Start();
 }
 private void MostrarToken(string cajero,string token)
 {
  var p=new StackPanel(){Margin=new Thickness(28)};p.Children.Add(Ui.Titulo("Token del usuario"));
  p.Children.Add(Ui.Texto(cajero+" · Simulación del envío al celular"));p.Children.Add(Ui.Texto(token,42));p.Children.Add(Ui.Texto("Temporal y de un solo uso. Ingréselo en el cajero."));
  var w=new Window(){Owner=this,Title="Token del usuario · "+cajero,Width=430,SizeToContent=SizeToContent.Height,Content=p,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var t=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(120)};t.Tick+=(_,_)=>w.Close();w.Closed+=(_,_)=>{t.Stop();tokens.Remove(w);};tokens.Add(w);t.Start();w.Show();
 }
 private async Task MostrarPagina(string nombre)
 {
  pagina=nombre;tabla=null;metricas=null;graficas=null;filtro=null;desde=null;hasta=null;
  var p=new DockPanel();p.Children.Add(Encabezado(nombre));
  var acciones=new WrapPanel();DockPanel.SetDock(acciones,Dock.Top);p.Children.Add(acciones);
  if(nombre is "Transacciones" or "Depósitos" or "Retiros" or "Cambios de PIN" or "Auditoría" or "Resumen diario" or "Transferencias")
  {
   desde=new DatePicker(){SelectedDate=nombre is "Transferencias" or "Transacciones"?DateTime.Today.AddDays(-30):DateTime.Today,Width=130,Margin=new Thickness(4)};hasta=new DatePicker(){SelectedDate=DateTime.Today,Width=130,Margin=new Thickness(4)};
   acciones.Children.Add(Ui.Texto("Desde"));acciones.Children.Add(desde);acciones.Children.Add(Ui.Texto("Hasta"));acciones.Children.Add(hasta);
   acciones.Children.Add(Ui.BotonAsync("Aplicar fechas",Refrescar));
   if(nombre is "Transferencias" or "Transacciones")acciones.Children.Add(Ui.BotonAsync("Todo el historial",async()=>{desde.SelectedDate=new DateTime(1900,1,1);hasta.SelectedDate=DateTime.Today;await Refrescar();}));
  }
  AccionesInterbancarias(nombre,acciones);
  if(nombre=="Cajeros")AccionesCajero(acciones);
  if(nombre=="Usuarios")AccionesUsuario(acciones);
  if(nombre=="Alertas")acciones.Children.Add(Ui.BotonAsync("Marcar atendida",async()=>{var r=Seleccion();await admin.Accion("ATENDER_ALERTA",new {alertaId=r["Id"]});await Refrescar();}));
  if(nombre=="Configuración")acciones.Children.Add(Ui.BotonAsync("Modificar parámetro",async()=>
  {
   var r=Seleccion();var f=Ui.Formulario(this,"Modificar parámetro",("valor",r["Clave"].ToString()!,r["Valor"].ToString()!,false));if(f==null)return;
   await admin.Accion("PARAMETROS",new {clave=r["Clave"],valor=Ui.Monto(f["valor"])});
   if((string)r["Clave"]=="Puerto")Ui.Mostrar(this,"Puerto guardado","Detenga e inicie el servidor de nuevo para aplicar el puerto. Actualice los cajeros.");
   await Refrescar();
  }));
  acciones.Children.Add(Ui.BotonAsync("Actualizar",Refrescar));
  if(nombre!="Resumen")acciones.Children.Add(Ui.BotonAsync("Exportar Excel",Exportar));
  filtro=new TextBox(){MinWidth=180,MaxWidth=260,Margin=new Thickness(4),ToolTip="Filtrar por usuario, cajero o cualquier columna"};filtro.TextChanged+=(_,_)=>Filtrar();acciones.Children.Add(filtro);
  if(nombre=="Resumen")
  {
   var cuerpo=new StackPanel();metricas=new StackPanel();graficas=new WrapPanel();cuerpo.Children.Add(metricas);cuerpo.Children.Add(graficas);
   cuerpo.Children.Add(Ui.Texto("Actividad e indicadores de hoy. Fuente: fecha y hora de SQL Server. Actualización automática cada 4 segundos.",12));
   p.Children.Add(new ScrollViewer(){Content=cuerpo,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  }
  else
  {
   tabla=Ui.Tabla();if(nombre=="Cajeros")tabla.LoadingRow+=ColorearCajero;p.Children.Add(tabla);
  }
  Contenido.Content=p;
  // Si la página anterior todavía estaba consultando SQL, cargar esta apenas
  // termine; no dejar una tabla vacía esperando el siguiente tick del timer.
  while(refrescando)await Task.Delay(30);
  await Refrescar();
 }
 private UIElement Encabezado(string n)
 {
  var panel=new StackPanel();DockPanel.SetDock(panel,Dock.Top);panel.Children.Add(Ui.Titulo(n));
  string ayuda=n switch {"Cajeros"=>"Seleccione una fila para gestionar efectivo o enviar comandos. Verde: en línea; naranja: advertencia; rojo: falla.","Usuarios"=>"Seleccione una cuenta y abra Historial del usuario para ver sus movimientos, transferencias enviadas y recibidas.",
   "Transferencias"=>"Entre cuentas NETGEAR y otros bancos · una fila por envío · fecha, origen, destino, monto, estado y token. Use Todo el historial para ampliar las fechas.",
   "Transacciones"=>"Movimientos de todas las cuentas: retiros, depósitos y transferencias enviadas o recibidas. Las transferencias internas generan un movimiento por cada usuario.","Arqueo"=>"Cuadre desde la última inicialización: efectivo inicial + cargas + depósitos − retiros.","Configuración"=>"Parámetros guardados en SQL Server. La vigencia del token no puede superar 120 segundos.",_=>"Información centralizada de todos los cajeros · actualización cada 4 segundos."};
  panel.Children.Add(Ui.Texto(ayuda,12));return panel;
 }
 private DataRowView Seleccion()=>tabla?.SelectedItem as DataRowView??throw new ArgumentException("Seleccione una fila de la tabla.");
 private void AccionesCajero(WrapPanel p)
 {
  p.Children.Add(Ui.BotonAsync("Nuevo cajero",async()=>
  {
   var f=Ui.Formulario(this,"Registrar cajero",("cajero","Código único (ATM-09)","",false),("nombre","Nombre","",false),("ubicacion","Ubicación","",false));
   if(f!=null){await admin.Accion("ALTA_CAJERO",f);await Refrescar();}
  }));
  p.Children.Add(Ui.BotonAsync("Editar",async()=>{var r=Seleccion();var f=Ui.Formulario(this,"Editar cajero",("nombre","Nombre",(string)r["Nombre"],false),("ubicacion","Ubicación",(string)r["Ubicacion"],false));if(f==null)return;f["cajero"]=(string)r["Codigo"];await admin.Accion("EDITAR_CAJERO",f);await Refrescar();}));
  foreach(var (label,tipo) in new[]{("Inicializar hoy","INICIALIZAR"),("Agregar efectivo","CARGAR")})
  p.Children.Add(Ui.BotonAsync(label,async()=>
  {
   var c=(string)Seleccion()["Codigo"];var desglose=Ui.Desglose(this,label);if(desglose==null)return;
   if(MessageBox.Show(this,$"{c}: Q {Billetes.Total(desglose):N2}. ¿Confirmar?",label,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   await admin.Accion(tipo,new {cajero=c,desglose});await servidor.Comando(c,"ACTUALIZAR_INVENTARIO");await Refrescar();
  }));
  foreach(var (label,tipo) in new[]{("Habilitar","HABILITAR"),("Fuera de servicio","FUERA_DE_SERVICIO"),("Baja lógica","BAJA_CAJERO")})
  p.Children.Add(Ui.BotonAsync(label,async()=>
  {
   string c=(string)Seleccion()["Codigo"];
   if(MessageBox.Show(this,$"{label}: {c}. ¿Confirmar?",label,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   await admin.Accion(tipo,new {cajero=c});await servidor.Comando(c,tipo=="BAJA_CAJERO"?"FUERA_DE_SERVICIO":tipo);await Refrescar();
  }));
  p.Children.Add(Ui.BotonAsync("Enviar mensaje",async()=>{string c=(string)Seleccion()["Codigo"];var f=Ui.Formulario(this,"Mensaje al cajero",("texto","Texto","",false));if(f==null)return;await admin.Accion("MENSAJE",new {cajero=c,texto=f["texto"]});await servidor.Comando(c,"MENSAJE",f["texto"]);}));
 }
 private void AccionesUsuario(WrapPanel p)
 {
  p.Children.Add(Ui.BotonAsync("Asignar cuenta",async()=>{
   var r=Seleccion();var f=Ui.Formulario(this,"Número de cuenta",("cuenta","Cuenta única (1 a 20 dígitos)","",false));if(f==null)return;
   await servidor.Interbancario.AsignarCuenta(admin.Id!.Value,Convert.ToInt32(r["Id"]),f["cuenta"]);await Refrescar();
  }));
  p.Children.Add(Ui.BotonAsync("Crear usuario",async()=>
  {
   var f=Ui.Formulario(this,"Crear usuario",("nombre","Nombre completo","",false),("tarjeta","Tarjeta (16 dígitos)","",false),("pin","PIN (4 dígitos)","",true),("confirmacion","Confirmar PIN","",true),("saldo","Saldo inicial (sin separadores)","0",false),("limite","Límite diario","500",false));
   if(f==null)return;if(f["pin"]!=f["confirmacion"])throw new ArgumentException("Los PIN no coinciden.");
   await admin.Accion("CREAR_USUARIO",new {nombre=f["nombre"],tarjeta=f["tarjeta"],pin=f["pin"],saldo=Ui.Monto(f["saldo"]),limite=Ui.Monto(f["limite"])});await Refrescar();
  }));
  p.Children.Add(Ui.BotonAsync("Editar nombre",async()=>{var r=Seleccion();var f=Ui.Formulario(this,"Editar usuario",("nombre","Nombre",(string)r["Nombre"],false));if(f==null)return;await admin.Accion("EDITAR_USUARIO",new {usuarioId=r["Id"],nombre=f["nombre"]});await Refrescar();}));
  p.Children.Add(Ui.BotonAsync("Cambiar límite",async()=>{var r=Seleccion();var f=Ui.Formulario(this,"Cambiar límite",("limite","Nuevo límite (no reinicia lo retirado)",Convert.ToString(r["LimiteDiario"],System.Globalization.CultureInfo.InvariantCulture)!,false));if(f==null)return;await admin.Accion("CAMBIAR_LIMITE",new {usuarioId=r["Id"],limite=Ui.Monto(f["limite"])});await Refrescar();}));
  p.Children.Add(Ui.BotonAsync("Cambiar tarjeta",async()=>{var r=Seleccion();var f=Ui.Formulario(this,"Reemplazar tarjeta",("tarjeta","Nueva tarjeta (16 dígitos)","",false),("pin","PIN temporal (4 dígitos)","",true));if(f==null)return;await admin.Accion("CAMBIAR_TARJETA",new {usuarioId=r["Id"],tarjeta=f["tarjeta"],pin=f["pin"]});await Refrescar();}));
  p.Children.Add(Ui.BotonAsync("Restablecer PIN",async()=>{var r=Seleccion();var f=Ui.Formulario(this,"PIN temporal",("pin","PIN temporal; deberá cambiarlo al ingresar","",true));if(f==null)return;await admin.Accion("RESTABLECER_PIN",new {usuarioId=r["Id"],pin=f["pin"]});await Refrescar();}));
  foreach(var (label,tipo) in new[]{("Bloquear","BLOQUEAR"),("Desbloquear","DESBLOQUEAR"),("Baja lógica","BAJA_USUARIO")})
  p.Children.Add(Ui.BotonAsync(label,async()=>{var r=Seleccion();if(MessageBox.Show(this,$"{label}: {r["Nombre"]}. ¿Confirmar?",label,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;await admin.Accion(tipo,new {usuarioId=r["Id"]});await Refrescar();}));
  p.Children.Add(Ui.BotonAsync("Historial del usuario",async()=>
  {
   var r=Seleccion();var t=await admin.Consultar(ReportesMovimientos.PorUsuario,("@u",r["Id"]));
   var tarjetas=await admin.Consultar("SELECT '**** '+RIGHT(Numero,4) Tarjeta,Activa,Asignada,Baja FROM dbo.Tarjeta WHERE UsuarioId=@u ORDER BY Asignada DESC",("@u",r["Id"]));
   var limites=await admin.Consultar("SELECT Anterior,Nuevo,AdministradorId,Fecha FROM dbo.HistorialLimite WHERE UsuarioId=@u ORDER BY Fecha DESC",("@u",r["Id"]));
   var tabs=new TabControl();foreach(var (n,d) in new[]{("Movimientos",t),("Transferencias",await admin.Consultar("SELECT * FROM dbo.vw_TransferenciasUsuario WHERE UsuarioId=@u ORDER BY Fecha DESC",("@u",r["Id"]))),("Tarjetas",tarjetas),("Límites",limites)})tabs.Items.Add(new TabItem(){Header=n,Content=Ui.Tabla(d.DefaultView)});
   new Window(){Owner=this,Title="Historial · "+r["Nombre"],Width=1000,Height=500,Content=tabs,WindowStartupLocation=WindowStartupLocation.CenterOwner}.Show();
  }));
 }
 private async Task Refrescar()
 {
  if(refrescando||admin.Id==null)return;refrescando=true;
  string actual=pagina;
  try
  {
   ActualizarDirecciones();
   EstadoServidor.Text=servidor.Activo?$"● Servicio activo · {servidor.Conectados} cajeros conectados · puerto {puerto}":"● Servicio detenido";
   EstadoServidor.Foreground=servidor.Activo?Brushes.DarkGreen:Brushes.Firebrick;
   string sql=actual switch
   {
    "Resumen"=>"SELECT * FROM dbo.vw_ResumenDiario",
    "Bancos de la red"=>"SELECT CodigoBanco,NombreBanco,IP,Puerto,EsPropio,Confiable,Activo,LimiteDiario,CASE WHEN FechaUltimoReinicio=CAST(SYSDATETIME() AS date) THEN TransferidoHoy ELSE 0 END TransferidoHoy,LimiteDiario-CASE WHEN FechaUltimoReinicio=CAST(SYSDATETIME() AS date) THEN TransferidoHoy ELSE 0 END CupoDisponibleHoy FROM dbo.BancoRed ORDER BY EsPropio DESC,CodigoBanco",
    "Transferencias"=>ReportesMovimientos.Transferencias,
    "Cajeros"=>"SELECT Codigo,Estado,IP,Efectivo,B200,B100,B50,B20,B10,B5,B1,UltimoHeartbeat,UltimaTransaccion,Nombre,Ubicacion,Equipo,Version,Activo,Habilitado,Conectado,FechaInicializacion FROM dbo.vw_EstadoCajeros ORDER BY Codigo",
    "Usuarios"=>"SELECT v.*,CASE WHEN u.NumeroCuenta IS NULL THEN NULL ELSE '**** '+RIGHT(u.NumeroCuenta,4) END NumeroCuenta FROM dbo.vw_Usuarios v JOIN dbo.Usuario u ON u.Id=v.Id ORDER BY v.Id",
    "Transacciones"=>ReportesMovimientos.Movimientos,
    "Depósitos"=>"SELECT * FROM dbo.vw_Transacciones WHERE Tipo='DEPOSITO' AND Fecha>=@d AND Fecha<@h ORDER BY Fecha DESC",
    "Retiros"=>"SELECT t.*,ISNULL(r.RetiradoHoy,0) ConsumoHoy FROM dbo.vw_Transacciones t LEFT JOIN dbo.vw_RetiradoHoyPorUsuario r ON r.UsuarioId=t.UsuarioId WHERE t.Tipo='RETIRO' AND t.Fecha>=@d AND t.Fecha<@h ORDER BY t.Fecha DESC",
    "Cambios de PIN"=>"SELECT p.Id,u.Nombre,p.Cajero,p.Fecha,p.Origen,p.Resultado FROM dbo.CambioPin p JOIN dbo.Usuario u ON u.Id=p.UsuarioId WHERE p.Fecha>=@d AND p.Fecha<@h ORDER BY p.Fecha DESC",
    "Alertas"=>"SELECT Id,Tipo,Cajero,Descripcion,Fecha,Atendida,AdministradorId,FechaAtencion FROM dbo.Alerta ORDER BY Atendida,Fecha DESC",
    "Auditoría"=>"SELECT * FROM dbo.Bitacora WHERE Fecha>=@d AND Fecha<@h ORDER BY Fecha DESC",
    "Arqueo"=>"SELECT * FROM dbo.vw_Arqueo",
    "Resumen diario"=>"SELECT CAST(Fecha AS date) Fecha,Cajero,Tipo,CodigoRespuesta Resultado,COUNT(*) Operaciones,SUM(Monto) Total FROM dbo.Transaccion WHERE Fecha>=@d AND Fecha<@h GROUP BY CAST(Fecha AS date),Cajero,Tipo,CodigoRespuesta ORDER BY Fecha DESC,Cajero",
    _=>"SELECT Clave,Valor FROM dbo.Parametro ORDER BY Clave"
   };
   var t=await admin.Consultar(sql,("@d",desde?.SelectedDate??DateTime.Today),("@h",(hasta?.SelectedDate??DateTime.Today).AddDays(1)));
   if(actual!=pagina)return;
   if(actual=="Resumen")await DibujarResumen(t);
   else if(tabla!=null)
   {
    var seleccionado=(tabla.SelectedItem as DataRowView)?[0];tabla.ItemsSource=t.DefaultView;Filtrar();
    if(seleccionado!=null)foreach(var r in t.DefaultView.Cast<DataRowView>())if(Equals(r[0],seleccionado)){tabla.SelectedItem=r;break;}
    ActualizarTotalesFiltrados();
   }
   if(actual=="Configuración"){var r=t.AsEnumerable().FirstOrDefault(r=>(string)r["Clave"]=="Puerto");if(r!=null&&!servidor.Activo)puerto=Convert.ToInt32(r["Valor"]);}
  }
  catch(Exception e){EstadoPie.Text="No se pudo actualizar: "+e.Message;}
  finally{refrescando=false;}
 }
 private void ColorearCajero(object? sender,DataGridRowEventArgs e)
 {
  if(e.Row.Item is not DataRowView r)return;
  e.Row.Foreground=(string)r["Estado"] switch {"En línea"=>Brushes.DarkGreen,"Efectivo bajo" or "Sin inicializar"=>Brushes.DarkOrange,_=>Brushes.Firebrick};
 }
 private void Filtrar()
 {
  if(tabla?.ItemsSource is not DataView v||filtro==null)return;
  string s=filtro.Text.Replace("'","''").Replace("[","[[]").Replace("%","[%]").Replace("*","[*]");
  v.RowFilter=string.IsNullOrWhiteSpace(s)?"":string.Join(" OR ",v.Table!.Columns.Cast<DataColumn>().Select(c=>$"CONVERT([{c.ColumnName}], 'System.String') LIKE '%{s}%'"));
  ActualizarTotalesFiltrados();
 }
 private void ActualizarTotalesFiltrados()
 {
  if(tabla?.ItemsSource is not DataView v)return;
  string extra="";
  if(pagina is "Depósitos" or "Retiros")
  {
   var aprobados=v.Cast<DataRowView>().Where(r=>Convert.ToString(r["Resultado"])=="00").ToArray();
   decimal total=aprobados.Sum(r=>Convert.ToDecimal(r["Monto"]));int usuarios=aprobados.Select(r=>r["UsuarioId"]).Distinct().Count();
   extra=$" · Aprobado Q {total:N2} · Promedio por usuario Q {(usuarios>0?total/usuarios:0):N2}";
  }
  if(pagina=="Transferencias")
  {
   var filas=v.Cast<DataRowView>().ToArray();var aprobadas=filas.Where(r=>(string)r["Estado"]=="APROBADA").ToArray();
   extra=$" · {aprobadas.Length} aprobadas · Total aprobado Q {aprobadas.Sum(r=>Convert.ToDecimal(r["Monto"])):N2} · {filas.Count(r=>(string)r["Estado"] is "PENDIENTE" or "RESERVADA" or "INCIERTA")} pendientes";
  }
  EstadoPie.Text=$"{v.Count} registros filtrados · actualizado {DateTime.Now:HH:mm:ss}"+extra;
 }
 private void ActualizarDirecciones()
 {
  try
  {
   var ips=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
    .Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up)
    .Where(n=>n.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
    .Where(n=>n.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
    .SelectMany(n=>n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254.")).Select(a=>$"{n.Name}: {a.Address}")).ToArray();
   Direcciones.Text=(ips.Length==0?"Sin IPv4 de red disponible":string.Join(" · ",ips))+$" · TCP {puerto}";
   Direcciones.ToolTip="Use la IP del adaptador de la red de los cajeros. Mostrar la IP no crea una reserva DHCP.";
  }
  catch(System.Net.NetworkInformation.NetworkInformationException){Direcciones.Text="No se pudieron consultar los adaptadores.";}
 }
 private async Task Exportar()
 {
  if(tabla?.ItemsSource is not DataView v)return;
  var d=new SaveFileDialog(){Filter="Libro de Excel (*.xlsx)|*.xlsx",FileName=pagina.Replace(" ","_")+"_"+DateTime.Today.ToString("yyyyMMdd")+".xlsx"};
  if(d.ShowDialog(this)!=true)return;
  // ZipArchiveMode.Create requiere un destino nuevo: reemplazo mediante temporal y movimiento atómico local.
  string temp=d.FileName+".tmp";if(File.Exists(temp))File.Delete(temp);
  Exportador.Excel(v.ToTable(),temp);File.Move(temp,d.FileName,true);await admin.RegistrarExportacion(pagina);Ui.Mostrar(this,"Reporte exportado",d.FileName);
 }
 private async Task DibujarResumen(DataTable t)
 {
  if(metricas==null||graficas==null)return;
  var tarjetas=new WrapPanel();var r=t.Rows[0];
  var etiquetas=new Dictionary<string,string>{{"CajerosEnLinea","Cajeros en línea"},{"TotalCajeros","Cajeros registrados"},{"EfectivoRed","Efectivo de la red"},{"TotalRetirado","Total retirado hoy"},{"TotalDepositado","Total depositado hoy"},{"PromedioDepositadoPorUsuario","Promedio depositado por usuario"},{"Transacciones","Operaciones del día"},{"Rechazadas","Operaciones rechazadas"},{"CambiosPin","Cambios de PIN"},{"AlertasActivas","Alertas activas"}};
  foreach(DataColumn c in t.Columns)
  {
   bool dinero=c.ColumnName is "EfectivoRed" or "TotalRetirado" or "TotalDepositado" or "PromedioDepositadoPorUsuario";
   var p=new StackPanel(){Width=190};p.Children.Add(Ui.Texto(etiquetas.GetValueOrDefault(c.ColumnName,c.ColumnName),12));
   p.Children.Add(Ui.Texto((dinero?"Q ":"")+(r[c] is DBNull?"0":Convert.ToDecimal(r[c]).ToString(dinero?"N2":"N0")),24));var b=Ui.Tarjeta(p);b.Margin=new Thickness(0,0,12,12);tarjetas.Children.Add(b);
  }
  metricas.Children.Clear();metricas.Children.Add(tarjetas);
  metricas.Children.Add(Ui.BotonAsync("Ver transferencias entre cuentas y bancos",()=>MostrarPagina("Transferencias")));
  var acceso=await admin.Consultar("SELECT TOP(1) u.Nombre,s.Inicio,s.Cajero FROM dbo.Sesion s JOIN dbo.Usuario u ON u.Id=s.UsuarioId WHERE s.Resultado='00' ORDER BY s.Inicio DESC");
  var cambios=await admin.Consultar("SELECT COUNT(DISTINCT UsuarioId) Usuarios FROM dbo.CambioPin WHERE Resultado='00' AND Origen='USUARIO' AND Fecha>=CAST(SYSDATETIME() AS date)");
  metricas.Children.Add(Ui.Texto("Usuarios que cambiaron PIN hoy: "+cambios.Rows[0][0]+(acceso.Rows.Count==0?" · Sin accesos de usuarios todavía.":$" · Último acceso: {acceso.Rows[0]["Nombre"]} · {acceso.Rows[0]["Inicio"]:yyyy-MM-dd HH:mm:ss} · {acceso.Rows[0]["Cajero"]}"),12));
  var porHora=await admin.Consultar("SELECT DATEPART(hour,Fecha) Hora,SUM(CASE WHEN Tipo='RETIRO' THEN Monto ELSE 0 END) Retiros,SUM(CASE WHEN Tipo='DEPOSITO' THEN Monto ELSE 0 END) Depositos FROM dbo.Transaccion WHERE CodigoRespuesta='00' AND Fecha>=CAST(SYSDATETIME() AS date) GROUP BY DATEPART(hour,Fecha) ORDER BY Hora");
  var porCajero=await admin.Consultar("SELECT Cajero,SUM(Monto) Monto FROM dbo.Transaccion WHERE CodigoRespuesta='00' AND Fecha>=CAST(SYSDATETIME() AS date) GROUP BY Cajero");
  var inventario=await admin.Consultar("SELECT Cajero,Denominacion,Cantidad FROM dbo.InventarioCajero ORDER BY Cajero,Denominacion DESC");
  if(pagina!="Resumen"||graficas==null)return;
  graficas.Children.Clear();graficas.Children.Add(Grafica("Retiros / depósitos por hora",porHora));graficas.Children.Add(Grafica("Montos del día por cajero",porCajero));
  var efectivo=new StackPanel(){Width=420};efectivo.Children.Add(Ui.Texto("Billetes por denominación",18));var combo=new ComboBox(){ItemsSource=inventario.AsEnumerable().Select(x=>(string)x["Cajero"]).Distinct().ToArray()};efectivo.Children.Add(combo);var zona=new ContentControl();efectivo.Children.Add(zona);combo.SelectionChanged+=(_,_)=>
   {cajeroGrafica=(string?)combo.SelectedItem??"ATM-01";var dt=inventario.Clone();foreach(var f in inventario.AsEnumerable().Where(x=>(string)x["Cajero"]==cajeroGrafica))dt.ImportRow(f);dt.Columns.Remove("Cajero");zona.Content=Grafica("Inventario · "+combo.SelectedItem,dt);};combo.SelectedItem=cajeroGrafica;if(combo.SelectedIndex<0)combo.SelectedIndex=0;graficas.Children.Add(Ui.Tarjeta(efectivo));
 }
 private static UIElement Grafica(string titulo,DataTable datos)
 {
  var p=new StackPanel(){Width=380,Margin=new Thickness(0,0,16,12)};p.Children.Add(Ui.Texto(titulo,18));
  if(datos.Rows.Count==0){p.Children.Add(Ui.Texto("Sin movimientos todavía."));return Ui.Tarjeta(p);}
  decimal max=datos.AsEnumerable().SelectMany(r=>datos.Columns.Cast<DataColumn>().Skip(1).Select(c=>Convert.ToDecimal(r[c]))).DefaultIfEmpty(1).Max();
  foreach(DataRow r in datos.Rows)
  {
   p.Children.Add(Ui.Texto(r[0].ToString()!,12));
   foreach(DataColumn c in datos.Columns.Cast<DataColumn>().Skip(1))
   {decimal valor=Convert.ToDecimal(r[c]);p.Children.Add(Ui.Texto($"{c.ColumnName}: {valor:N0}",11));p.Children.Add(new Border(){Background=c.Ordinal==1?new SolidColorBrush(Color.FromRgb(36,95,212)):Brushes.SeaGreen,Height=12,Width=Math.Max(2,(double)(valor/Math.Max(1,max))*340),HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(3)});}
  }
  return Ui.Tarjeta(p);
 }
}
