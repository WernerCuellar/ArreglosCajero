using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CajeroRed.Aplicacion;
using CajeroRed.Presentacion;
using CajeroRed.Protocolo;

namespace CajeroRed.Pruebas;

internal static partial class Program
{
 private sealed class ParCajero : IAsyncDisposable
 {
  private readonly TcpListener listener=new(IPAddress.Loopback,0);
  private readonly CancellationTokenSource fin=new();
  private readonly ConcurrentDictionary<string,TcpClient> conexiones=new();
  private readonly List<Task> tareas=[];
  public int Puerto=>((IPEndPoint)listener.LocalEndpoint).Port;
  public int Movimientos;
  public decimal Saldo=100;
  public bool OmitirMovimiento=true;
  public TaskCompletionSource? SaldoBloqueado;
  public TaskCompletionSource SaldoSolicitado=new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly Task aceptar;
  public ParCajero()
  {
   listener.Start();
   aceptar=Task.Run(async()=>
   {
    try{while(!fin.IsCancellationRequested){var c=await listener.AcceptTcpClientAsync(fin.Token);lock(tareas)tareas.Add(Atender(c));}}
    catch(OperationCanceledException){}catch(SocketException) when(fin.IsCancellationRequested){}
   });
  }
  private async Task Atender(TcpClient tcp)
  {
   string codigo="";
   using(tcp)
   {
    try
    {
     using var reader=new StreamReader(tcp.GetStream(),new UTF8Encoding(false));
     using var writer=new StreamWriter(tcp.GetStream(),new UTF8Encoding(false)){AutoFlush=true,NewLine="\n"};
     while(!fin.IsCancellationRequested)
     {
      string? linea=await reader.ReadLineAsync(fin.Token);if(linea==null)break;
      var m=Mensaje.Leer(linea);object datos=new {};string respuesta="00";
      if(m.Tipo=="REGISTRAR_CAJERO"){codigo=m.Cajero;conexiones[codigo]=tcp;datos=new {habilitado=true,inicializado=true};}
      if(m.Tipo=="HEARTBEAT")datos=new {habilitado=true,inicializado=true};
      if(m.Tipo=="LOGIN")datos=new {requiereToken=true};
      if(m.Tipo=="VALIDAR_TOKEN")datos=new {sesion="sesion-1",usuarioId=1,nombre="Usuario de prueba",debeCambiarPin=false};
      if(m.Tipo=="LOGOUT")continue; // No responde: el cierre local debe ser inmediato.
      if(m.Tipo=="BANCOS_TRANSFERENCIA")datos=new[]{new {codigoBanco="BANC90",nombreBanco="NETGEAR",esPropio=true}};
      if(m.Tipo=="VERIFICAR_DESTINO")datos=new {nombreTitular="Destinatario de prueba"};
      if(m.Tipo is "RETIRO" or "DEPOSITO" or "TRANSFERIR")
      {
       Interlocked.Increment(ref Movimientos);
       if(OmitirMovimiento)continue;
       datos=new {saldo=99,fecha="2026-10-31T12:00:00",idTransaccion=1,tokenTransferencia="TRF-UI",bancoDestino="BANC90",nombreDestino="Destinatario de prueba",cuentaDestino="**** 0002"};
      }
      if(m.Tipo=="ESTADO_SOLICITUD")
      {
       if(m.Sesion!="sesion-1")respuesta="25";
       else datos=new {idOriginal=m.Datos.GetProperty("idOriginal").GetString(),usuarioId=1,codigo="00",mensaje="Retiro aprobado",datos=new {saldo=99,fecha="2026-10-31T12:00:00",idTransaccion=1}};
      }
      if(m.Tipo=="CAMBIO_PIN")datos=new {fecha="2026-10-31T12:00:00"};
      if(m.Tipo=="CONSULTA_SALDO")
      {
       SaldoSolicitado.TrySetResult();
       if(SaldoBloqueado!=null)await SaldoBloqueado.Task.WaitAsync(fin.Token);
       datos=new {saldo=Saldo,limiteDiario=500,disponibleHoy=500,retiradoHoy=0,fecha="2026-10-31T12:00:00"};
      }
      await writer.WriteLineAsync(Mensaje.Respuesta(m,respuesta,"Resultado de prueba",datos).Serializar());
     }
    }
    catch(Exception e) when(e is IOException or OperationCanceledException or ObjectDisposedException or SocketException){}
    finally{if(codigo!="")conexiones.TryRemove(codigo,out _);}
   }
  }
  public void Cortar(string cajero){if(conexiones.TryGetValue(cajero,out var tcp))tcp.Dispose();}
  public async ValueTask DisposeAsync()
  {
   fin.Cancel();listener.Stop();foreach(var tcp in conexiones.Values)tcp.Dispose();
   try{await aceptar;}catch(OperationCanceledException){}
   Task[] pendientes;lock(tareas)pendientes=tareas.ToArray();
   try{await Task.WhenAll(pendientes).WaitAsync(TimeSpan.FromSeconds(2));}catch(Exception){}
   fin.Dispose();
  }
 }
 private static int RevisionCajero()
 {
  int resultado=0;var app=new Application(){ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.Resources.MergedDictionaries.Add(new ResourceDictionary(){Source=new Uri("/PresentacionCompartida;component/Tema.xaml",UriKind.Relative)});
  app.Dispatcher.InvokeAsync(async()=>
  {
   CajeroRed.Cajero.MainWindow? w=null;
   string carpeta=Path.Combine(raiz,".cli","cliente-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(carpeta);
   try
   {
    await using var par=new ParCajero();
    string pendientes=Path.Combine(carpeta,"pendientes");
    await using(var c=new ClienteTcp(){Direccion="127.0.0.1",Puerto=par.Puerto,Cajero="ATM-PENDIENTE"})
    {
     c.ConfigurarPendientes(pendientes);c.Iniciar();await Esperar(()=>c.Conectado);
     c.EstablecerSesion("sesion-1",1);
     Task<Mensaje> retiro=c.Enviar("RETIRO",new {monto=1,desglose=new Dictionary<int,int>{{1,1}}});
     await Esperar(()=>par.Movimientos==1);
     bool bloqueo=false;try{await c.Enviar("DEPOSITO",new {monto=1,desglose=new Dictionary<int,int>{{1,1}}});}catch(InvalidOperationException){bloqueo=true;}
     Verificar(bloqueo&&par.Movimientos==1,"no envía otro movimiento durante una operación en curso");
     try{await retiro;throw new Exception("Se esperaba timeout.");}catch(TimeoutException){}
     string referencia=c.SolicitudIncierta!;
     Verificar(referencia.Length>0&&Directory.GetFiles(pendientes,"*.json").Length==1,"respuesta perdida conserva referencia en disco");
     await using var recuperado=new ClienteTcp(){Direccion="127.0.0.1",Puerto=par.Puerto,Cajero="ATM-PENDIENTE"};
     recuperado.ConfigurarPendientes(pendientes);recuperado.EstablecerSesion("sesion-2",2);
     Verificar(recuperado.SolicitudIncierta==null,"otro usuario no carga la referencia ajena");
     recuperado.EstablecerSesion("sesion-1",1);
     Verificar(recuperado.SolicitudIncierta==referencia,"nuevo cliente recupera referencia del usuario y servidor correctos");
     string json=File.ReadAllText(Directory.GetFiles(pendientes,"*.json").Single());
     Verificar(!json.Contains("pin",StringComparison.OrdinalIgnoreCase)&&!json.Contains("token",StringComparison.OrdinalIgnoreCase)&&!json.Contains("sesion",StringComparison.OrdinalIgnoreCase),"pendiente local no contiene credenciales");
     var confirmado=await c.Enviar("ESTADO_SOLICITUD",new {idOriginal=referencia});
     c.ConfirmarConsultaPendiente(confirmado);
     Verificar(c.SolicitudIncierta==null&&!Directory.GetFiles(pendientes,"*.json").Any(),"consulta confirmada elimina pendiente local");
    }
    // Controles numéricos: las restricciones se ejercitan mediante sus eventos.
    var secreto=Ui.EntradaSecretaNumerica(4);
    bool Pegado(DependencyObject control,string texto)
    {
     var evento=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,texto),false,DataFormats.UnicodeText){RoutedEvent=DataObject.PastingEvent};
     ((UIElement)control).RaiseEvent(evento);return evento.CommandCancelled;
    }
    Verificar(!Pegado(secreto,"1234")&&Pegado(secreto,"12345")&&Pegado(secreto,"12 4")&&Pegado(secreto,"abcd"),"PIN rechaza pegados con letras, espacios o más de cuatro dígitos");
    var token=Ui.EntradaSecretaNumerica(6);
    Verificar(!Pegado(token,"123456")&&Pegado(token,"1234567"),"token admite seis dígitos sin truncar pegados");
    var textoPin=new TextCompositionEventArgs(Keyboard.PrimaryDevice,new TextComposition(InputManager.Current,secreto,"x")){RoutedEvent=TextCompositionManager.PreviewTextInputEvent};
    secreto.RaiseEvent(textoPin);Verificar(textoPin.Handled,"PIN bloquea letras al escribir");

    string configuracion=Path.Combine(carpeta,"cajero.json");
    File.WriteAllText(configuracion,JsonSerializer.Serialize(new {direccion="127.0.0.1",puerto=par.Puerto,codigoCajero="ATM-CLIENTE",nombreBanco="NETGEAR"}));
    w=new CajeroRed.Cajero.MainWindow(false,configuracion,Path.Combine(carpeta,"pendientes-ui"));
    w.WindowStartupLocation=WindowStartupLocation.Manual;w.Left=-12000;w.Top=-12000;w.ShowInTaskbar=false;w.Show();
    var tipo=w.GetType();var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
    object? Invocar(string metodo,params object?[] argumentos)=>tipo.GetMethod(metodo,flags)!.Invoke(w,argumentos);
    void Campo(string campo,object valor)=>tipo.GetField(campo,flags)!.SetValue(w,valor);
    var cliente=(ClienteTcp)tipo.GetField("cliente",flags)!.GetValue(w)!;
    cliente.Iniciar();await Esperar(()=>cliente.Conectado);await Task.Delay(80);
    ((TextBox)tipo.GetField("campoTarjeta",flags)!.GetValue(w)!).Text="4000000000000001";
    ((PasswordBox)tipo.GetField("campoPin",flags)!.GetValue(w)!).Password="1234";
    await (Task)Invocar("Login")!;
    var pantalla=(ContentControl)w.FindName("Pantalla");
    var tokenPanel=pantalla.Content is Border bordeToken?(StackPanel)bordeToken.Child:(StackPanel)pantalla.Content;tokenPanel.Children.OfType<PasswordBox>().Single().Password="123456";
    ((TextBlock)w.FindName("Aviso")).Text="Sesión cerrada por 60 segundos sin actividad.";
    tokenPanel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await Esperar(()=>cliente.Sesion!=null);
    Verificar(!((TextBlock)w.FindName("Aviso")).Text.Contains("60 segundos"),"nuevo acceso limpia aviso antiguo de inactividad");

    void Activar()
    {
     Campo("habilitado",true);Campo("inicializado",true);Campo("tarjeta","4000000000000001");Campo("nombre","Prueba");
     cliente.EstablecerSesion("sesion-1",1);Invocar("MostrarMenu");
    }
    Exception? errorDialogo=null;
    void Programar(Action<Window> accion)
    {
     errorDialogo=null;
     w.Dispatcher.BeginInvoke(new Action(()=>
     {
      try{accion(((IEnumerable<Window>)tipo.GetField("formularios",flags)!.GetValue(w)!).Single());}
      catch(Exception e){errorDialogo=e;foreach(var ventana in ((IEnumerable<Window>)tipo.GetField("formularios",flags)!.GetValue(w)!).ToArray())ventana.Close();}
     }));
    }
    Programar(form=>
    {
     var panel=(StackPanel)((ScrollViewer)form.Content).Content;
     var entradas=panel.Children.OfType<PasswordBox>().ToArray();
     entradas[0].Password="1234";entradas[1].Password="5678";entradas[2].Password="5679";
     var guardar=panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().First();
     guardar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     Verificar(form.IsVisible&&entradas[1].Password=="5678","confirmación PIN inválida conserva el formulario y sus valores");
     entradas[2].Password="5678";guardar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    });
    await (Task)Invocar("CambiarPin")!;if(errorDialogo!=null)throw errorDialogo;
    Verificar(pantalla.Content is Border,"cambio PIN válido muestra comprobante");
    await (Task)Invocar("Saldo")!;
    string Recibo()=>string.Join("\n",((StackPanel)((Border)pantalla.Content).Child).Children.OfType<TextBlock>().Select(t=>t.Text));
    Verificar(Recibo().Contains("Máximo según saldo y límite: Q "+100m.ToString("N2")),"máximo retirable considera saldo menor que límite");
    par.Saldo=1000;await (Task)Invocar("Saldo")!;
    Verificar(Recibo().Contains("Máximo según saldo y límite: Q "+500m.ToString("N2")),"máximo retirable considera límite menor que saldo");

    Activar();Programar(form=>
    {
     Campo("actividad",DateTime.UtcNow.AddSeconds(-61));
     form.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w)!,0,Key.A){RoutedEvent=Keyboard.PreviewKeyDownEvent});
     Invocar("RevisarInactividad");
     Verificar(cliente.Sesion!=null&&form.IsVisible,"actividad en formulario evita cierre por inactividad");
     Campo("actividad",DateTime.UtcNow.AddSeconds(-61));Invocar("RevisarInactividad");
     Verificar(cliente.Sesion==null&&!form.IsVisible,"inactividad cierra sesión y formulario");
    });
    await (Task)Invocar("CambiarPin")!;if(errorDialogo!=null)throw errorDialogo;

    Activar();int antes=par.Movimientos;
    Programar(form=>
    {
     Invocar("AtenderComando",Mensaje.Solicitud("COMANDO","ATM-CLIENTE",new {comando="FUERA_DE_SERVICIO"}));
     Verificar(cliente.Sesion==null&&!form.IsVisible,"deshabilitación cierra formulario y sesión");
    });
    await (Task)Invocar("Movimiento","RETIRO")!;if(errorDialogo!=null)throw errorDialogo;
    Verificar(par.Movimientos==antes,"formulario invalidado no envía dinero");
    Activar();
    Programar(form=>par.Cortar("ATM-CLIENTE"));
    await (Task)Invocar("Movimiento","DEPOSITO")!;if(errorDialogo!=null)throw errorDialogo;
    Verificar(cliente.Sesion==null&&!((IEnumerable<Window>)tipo.GetField("formularios",flags)!.GetValue(w)!).Any(),"corte de red cierra formulario abierto sin enviar operación");
    await Esperar(()=>cliente.Conectado,12000);await Task.Delay(80);
    Activar();
    par.SaldoSolicitado=new(TaskCreationOptions.RunContinuationsAsynchronously);par.SaldoBloqueado=new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task consulta=(Task)Invocar("Saldo")!;await par.SaldoSolicitado.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var tiempo=System.Diagnostics.Stopwatch.StartNew();Invocar("CerrarSesion","Sesión cerrada.");
    Verificar(cliente.Sesion==null&&tiempo.ElapsedMilliseconds<1000,"cerrar sesión no espera respuesta de LOGOUT");
    par.SaldoBloqueado.TrySetResult();await consulta;par.SaldoBloqueado=null;
    Verificar(pantalla.Content is not Border b||b.Child is not StackPanel sp||!sp.Children.OfType<TextBlock>().Any(t=>t.Text=="Operación completada"),"respuesta tardía no restaura pantalla de la sesión anterior");

    Activar();par.OmitirMovimiento=false;int transferenciasAntes=par.Movimientos;
    int paso=0;Exception? errorTransferencia=null;
    var automatizar=new System.Windows.Threading.DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(40)};
    automatizar.Tick+=(_,_)=>{
     try{
      var form=app.Windows.OfType<Window>().FirstOrDefault(v=>v!=w&&v.Owner==w&&v.IsVisible);
      if(form==null)return;
      if(paso==0&&form.Title.Contains("destinatario")){
       var panel=(StackPanel)form.Content;
       panel.Children.OfType<TextBox>().Single().Text="20000002";
       paso=1;panel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      }else if(paso==1&&form.Title.StartsWith("Transferir a")){
       var panel=(StackPanel)((ScrollViewer)form.Content).Content;
       var entradas=panel.Children.OfType<TextBox>().ToArray();entradas[0].Text="12.50";entradas[1].Text="Prueba de pantalla";
       paso=2;panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      }else if(paso==2&&form.Title=="Confirmar transferencia"){
       var panel=(StackPanel)((ScrollViewer)form.Content).Content;
       Verificar(string.Join(" ",panel.Children.OfType<TextBlock>().Select(t=>t.Text)).Contains("Destinatario de prueba"),"confirmación muestra nombre del destinatario");
       paso=3;panel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      }
     }catch(Exception e){errorTransferencia=e;automatizar.Stop();foreach(var f in app.Windows.OfType<Window>().Where(v=>v.Owner==w).ToArray())f.Close();}
    };
    automatizar.Start();await (Task)Invocar("TransferirDinero")!;automatizar.Stop();if(errorTransferencia!=null)throw errorTransferencia;
    Verificar(paso==3&&par.Movimientos==transferenciasAntes+1&&Recibo().Contains("TRF-UI"),"formulario verifica, confirma y muestra comprobante de una sola transferencia");
    Invocar("CerrarSesion","Prueba de configuración");
    _=w.Dispatcher.BeginInvoke(new Action(()=>{
     var form=app.Windows.OfType<Window>().Single(v=>v.Title=="Buscar y configurar servidor NETGEAR");
     var panel=(DockPanel)form.Content;
     var abajo=panel.Children.OfType<StackPanel>().Last();abajo.Children.OfType<TextBox>().Single().Text="127.0.0.1";
     var fila=abajo.Children.OfType<WrapPanel>().Single();var campos=fila.Children.OfType<TextBox>().ToArray();
     campos[0].Text=par.Puerto.ToString();campos[1].Text="ATM-CONFIG";
     abajo.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }));
    Invocar("BuscarServidor");
    Verificar(File.ReadAllText(configuracion).Contains("ATM-CONFIG")&&cliente.Cajero=="ATM-CONFIG","ventana Buscar servidor guarda IP, puerto y código para conectar");

