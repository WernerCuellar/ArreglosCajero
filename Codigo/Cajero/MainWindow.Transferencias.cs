using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CajeroRed.Presentacion;
namespace CajeroRed.Cajero;
public partial class MainWindow
{
 private sealed record BancoDestino(string Codigo,string Nombre,bool Propio)
 {
  public override string ToString()=>Nombre+(Propio?" · cuentas NETGEAR":" · otro banco")+" ("+Codigo+")";
 }
 private async Task TransferirDinero()
 {
  int version=generacion;string? sesion=cliente.Sesion;ExigirMovimiento(version,sesion);
  var listado=await cliente.Enviar("BANCOS_TRANSFERENCIA");
  if(!SigueVigente(version,sesion)||!Exito(listado))return;
  var bancos=listado.Datos.EnumerateArray().Select(b=>new BancoDestino(b.GetProperty("codigoBanco").GetString()!,b.GetProperty("nombreBanco").GetString()!,b.GetProperty("esPropio").GetBoolean())).ToArray();
  if(bancos.Length==0){Aviso.Text="No hay bancos configurados. Consulte al administrador.";return;}
  BancoDestino? elegido=null;string cuenta="",titular="";
  var w=new Window(){Owner=this,Title="Transferir dinero · destinatario",Width=530,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel(){Margin=new Thickness(24)};
  panel.Children.Add(Ui.Titulo("¿A quién desea enviar dinero?"));
  panel.Children.Add(Ui.Texto("Seleccione NETGEAR para otra cuenta de nuestro banco, u otro banco de la lista."));
  var combo=new ComboBox(){ItemsSource=bancos,SelectedIndex=0};panel.Children.Add(combo);
  panel.Children.Add(Ui.Texto("Cuenta o tarjeta del destinatario"));var entrada=Ui.EntradaNumerica(20);panel.Children.Add(entrada);
  var aviso=Ui.Texto("",12);aviso.Foreground=Brushes.Firebrick;panel.Children.Add(aviso);
  panel.Children.Add(Ui.BotonAsync("Verificar destinatario",async()=>
  {
   try
   {
    ExigirMovimiento(version,sesion);
    var banco=(BancoDestino)combo.SelectedItem;string numero=entrada.Text;
    if(numero.Length is <1 or >20||!numero.All(char.IsAsciiDigit))throw new ArgumentException("Ingrese la cuenta o tarjeta, solo dígitos.");
    var r=await cliente.Enviar("VERIFICAR_DESTINO",new {bancoDestino=banco.Codigo,cuentaDestino=numero});
    if(!w.IsVisible||!SigueVigente(version,sesion))return;
    if(!ReferenceEquals(combo.SelectedItem,banco)||entrada.Text!=numero){aviso.Text="La selección cambió; verifique de nuevo.";return;}
    if(r.Codigo!="00"){aviso.Text=r.MensajeTexto;return;}
    elegido=banco;cuenta=numero;titular=r.Datos.GetProperty("nombreTitular").GetString()!;
    w.DialogResult=true;
   }
   catch(Exception e){if(w.IsVisible)aviso.Text=e.Message;}
  }));
  panel.Children.Add(Ui.Boton("Cancelar",()=>w.DialogResult=false));w.Content=panel;PrepararFormulario(w);
  if(w.ShowDialog()!=true||elegido==null||!SigueVigente(version,sesion))return;
  var f=Ui.FormularioValidado(this,"Transferir a "+titular,
   [("monto",$"Monto para {titular} · {elegido.Nombre} · cuenta **** {cuenta[^Math.Min(4,cuenta.Length)..]}","",false),("concepto","Concepto (opcional)","Transferencia",false)],
   valores=>{
    ExigirMovimiento(version,sesion);
    if(Ui.Monto(valores["monto"])<=0)throw new ArgumentException("Ingrese un monto mayor que cero.");
    if(valores["concepto"].Length>200)throw new ArgumentException("Concepto: máximo 200 caracteres.");
   },PrepararFormulario,"Continuar");
  if(f==null)return;
  decimal monto=Ui.Monto(f["monto"]);
  if(!Confirmar("Confirmar transferencia",$"Destinatario: {titular}\nBanco: {elegido.Nombre}\nCuenta: **** {cuenta[^Math.Min(4,cuenta.Length)..]}\nMonto: Q {monto:N2}\nConcepto: {f["concepto"]}",version,sesion))return;
  ExigirMovimiento(version,sesion);
  var resultado=await cliente.Enviar("TRANSFERIR",new {bancoDestino=elegido.Codigo,cuentaDestino=cuenta,nombreDestino=titular,monto,concepto=f["concepto"]});
  if(!SigueVigente(version,sesion))return;
  if(Exito(resultado))Comprobante(resultado,$"TRANSFERENCIA\nDestinatario: {titular}\nBanco: {elegido.Nombre}\nMonto: Q {monto:N2}");
  ActualizarBloqueo();
 }
 private async Task HistorialTransferencias()
 {
  int version=generacion;string? sesion=cliente.Sesion;
  var r=await cliente.Enviar("HISTORIAL_TRANSFERENCIAS");
  if(!SigueVigente(version,sesion)||!Exito(r))return;
  var filas=r.Datos.EnumerateArray().Select(d=>new {
   Fecha=d.GetProperty("fecha").GetDateTime().ToString("yyyy-MM-dd HH:mm:ss"),
   Direccion=d.GetProperty("direccion").GetString(),
   BancoOrigen=d.GetProperty("bancoOrigen").GetString(),BancoDestino=d.GetProperty("bancoDestino").GetString(),
   Origen=d.GetProperty("nombreOrigen").GetString(),Destino=d.GetProperty("nombreDestino").GetString(),
   Monto=d.GetProperty("monto").GetDecimal(),Estado=d.GetProperty("estado").GetString(),Token=d.GetProperty("tokenTransferencia").GetString()
  }).ToArray();
  var panel=new StackPanel();panel.Children.Add(Ui.Titulo("Transferencias enviadas y recibidas"));
  panel.Children.Add(Ui.Texto("Los abonos recibidos se incorporan al saldo de su cuenta. Si hay un envío pendiente, consulte su referencia."));
  panel.Children.Add(Ui.Tabla(filas));panel.Children.Add(Ui.Boton("Volver",MostrarMenu));Pantalla.Content=panel;
 }
}
