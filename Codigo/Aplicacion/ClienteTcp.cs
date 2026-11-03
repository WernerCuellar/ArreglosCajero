using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using CajeroRed.Protocolo;

namespace CajeroRed.Aplicacion;

public sealed partial class ClienteTcp : IAsyncDisposable
{
    public string Direccion {get;set;}="127.0.0.1";
    public int Puerto {get;set;}=5000;
    public string Cajero {get;set;}="ATM-01";
    public bool Conectado {get;private set;}
    private string? sesion;
    public string? Sesion {get=>sesion;set{sesion=value;if(value==null)UsuarioActual=null;}}
    public string? SolicitudIncierta {get;private set;}
    public event Action<string>? Estado;
    public event Action<Mensaje>? Comando;
    private readonly ConcurrentDictionary<string,TaskCompletionSource<Mensaje>> pendientes=new();
    private readonly SemaphoreSlim escritura=new(1,1);
    private readonly CancellationTokenSource fin=new();
    private TcpClient? socket;
    private StreamWriter? writer;
    private Task? ciclo;
    private long ultimaRespuesta=Stopwatch.GetTimestamp();
    public void Iniciar()
    {
        if(string.IsNullOrWhiteSpace(Direccion)||Puerto is <1 or >65535||string.IsNullOrWhiteSpace(Cajero))
        {Estado?.Invoke("Configuración de conexión inválida. Corríjala para conectar.");return;}
        if(ciclo==null||ciclo.IsCompleted)ciclo=Task.Run(Reconectar);
    }
    public void ReconectarAhora()=>socket?.Dispose();
    public async Task<Mensaje> Enviar(string tipo,object? datos=null,string? id=null)
    {
        var m=Mensaje.Solicitud(tipo,Cajero,datos,Sesion);
        if(id!=null)m=m with {IdSolicitud=id};
        return await EnviarConProteccion(m);
    }
    private async Task<Mensaje> Solicitar(Mensaje m,CancellationToken ct,int segundos=10)
    {
        var actual=writer ?? throw new IOException("Servidor desconectado.");
        var promesa=new TaskCompletionSource<Mensaje>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!pendientes.TryAdd(m.IdSolicitud,promesa))throw new InvalidOperationException("Solicitud ya en curso.");
        try
        {
            await escritura.WaitAsync(ct);
            try {await actual.WriteLineAsync(m.Serializar().AsMemory(),ct);}finally{escritura.Release();}
            var r=await promesa.Task.WaitAsync(TimeSpan.FromSeconds(segundos),ct);
            return r;
        }
        finally{pendientes.TryRemove(m.IdSolicitud,out _);}
    }
    private async Task Reconectar()
    {
        while(!fin.IsCancellationRequested)
        {
            Task? lectura=null,latido=null;string fase="CONECTAR",motivo="";
            using var conectado=CancellationTokenSource.CreateLinkedTokenSource(fin.Token);
            try
            {
                Estado?.Invoke("Conectando…");
                socket=new TcpClient();SocketRed.Configurar(socket);
                await socket.ConnectAsync(Direccion,Puerto,fin.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5),fin.Token);
                writer=new StreamWriter(socket.GetStream(),new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
                lectura=Leer(socket,conectado.Token);
                fase="REGISTRAR";
                var registro=await Solicitar(Mensaje.Solicitud("REGISTRAR_CAJERO",Cajero,new {equipo=Environment.MachineName,version="2.2.0"}),conectado.Token);
                if(registro.Codigo!="00")throw new IOException(registro.MensajeTexto);
                ultimaRespuesta=Stopwatch.GetTimestamp();Conectado=true;Estado?.Invoke("Conectado · "+Cajero);
                Comando?.Invoke(registro);
                latido=Latidos(conectado.Token);
                var terminada=await Task.WhenAny(lectura,latido);fase=ReferenceEquals(terminada,lectura)?"LECTURA":"HEARTBEAT";
                await terminada;
            }
            catch(Exception e) when(e is SocketException or IOException or TimeoutException or OperationCanceledException or System.Text.Json.JsonException)
            {motivo=e.Message;DiagnosticoRed.Registrar("CAJERO",Cajero,"ERROR_CONEXION",new {tipo=e.GetType().Name,fase,socket=((e as SocketException)??(e.InnerException as SocketException))?.SocketErrorCode.ToString(),destino=Direccion,puerto=Puerto});if(!fin.IsCancellationRequested)Estado?.Invoke("Fuera de servicio · "+e.Message);}
            finally
            {
                Conectado=false;Sesion=null;conectado.Cancel();writer=null;socket?.Dispose();
                foreach(var p in pendientes.Values)p.TrySetException(new IOException("Se perdió la conexión. Consulte el estado antes de repetir una operación."));
                try{if(lectura!=null)await lectura;}catch(Exception){}
                try{if(latido!=null)await latido;}catch(Exception){}
                if(!fin.IsCancellationRequested)Estado?.Invoke("Fuera de servicio · "+motivo+" · reintento en 5 s");
            }
            try{await Task.Delay(5000,fin.Token);}catch(OperationCanceledException){}
        }
    }
    private async Task Leer(TcpClient actual,CancellationToken ct)
    {
        var bytes=new List<byte>();var buffer=new byte[4096];var stream=actual.GetStream();
        while(!ct.IsCancellationRequested)
        {
            int n=await stream.ReadAsync(buffer,ct);if(n==0)throw new IOException("Conexión cerrada por el servidor.");
            for(int i=0;i<n;i++)
            {
                if(buffer[i]!=10){if(bytes.Count>65536)throw new IOException("Respuesta demasiado grande.");bytes.Add(buffer[i]);continue;}
                var m=Mensaje.Leer(new UTF8Encoding(false,true).GetString(bytes.ToArray()).TrimEnd('\r'));bytes.Clear();ultimaRespuesta=Stopwatch.GetTimestamp();
                if(m.Tipo=="COMANDO")Comando?.Invoke(m);
                else if(pendientes.TryGetValue(m.IdSolicitud,out var p))p.TrySetResult(m);
            }
        }
    }
    private async Task Latidos(CancellationToken ct)
    {
        using var reloj=new PeriodicTimer(TimeSpan.FromSeconds(10));
        while(await reloj.WaitForNextTickAsync(ct))
        {
         try
         {
          var r=await Solicitar(Mensaje.Solicitud("HEARTBEAT",Cajero),ct,3);
          if(r.Codigo!="00")DiagnosticoRed.Registrar("CAJERO",Cajero,"HEARTBEAT_RECHAZADO",new {codigo=r.Codigo});
          Comando?.Invoke(r);
         }
         catch(TimeoutException)
         {
          DiagnosticoRed.Registrar("CAJERO",Cajero,"HEARTBEAT_SIN_RESPUESTA",new {segundos=(int)Stopwatch.GetElapsedTime(ultimaRespuesta).TotalSeconds});
          if(Stopwatch.GetElapsedTime(ultimaRespuesta)>TimeSpan.FromSeconds(30))throw;
         }
        }
    }
    public async ValueTask DisposeAsync()
    {fin.Cancel();socket?.Dispose();if(ciclo!=null)try{await ciclo;}catch(Exception){}fin.Dispose();}
}