    // Una configuración inválida no debe iniciar ni matar la aplicación.
    string corrupta=Path.Combine(carpeta,"invalida.json");File.WriteAllText(corrupta,"{mal json");
    var invalida=new CajeroRed.Cajero.MainWindow(false,corrupta,pendientes);
    Verificar(((TextBlock)invalida.FindName("Aviso")).Text.Contains("Configuración inválida"),"JSON dañado mantiene accesible la configuración");
    tipo.GetField("cerrando",flags)!.SetValue(invalida,true);invalida.Close();
    File.WriteAllText(corrupta,"{\"direccion\":\"127.0.0.1\",\"puerto\":70000,\"codigoCajero\":\"ATM-01\"}");
    invalida=new CajeroRed.Cajero.MainWindow(false,corrupta,pendientes);
    Verificar(((TextBlock)invalida.FindName("Aviso")).Text.Contains("Configuración inválida"),"puerto fuera de rango no detiene la aplicación");
    tipo.GetField("cerrando",flags)!.SetValue(invalida,true);invalida.Close();
    File.WriteAllText(corrupta,JsonSerializer.Serialize(new {direccion="127.0.0.1",puerto=par.Puerto,codigoCajero="ATM-PRUEBA"}));
    invalida=new CajeroRed.Cajero.MainWindow(false,corrupta,pendientes);
    File.SetAttributes(corrupta,FileAttributes.ReadOnly);
    invalida.WindowStartupLocation=WindowStartupLocation.Manual;invalida.Left=-12000;invalida.Top=-12000;invalida.ShowInTaskbar=false;invalida.Show();
    _=invalida.Dispatcher.BeginInvoke(new Action(()=>
    {
     var form=app.Windows.OfType<Window>().Single(v=>v.Title=="Conexión del cajero");
     var panel=(StackPanel)((ScrollViewer)form.Content).Content;
     panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }));
    try
    {
     tipo.GetMethod("Configurar",flags)!.Invoke(invalida,null);
     Verificar(((TextBlock)invalida.FindName("Aviso")).Text.Contains("No se pudo guardar"),"error de escritura de configuración se informa sin cerrar el cajero");
    }
    finally{File.SetAttributes(corrupta,FileAttributes.Normal);tipo.GetField("cerrando",flags)!.SetValue(invalida,true);invalida.Close();}
    string recibos=Path.Combine(raiz,"Pruebas","Evidencias","Comprobantes");Directory.CreateDirectory(recibos);
    foreach(string operacion in new[]{"RETIRO","DEPOSITO","CONSULTA_SALDO","CAMBIO_PIN"})
    {
     string archivo=Path.Combine(recibos,operacion+".pdf");
     CajeroRed.Cajero.ComprobantePdf.Guardar(archivo,[operacion,"Fecha del servidor: 2026-10-31 12:00:00","Referencia: PRUEBA-"+operacion,"Cajero: ATM-PRUEBA","Tarjeta: **** 0001",operacion=="CAMBIO_PIN"?"PIN actualizado correctamente.":"Monto/saldo confirmado: Q 100.00"]);
     Verificar(File.ReadAllBytes(archivo).Take(4).SequenceEqual(Encoding.ASCII.GetBytes("%PDF")),"comprobante PDF válido: "+operacion);
    }
    Console.WriteLine($"RESULTADO: {aprobadas} comprobaciones del cajero correctas.");
    await cliente.DisposeAsync();Campo("cerrando",true);w.Close();w=null;
   }
   catch(Exception e){resultado=1;Console.Error.WriteLine(e);}
   finally
   {
    if(w!=null){w.GetType().GetField("cerrando",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(w,true);w.Close();}
    app.Shutdown(resultado);
   }
  });
  app.Run();return resultado;
 }
}
