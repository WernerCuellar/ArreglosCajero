using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CajeroRed.Presentacion;

namespace CajeroRed.Servidor;

public partial class MainWindow
{
 private void AccionesInterbancarias(string nombre,WrapPanel acciones)
 {
  if(nombre=="Bancos de la red")
  {
   acciones.Children.Add(Ui.BotonAsync("Mi banco NETGEAR",async()=>
   {
    var datos=await servidor.Interbancario.Bancos();var propio=datos.AsEnumerable().SingleOrDefault(r=>(bool)r["EsPropio"]);
    var f=Ui.Formulario(this,"Identidad del banco",("codigo","Código único (confirmar con los grupos)",propio?["CodigoBanco"].ToString()??"BANC02",false),
     ("nombre","Nombre del banco",propio?["NombreBanco"].ToString()??"NETGEAR",false),("limite","Límite diario bancario","25000",false));
    if(f==null)return;
    await servidor.Interbancario.Configurar(admin.Id!.Value,f["codigo"],f["nombre"],"0.0.0.0",puerto,true,true,Ui.Monto(f["limite"]));
    if(servidor.Activo&&!servidor.Interbancario.DiscoveryActivo)await servidor.Interbancario.IniciarDiscovery(puerto);
    await Refrescar();
   }));
   acciones.Children.Add(Ui.BotonAsync("Registrar / editar banco",async()=>{await RegistrarBanco(tabla?.SelectedItem as DataRowView);await Refrescar();}));
   acciones.Children.Add(Ui.BotonAsync("Escanear red local",async()=>
   {
    var encontrados=await servidor.Interbancario.Escanear();var grid=Ui.Tabla(encontrados.DefaultView);var p=new DockPanel(){Margin=new Thickness(18)};
    var boton=Ui.BotonAsync("Registrar seleccionado",async()=>{var seleccionado=grid.SelectedItem as DataRowView??throw new ArgumentException("Seleccione un banco.");await RegistrarBanco(seleccionado);await Refrescar();});
    DockPanel.SetDock(boton,Dock.Top);p.Children.Add(boton);
    var aviso=Ui.Texto("El descubrimiento muestra anuncios; confirme código e IP con el otro grupo antes de autorizar transferencias.",12);DockPanel.SetDock(aviso,Dock.Top);p.Children.Add(aviso);p.Children.Add(grid);
    new Window(){Owner=this,Title="Bancos detectados en red",Width=950,Height=500,Content=p,WindowStartupLocation=WindowStartupLocation.CenterOwner}.Show();
   }));
   acciones.Children.Add(Ui.BotonAsync("Ping banco",async()=>{var r=Seleccion();var respuesta=await servidor.Interbancario.Ping((string)r["IP"],(int)r["Puerto"]);Ui.Mostrar(this,"Ping banco",respuesta.Mensaje+"\n"+respuesta.Datos);}));
   acciones.Children.Add(Ui.BotonAsync("Verificar cuenta",async()=>
   {
    var b=(string)Seleccion()["CodigoBanco"];var f=Ui.Formulario(this,"Verificar cuenta",("cuenta","Cuenta o tarjeta del banco destino","",false));if(f==null)return;
    var r=await servidor.Interbancario.Verificar(b,f["cuenta"]);Ui.Mostrar(this,"Verificar cuenta",r.Mensaje+"\n"+r.Datos);
   }));
   acciones.Children.Add(Ui.BotonAsync("Transferir",async()=>
   {
    string banco=(string)Seleccion()["CodigoBanco"];
    var f=Ui.Formulario(this,"Transferencia a "+banco,("usuario","ID del usuario local que autoriza el envío","",false),
     ("cuenta","Cuenta o tarjeta destino","",false),("monto","Monto en quetzales","",false),("concepto","Concepto","Transferencia interbancaria",false));
    if(f==null)return;
    if(!int.TryParse(f["usuario"],out int usuario))throw new ArgumentException("ID de usuario inválido.");
    decimal monto=Ui.Monto(f["monto"]);
    var verificacion=await servidor.Interbancario.Verificar(banco,f["cuenta"]);
    if(verificacion.Codigo!="00")throw new ArgumentException(verificacion.Mensaje);
    if(MessageBox.Show(this,$"Enviar Q {monto:N2} del usuario {usuario} a {verificacion.Datos.GetProperty("nombreTitular").GetString()} en {banco}.\n¿El usuario autorizó esta transferencia?", "Confirmar transferencia",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
    var r=await servidor.Interbancario.Transferir(admin.Id!.Value,usuario,banco,f["cuenta"],monto,f["concepto"]);
    Ui.Mostrar(this,"Comprobante de transferencia",r.Mensaje+"\n"+r.Datos+"\nConserve el token para el usuario y la conciliación.");
    await Refrescar();
   }));
  }
  if(nombre=="Transferencias")
   acciones.Children.Add(Ui.BotonAsync("Consultar / conciliar",async()=>
   {
    var seleccionado=Seleccion();
    if((string)seleccionado["Tipo"]!="INTERBANCARIA"||(string)seleccionado["Direccion"]!="SALIENTE"||(string)seleccionado["Estado"] is not ("INCIERTA" or "RESERVADA"))throw new ArgumentException("Seleccione una transferencia interbancaria saliente pendiente de confirmación.");
    string token=(string)seleccionado["TokenTransferencia"];
    var r=await servidor.Interbancario.Conciliar(admin.Id!.Value,token);
    Ui.Mostrar(this,"Estado de "+token,r.Mensaje+"\n"+r.Datos+"\nUna respuesta sin confirmación mantiene el saldo reservado.");
    await Refrescar();
   }));
 }
 private async Task RegistrarBanco(DataRowView? seleccionado)
 {
  if(seleccionado?.Row.Table.Columns.Contains("EsPropio")==true&&(bool)seleccionado["EsPropio"])
   throw new ArgumentException("Para editar NETGEAR use Mi banco NETGEAR.");
  string Valor(string columna,string defecto)=>seleccionado?.Row.Table.Columns.Contains(columna)==true?Convert.ToString(seleccionado[columna],CultureInfo.InvariantCulture)??defecto:defecto;
  var f=Ui.Formulario(this,"Banco externo",("codigo","Código de banco",Valor("CodigoBanco","BANC01"),false),
   ("nombre","Nombre",Valor("NombreBanco",""),false),("ip","IPv4 confirmada por el otro grupo",Valor("IP",""),false),
   ("puerto","Puerto TCP",Valor("Puerto","5000"),false),("limite","Límite diario bancario",Valor("LimiteDiario","25000"),false),
   ("confiable","Autorizar esta IP: 1 = sí, 0 = solo registrar",Valor("Confiable","0")=="True"?"1":Valor("Confiable","0"),false));
  if(f==null)return;
  if(!int.TryParse(f["puerto"],out int port)||f["confiable"] is not ("0" or "1"))throw new ArgumentException("Revise puerto y autorización (0 o 1).");
  await servidor.Interbancario.Configurar(admin.Id!.Value,f["codigo"],f["nombre"],f["ip"],port,false,f["confiable"]=="1",Ui.Monto(f["limite"]));
 }
}
