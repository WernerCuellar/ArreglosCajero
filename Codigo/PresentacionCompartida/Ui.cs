using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CajeroRed.Protocolo;

namespace CajeroRed.Presentacion;

public static class Ui
{
 public static DataGrid Tabla(System.Collections.IEnumerable? datos=null)
 {
  var tabla=new DataGrid(){ItemsSource=datos,Language=System.Windows.Markup.XmlLanguage.GetLanguage("es-GT")};
  tabla.AutoGeneratingColumn+=(_,e)=>
  {
   e.Column.Header=System.Text.RegularExpressions.Regex.Replace(e.PropertyName,"(?<=[a-z])(?=[A-Z])"," ");
   if(e.Column is not DataGridTextColumn columna)return;
   if(e.PropertyType==typeof(DateTime))columna.Binding.StringFormat="yyyy-MM-dd HH:mm:ss";
   else if(e.PropertyName is "Saldo" or "Monto" or "LimiteDiario" or "RetiradoHoy" or "DisponibleHoy" or "MaximoRetirable" or "Efectivo" or "Total" or "ConsumoHoy" or "Anterior" or "Nuevo" or "Inicializacion" or "CargasAdicionales" or "Depositos" or "Retiros" or "Esperado" or "InventarioActual" or "Diferencia" or "TransferidoHoy" or "CupoDisponibleHoy")
    columna.Binding.StringFormat="Q {0:N2}";
  };
  return tabla;
 }
 public static readonly CultureInfo Cultura=CultureInfo.GetCultureInfo("es-GT");
 public static TextBlock Texto(string t,double size=14)=>new(){Text=t,FontSize=size,Margin=new Thickness(0,4,0,8)};
 public static TextBlock Titulo(string t)=>new(){Text=t,FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14),Foreground=new SolidColorBrush(Color.FromRgb(22,58,130))};
 public static Button Boton(string t,Action click){var b=new Button(){Content=t};b.Click+=(_,_)=>click();return b;}
 public static Button BotonAsync(string t,Func<Task> click)
 {
  var b=new Button(){Content=t};b.Click+=async(_,_)=>{b.IsEnabled=false;try{await click();}catch(Exception e){Error(e);}finally{b.IsEnabled=true;}};return b;
 }
 public static void Error(Exception e)=>MessageBox.Show(e is TimeoutException?"Sin respuesta en 10 segundos. Si fue un movimiento, consulte el estado antes de repetirlo.":e.Message,"Revise los datos o la conexión",MessageBoxButton.OK,MessageBoxImage.Warning);
 public static Border Tarjeta(UIElement contenido)=>new(){Background=Brushes.White,CornerRadius=new CornerRadius(9),Padding=new Thickness(20),Margin=new Thickness(0,0,0,16),BorderBrush=new SolidColorBrush(Color.FromRgb(212,221,234)),BorderThickness=new Thickness(1),Child=contenido};
 public static TextBox EntradaNumerica(int maximo,string valor="")
 {
  var campo=new TextBox(){MaxLength=maximo,Text=valor,AllowDrop=false,ToolTip=$"Solo dígitos; máximo {maximo}."};
  InputMethod.SetIsInputMethodEnabled(campo,false);
  bool Permitido(string texto)=>texto.All(char.IsAsciiDigit)&&campo.Text.Length-campo.SelectionLength+texto.Length<=maximo;
  campo.PreviewTextInput+=(_,e)=>e.Handled=!Permitido(e.Text);
  campo.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space)e.Handled=true;};
  DataObject.AddPastingHandler(campo,(_,e)=>{if(e.DataObject.GetData(DataFormats.UnicodeText,true) is not string texto||!Permitido(texto))e.CancelCommand();});
  return campo;
 }
 public static PasswordBox EntradaSecretaNumerica(int maximo,string valor="")
 {
  var campo=new PasswordBox(){MaxLength=maximo,Password=valor,AllowDrop=false,ToolTip=$"Exactamente {maximo} dígitos."};
  InputMethod.SetIsInputMethodEnabled(campo,false);
  campo.PreviewTextInput+=(_,e)=>e.Handled=!e.Text.All(char.IsAsciiDigit)||e.Text.Length>maximo;
  campo.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space)e.Handled=true;};
  // Rechazar el pegado completo si excede la capacidad evita truncarlo
  // silenciosamente. Para sustituir un secreto pegado, vaciarlo primero.
  DataObject.AddPastingHandler(campo,(_,e)=>{
   if(e.DataObject.GetData(DataFormats.UnicodeText,true) is not string texto||!texto.All(char.IsAsciiDigit)||campo.Password.Length+texto.Length>maximo)e.CancelCommand();
  });
  return campo;
 }
 public static TextBox EntradaTarjeta(string valor="")=>EntradaNumerica(16,valor);
 public static Dictionary<string,string>? Formulario(Window propietario,string titulo,params (string Clave,string Etiqueta,string Valor,bool Secreto)[] campos)
  =>FormularioValidado(propietario,titulo,campos);
 public static Dictionary<string,string>? FormularioValidado(Window propietario,string titulo,
  (string Clave,string Etiqueta,string Valor,bool Secreto)[] campos,Action<Dictionary<string,string>>? validar=null,Action<Window>? preparar=null,string aceptarTexto="Guardar")
 {
  var w=new Window(){Owner=propietario,Title=titulo,Width=500,MaxHeight=760,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel(){Margin=new Thickness(24)};panel.Children.Add(Titulo(titulo));
  var entradas=new Dictionary<string,Control>();Dictionary<string,string>? resultado=null;
  foreach(var c in campos)
  {
   panel.Children.Add(Texto(c.Etiqueta));
   Control input=c.Secreto?EntradaSecretaNumerica(4,c.Valor):c.Clave=="tarjeta"?EntradaTarjeta(c.Valor):new TextBox(){Text=c.Valor};
   panel.Children.Add(input);entradas[c.Clave]=input;
  }
  var error=Texto("");error.Foreground=Brushes.Firebrick;panel.Children.Add(error);
  var acciones=new WrapPanel();var aceptar=Boton(aceptarTexto,()=>{
   try
   {
    var valores=entradas.ToDictionary(x=>x.Key,x=>x.Value is PasswordBox p?p.Password:((TextBox)x.Value).Text.Trim());
    validar?.Invoke(valores);resultado=valores;w.DialogResult=true;
   }
   catch(Exception e) when(e is ArgumentException or InvalidOperationException or FormatException or OverflowException){error.Text=e.Message;}
  });aceptar.IsDefault=true;
  acciones.Children.Add(aceptar);acciones.Children.Add(Boton("Cancelar",()=>w.DialogResult=false));panel.Children.Add(acciones);
  w.Content=new ScrollViewer(){Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  preparar?.Invoke(w);
  return w.ShowDialog()==true?resultado:null;
 }
 public static decimal Monto(string t)
 {
  // Un solo formato para evitar ambigüedad entre separadores de miles y decimales.
  if(!decimal.TryParse(t,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)||n<0||decimal.Round(n,2)!=n)
   throw new ArgumentException("Use un monto positivo con punto decimal y sin separadores de miles.");return n;
 }
 public static Dictionary<int,int>? Desglose(Window owner,string titulo)
 {
  var campos=Billetes.Denominaciones.Select(d=>(d.ToString(),$"Billetes de Q {d}","0",false)).ToArray();
  var datos=Formulario(owner,titulo,campos);if(datos==null)return null;
  var b=datos.ToDictionary(x=>int.Parse(x.Key),x=>int.TryParse(x.Value,out var n)&&n>=0&&n<=30000?n:throw new ArgumentException("Las cantidades deben ser enteros entre 0 y 30000."));
  if(Billetes.Total(b)<=0)throw new ArgumentException("Ingrese al menos un billete.");return b;
 }
 public static void Mostrar(Window owner,string titulo,string mensaje)
 {MessageBox.Show(owner,mensaje,titulo,MessageBoxButton.OK,MessageBoxImage.Information);}
}
