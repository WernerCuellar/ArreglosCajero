using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CajeroRed.Presentacion;
using CajeroRed.Protocolo;
using Microsoft.Win32;

namespace CajeroRed.Cajero;

public partial class MainWindow
{
 private void ExigirSesion(int version,string? sesion)
 {
  if(!SigueVigente(version,sesion))throw new InvalidOperationException("La sesión terminó o el cajero quedó fuera de servicio.");
 }
 private void ExigirMovimiento(int version,string? sesion)
 {
  ExigirSesion(version,sesion);
  if(cliente.MovimientosBloqueados)throw new InvalidOperationException("Aclare la operación pendiente antes de enviar dinero.");
 }
 private bool Confirmar(string titulo,string mensaje,int version,string? sesion)
 {
  ExigirSesion(version,sesion);
  var w=new Window(){Owner=this,Title=titulo,Width=500,SizeToContent=SizeToContent.Height,MaxHeight=720,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel(){Margin=new Thickness(24)};panel.Children.Add(Ui.Titulo(titulo));panel.Children.Add(Ui.Texto(mensaje));
  panel.Children.Add(Ui.Boton("Confirmar",()=>{if(SigueVigente(version,sesion))w.DialogResult=true;else w.DialogResult=false;}));
  panel.Children.Add(Ui.Boton("Cancelar",()=>w.DialogResult=false));w.Content=new ScrollViewer(){Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  PrepararFormulario(w);
  return w.ShowDialog()==true&&SigueVigente(version,sesion);
 }
 private async Task Movimiento(string tipo)
 {
  int version=generacion;string? sesion=cliente.Sesion;ExigirMovimiento(version,sesion);
  var campos=new List<(string,string,string,bool)>{("monto","Monto en quetzales enteros","",false)};
  campos.AddRange(Billetes.Denominaciones.Select(d=>(d.ToString(),$"Cantidad de billetes de Q {d}","0",false)));
  Dictionary<int,int> Desglose(Dictionary<string,string> f)=>Billetes.Denominaciones.ToDictionary(d=>d,d=>
   int.TryParse(f[d.ToString()],out int n)&&n>=0&&n<=30000?n:throw new ArgumentException("Ingrese cantidades enteras entre 0 y 30000."));
  var f=Ui.FormularioValidado(this,tipo=="RETIRO"?"Retiro de efectivo":"Depósito de efectivo",campos.ToArray(),v=>
  {
   ExigirMovimiento(version,sesion);decimal cantidad=Ui.Monto(v["monto"]);
   if(cantidad is <=0 or >30000||cantidad!=decimal.Truncate(cantidad))throw new ArgumentException("Ingrese quetzales enteros entre 1 y 30000.");
   if(Billetes.Total(Desglose(v))!=cantidad)throw new ArgumentException("El desglose debe sumar exactamente el monto indicado.");
  },PrepararFormulario,"Continuar");
  if(f==null)return;ExigirMovimiento(version,sesion);
  decimal monto=Ui.Monto(f["monto"]);var desglose=Desglose(f);
  if(!Confirmar("Confirmar "+tipo.ToLowerInvariant(),$"Q {monto:N2}\n"+string.Join("\n",desglose.Where(x=>x.Value>0).Select(x=>$"Q {x.Key} × {x.Value}")),version,sesion))return;
  ExigirMovimiento(version,sesion);
  var r=await cliente.Enviar(tipo,new {monto,desglose});
  if(!SigueVigente(version,sesion))return;
  if(r.Codigo=="92"&&r.Datos.TryGetProperty("alternativa",out var alternativa)&&alternativa.ValueKind==JsonValueKind.Object)
  {
   var alterno=alternativa.EnumerateObject().ToDictionary(x=>int.Parse(x.Name),x=>x.Value.GetInt32());
   if(!Confirmar("Desglose alternativo","El servidor propone:\n"+string.Join("\n",alterno.Select(x=>$"Q {x.Key} × {x.Value}"))+"\nLas existencias se validarán nuevamente.",version,sesion))return;
   ExigirMovimiento(version,sesion);r=await cliente.Enviar(tipo,new {monto,desglose=alterno});
   if(!SigueVigente(version,sesion))return;
  }
  if(Exito(r))Comprobante(r,$"{tipo}\nMonto: Q {monto:N2}");
  ActualizarBloqueo();
 }
 private async Task Saldo()
 {
  int version=generacion;string? sesion=cliente.Sesion;
  var r=await cliente.Enviar("CONSULTA_SALDO");if(!SigueVigente(version,sesion)||!Exito(r))return;
  var d=r.Datos;decimal saldo=d.GetProperty("saldo").GetDecimal(),disponible=d.GetProperty("disponibleHoy").GetDecimal();
  Comprobante(r,$"CONSULTA DE SALDO\nSaldo actual: Q {saldo:N2}\nLímite diario: Q {d.GetProperty("limiteDiario").GetDecimal():N2}\nRetirado hoy: Q {d.GetProperty("retiradoHoy").GetDecimal():N2}\nDisponible del límite diario: Q {disponible:N2}\nMáximo según saldo y límite: Q {Math.Max(0,Math.Min(saldo,disponible)):N2}\nEl retiro también depende de los billetes del cajero.");
 }
 private async Task Historial()
 {
  int version=generacion;string? sesion=cliente.Sesion;
  var r=await cliente.Enviar("ULTIMAS_TRANSACCIONES");if(!SigueVigente(version,sesion)||!Exito(r))return;
  var p=new StackPanel();p.Children.Add(Ui.Titulo("Últimas 5 transacciones"));
  var filas=r.Datos.EnumerateArray().Select(d=>new {Fecha=d.GetProperty("fecha").GetDateTime().ToString("yyyy-MM-dd HH:mm:ss"),Tipo=d.GetProperty("tipo").GetString(),Monto=d.GetProperty("monto").GetDecimal(),Cajero=d.GetProperty("cajero").GetString()}).ToArray();
  var tabla=Ui.Tabla(filas);tabla.MinHeight=220;p.Children.Add(tabla);p.Children.Add(Ui.Boton("Volver",MostrarMenu));Pantalla.Content=p;
 }
 private async Task CambiarPin()
 {
  int version=generacion;string? sesion=cliente.Sesion;ExigirSesion(version,sesion);
  var f=Ui.FormularioValidado(this,"Cambiar PIN",[("actual","PIN actual","",true),("nuevo","Nuevo PIN","",true),("confirmacion","Confirmar nuevo PIN","",true)],v=>
  {
   ExigirSesion(version,sesion);
   if(new[]{"actual","nuevo","confirmacion"}.Any(k=>v[k].Length!=4||!v[k].All(char.IsAsciiDigit)))throw new ArgumentException("Los tres campos requieren cuatro dígitos.");
   if(v["nuevo"]!=v["confirmacion"])throw new ArgumentException("El nuevo PIN y su confirmación no coinciden.");
   if(v["actual"]==v["nuevo"])throw new ArgumentException("El nuevo PIN debe ser distinto al actual.");
  },PrepararFormulario);
  if(f==null)return;ExigirSesion(version,sesion);
  var r=await cliente.Enviar("CAMBIO_PIN",new {pinActual=f["actual"],pinNuevo=f["nuevo"]});
  if(!SigueVigente(version,sesion))return;
  if(Exito(r)){debeCambiarPin=false;Comprobante(r,"CAMBIO DE PIN\nSu PIN se actualizó en todos los cajeros.");}
 }
 private void Comprobante(Mensaje r,string texto)
 {
  int version=generacion;string? sesion=cliente.Sesion;
  var lineas=new List<string>{"COMPROBANTE",texto};
  if(r.Datos.TryGetProperty("tokenTransferencia",out var token))lineas.Add("Token de transferencia: "+token.GetString());
  if(r.Datos.TryGetProperty("nombreDestino",out var destinatario))lineas.Add("Destinatario: "+destinatario.GetString());
  if(r.Datos.TryGetProperty("bancoDestino",out var banco))lineas.Add("Banco destino: "+banco.GetString());
  if(r.Datos.TryGetProperty("cuentaDestino",out var cuenta))lineas.Add("Cuenta destino: "+cuenta.GetString());
  if(r.Datos.TryGetProperty("idTransaccion",out var id))lineas.Add("Transacción: "+id);
  if(r.Datos.TryGetProperty("fecha",out var fecha))lineas.Add("Fecha del servidor: "+fecha.GetString());
  if(r.Datos.TryGetProperty("saldo",out var saldo))lineas.Add($"Saldo actual: Q {saldo.GetDecimal():N2}");
  lineas.Add($"Cajero: {config.CodigoCajero}");lineas.Add("Tarjeta: **** "+(tarjeta.Length>=4?tarjeta[^4..]:"----"));lineas.Add("Referencia: "+r.IdSolicitud);
  var copia=lineas.ToArray();var p=new StackPanel();p.Children.Add(Ui.Titulo("Operación completada"));
  p.Children.Add(Ui.Texto(string.Join("\n",copia),17));
  p.Children.Add(Ui.Boton("Guardar comprobante en PDF",()=>
  {
   if(!SigueVigente(version,sesion))return;
   var dialogo=new SaveFileDialog(){Filter="Comprobante PDF (*.pdf)|*.pdf",DefaultExt=".pdf",AddExtension=true,FileName="NETGEAR_"+r.IdSolicitud+".pdf"};
   if(dialogo.ShowDialog(this)!=true||!SigueVigente(version,sesion))return;
   try{ComprobantePdf.Guardar(dialogo.FileName,copia);Aviso.Text="Comprobante guardado. La operación ya estaba confirmada.";}
   catch(Exception e){Aviso.Text="La operación está confirmada, pero no se pudo guardar el PDF. "+e.Message;}
  }));
  p.Children.Add(Ui.Boton("Continuar",MostrarMenu));Pantalla.Content=Ui.Tarjeta(p);
 }
 private async Task ConsultarPendiente()
 {
  string? referencia=cliente.SolicitudIncierta;
  if(referencia==null){Aviso.Text=cliente.ErrorRecuperacion??"No hay operaciones pendientes para este usuario y conexión.";return;}
  int version=generacion;string? sesion=cliente.Sesion;
  var r=await cliente.Enviar(cliente.TipoPendiente=="TRANSFERIR"?"ESTADO_TRANSFERENCIA":"ESTADO_SOLICITUD",new {idOriginal=referencia});
  if(!SigueVigente(version,sesion)||!Exito(r))return;
  cliente.ConfirmarConsultaPendiente(r);MostrarMenu();
  string codigo=r.Datos.GetProperty("codigo").GetString()!;
  Aviso.Text=$"Resultado confirmado · {codigo}: {r.Datos.GetProperty("mensaje").GetString()} · Referencia {referencia}";
  if(codigo=="00")
   Comprobante(new Mensaje(){Tipo="RESPUESTA",IdSolicitud=referencia,Codigo=codigo,Datos=r.Datos.GetProperty("datos").Clone()},"RESULTADO RECUPERADO\n"+r.Datos.GetProperty("mensaje").GetString());
 }
}
