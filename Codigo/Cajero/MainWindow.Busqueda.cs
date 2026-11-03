using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CajeroRed.Aplicacion;
using CajeroRed.Presentacion;

namespace CajeroRed.Cajero;
public partial class MainWindow
{
 private void BuscarServidor()
 {
  if(cliente.Sesion!=null){Aviso.Text="Cierre sesión antes de cambiar de servidor.";return;}
  var w=new Window(){Owner=this,Title="Buscar y configurar servidor NETGEAR",Width=760,Height=700,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new DockPanel(){Margin=new Thickness(22)};
  var arriba=new StackPanel();DockPanel.SetDock(arriba,Dock.Top);panel.Children.Add(arriba);
  arriba.Children.Add(Ui.Titulo("Conectar cajero a NETGEAR"));
  arriba.Children.Add(Ui.Texto("Conecte esta PC a la misma red del servidor. El administrador debe pulsar Iniciar servicio."));
  var estado=Ui.Texto("Pulse Escanear. También puede escribir una IP manualmente.",12);arriba.Children.Add(estado);
  var grid=Ui.Tabla();grid.MinHeight=130;
  using var finBusqueda=new CancellationTokenSource();
  var escanear=Ui.BotonAsync("Escanear servidores",async()=>
  {
   estado.Text="Buscando en la red local…";
   try
   {
    var servidores=await DescubridorServidores.Escanear(config.Direccion,config.Puerto,finBusqueda.Token);
    if(!w.IsVisible)return;
    grid.ItemsSource=servidores;
    estado.Text=servidores.Count==0?"No se encontró NETGEAR. Revise cable/Wi-Fi, firewall y servicio; o escriba la IP.":$"{servidores.Count} servidor(es) encontrado(s). Seleccione el suyo.";
   }
   catch(OperationCanceledException){}
   catch(Exception e){if(w.IsVisible)estado.Text="No se pudo escanear: "+e.Message;}
  });arriba.Children.Add(escanear);
  var abajo=new StackPanel();DockPanel.SetDock(abajo,Dock.Bottom);panel.Children.Add(abajo);
  abajo.Children.Add(Ui.Texto("IP o nombre del servidor"));var ip=new TextBox(){Text=config.Direccion};abajo.Children.Add(ip);
  var fila=new WrapPanel();abajo.Children.Add(fila);
  fila.Children.Add(Ui.Texto("Puerto"));var port=Ui.EntradaNumerica(5,config.Puerto.ToString());port.Width=90;fila.Children.Add(port);
  fila.Children.Add(Ui.Texto("Código asignado"));var codigo=new TextBox(){Text=config.CodigoCajero,Width=160,Margin=new Thickness(8,4,0,4)};fila.Children.Add(codigo);
  abajo.Children.Add(Ui.Texto("Cada PC debe usar un código distinto, registrado por el administrador (ATM-01, ATM-02…).",12));
  var error=Ui.Texto("",12);error.Foreground=Brushes.Firebrick;abajo.Children.Add(error);
  grid.SelectionChanged+=(_,_)=>{
   if(grid.SelectedItem is DescubridorServidores.Servidor s){ip.Text=s.IP;port.Text=s.Puerto.ToString();}
  };
  abajo.Children.Add(Ui.Boton("Guardar y conectar",()=>
  {
   try
   {
    if(!int.TryParse(port.Text,out int numero))throw new ArgumentException("Puerto inválido.");
    var nueva=config with {Direccion=ip.Text.Trim(),Puerto=numero,CodigoCajero=codigo.Text.Trim()};
    ValidarConfig(nueva);GuardarConfiguracion(nueva);w.Close();
   }
   catch(Exception e){error.Text="No se pudo guardar: "+e.Message;}
  }));
  panel.Children.Add(grid);w.Content=panel;
  w.Closed+=(_,_)=>finBusqueda.Cancel();
  w.ShowDialog();
 }
 private void GuardarConfiguracion(Configuracion nueva)
 {
  ValidarConfig(nueva);
  string texto=System.Text.Json.JsonSerializer.Serialize(nueva,new System.Text.Json.JsonSerializerOptions(CajeroRed.Protocolo.Mensaje.Json){WriteIndented=true});
  try{System.IO.File.WriteAllText(ruta,texto);}
  catch(Exception e) when(e is System.IO.IOException or UnauthorizedAccessException)
  {System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(rutaRespaldo)!);System.IO.File.WriteAllText(rutaRespaldo,texto);}
  if(rutaRespaldo!=ruta&&System.IO.File.Exists(rutaRespaldo))System.IO.File.WriteAllText(rutaRespaldo,texto);
  config=nueva;configValida=true;LimpiarSesion("Configuración guardada. Conectando…");AplicarConfig();
  cliente.ReconectarAhora();if(inicioAutomatico)cliente.Iniciar();
 }
}
