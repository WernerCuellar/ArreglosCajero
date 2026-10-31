using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CajeroRed.Infraestructura;
using CajeroRed.Protocolo;

namespace CajeroRed.Aplicacion;

public sealed class ServidorTcp(BaseDatos bd) : IAsyncDisposable
{
    private sealed class Conexion(TcpClient cliente)
    {
        public TcpClient Cliente = cliente;
        public StreamWriter Escritor = new(cliente.GetStream(),new UTF8Encoding(false),4096,true) { AutoFlush=true, NewLine="\n" };
        public SemaphoreSlim Escritura = new(1,1);
        public string? Codigo;
        public string Motivo = "REMOTO_CERRO";
        public string UltimoTipo = "SIN_REGISTRO";
        public long UltimaRecepcion=Stopwatch.GetTimestamp();
        public long UltimoHeartbeat;
        public async Task EnviarLinea(string linea,CancellationToken ct)
        {await Escritura.WaitAsync(ct);try{await Escritor.WriteLineAsync(linea.AsMemory(),ct);}finally{Escritura.Release();}}
        public async Task Enviar(Mensaje m, CancellationToken ct)
        { await Escritura.WaitAsync(ct); try { await Escritor.WriteLineAsync(m.Serializar().AsMemory(),ct); } finally { Escritura.Release(); } }
    }
    public BancoInterbancario Interbancario {get;}=new(bd);
    private TcpListener? listener;
    private CancellationTokenSource? cancelacion;
    private readonly ConcurrentDictionary<string,Conexion> cajeros = new();
    private readonly ConcurrentDictionary<int,Task> tareas = new();
    private readonly ConcurrentDictionary<int,TcpClient> sockets = new();
    private int siguiente;
    public int Conectados => cajeros.Count;
    public bool Activo => listener != null;
    public event Action<string>? Evento;
    public event Action<string,string>? TokenGenerado;
    public async Task Iniciar(int puerto)
    {
        if (Activo) return;
        // Quita estados de conexión obsoletos tras un reinicio del proceso.
        await bd.Ejecutar("UPDATE dbo.Cajero SET Conectado=0; UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE Fin IS NULL;");
        var nuevo = new TcpListener(IPAddress.Any,puerto); nuevo.Start();
        listener=nuevo; cancelacion=new();
        var token=cancelacion.Token;
        _=Task.Run(()=>Aceptar(nuevo,token)); _=Task.Run(()=>Vigilar(token));
        try{await Interbancario.IniciarDiscovery(puerto);}catch(Exception e) when(e is SocketException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException){Evento?.Invoke("TCP activo; configure banco propio o revise UDP 5005 para descubrimiento.");}
        Evento?.Invoke($"Servidor escuchando en puerto {puerto}.");
    }
    private async Task Aceptar(TcpListener servidor, CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested)
        {
            var cliente=await servidor.AcceptTcpClientAsync(ct); cliente.NoDelay=true;
            int id=Interlocked.Increment(ref siguiente); sockets[id]=cliente;
            tareas[id]=Procesar(cliente,ct);
            _=tareas[id].ContinueWith(t=>{tareas.TryRemove(id,out _);sockets.TryRemove(id,out _);},TaskScheduler.Default);
        }} catch (OperationCanceledException) {} catch (SocketException) when (ct.IsCancellationRequested) {}
    }
    private async Task Procesar(TcpClient cliente,CancellationToken ct)
    {
        var con=new Conexion(cliente);
        string remoto=cliente.Client.RemoteEndPoint?.ToString()??"desconocido";
        try
        {
            var stream=cliente.GetStream();
            // Leer byte a byte con búfer del stream evita ReadLine sin límite de memoria.
            var linea=new List<byte>(); var buffer=new byte[4096];
            using var registroTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            registroTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!ct.IsCancellationRequested)
            {
                int n=await stream.ReadAsync(buffer,con.Codigo==null?registroTimeout.Token:ct);
                if(n==0) break;
                for(int i=0;i<n;i++)
                {
                    byte b=buffer[i];
                    if(b!=10) { if(linea.Count>=32768) throw new IOException("Mensaje demasiado grande.");linea.Add(b);continue; }
                    Mensaje m;
                    try {
                        string crudo=new UTF8Encoding(false,true).GetString(linea.ToArray()).TrimEnd('\r');
                        if(con.Codigo==null&&BancoInterbancario.EsInterbancario(crudo))
                        {
                            con.Motivo="INTERBANCARIO_FINALIZADO";
                            string ip=((IPEndPoint)cliente.Client.RemoteEndPoint!).Address.ToString();
                            await con.EnviarLinea(await Interbancario.Atender(crudo,ip),ct);
                            return;
                        }
                        m=Mensaje.Leer(crudo);
                    }
                    catch(Exception e) when(e is JsonException or DecoderFallbackException) {throw new IOException("JSON inválido.");}
                    linea.Clear();
                    if(string.IsNullOrWhiteSpace(m.IdSolicitud)||m.IdSolicitud.Length>64||m.Cajero.Length>24) throw new IOException("Cabecera inválida.");
                    if(con.Codigo!=null && (m.Cajero!=con.Codigo||m.Tipo=="REGISTRAR_CAJERO"))
                    {await con.Enviar(Mensaje.Respuesta(m,"91","Identidad del cajero inválida."),ct);continue;}
                    if(con.Codigo==null && m.Tipo!="REGISTRAR_CAJERO") throw new IOException("Debe registrarse primero.");
                    bool reservado=false;
                    if(m.Tipo=="REGISTRAR_CAJERO")
                    {
                        reservado=cajeros.TryAdd(m.Cajero,con);
                        if(!reservado) {con.Motivo="CODIGO_DUPLICADO";await con.Enviar(Mensaje.Respuesta(m,"94","Código ya conectado."),ct);return;}
                        con.Codigo=m.Cajero;
                        m=m with { Datos=JsonSerializer.SerializeToElement(new {equipo=m.Datos.GetProperty("equipo").GetString(),version=m.Datos.GetProperty("version").GetString(),ip=((IPEndPoint)cliente.Client.RemoteEndPoint!).Address.ToString()}) };
                    }
                    Mensaje respuesta;con.UltimoTipo=m.Tipo;con.UltimaRecepcion=Stopwatch.GetTimestamp();if(m.Tipo=="HEARTBEAT")con.UltimoHeartbeat=con.UltimaRecepcion;var reloj=Stopwatch.StartNew();
                    try {respuesta=await bd.Atender(m);}
                    catch(Exception e) when(e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
                    { DiagnosticoRed.Registrar("SERVIDOR",con.Codigo??"SIN_REGISTRO","SQL_ERROR",new {tipo=m.Tipo,sqlNumero=(e as Microsoft.Data.SqlClient.SqlException)?.Number,ms=reloj.ElapsedMilliseconds});
                      Evento?.Invoke("Error de base de datos. Revise SQL Server; no se informa éxito al cajero.");respuesta=Mensaje.Respuesta(m,"96","No se pudo confirmar la operación. Consulte su estado antes de reintentar."); }
                    if(reloj.ElapsedMilliseconds>=2000)DiagnosticoRed.Registrar("SERVIDOR",con.Codigo??"SIN_REGISTRO","SQL_LENTO",new {tipo=m.Tipo,ms=reloj.ElapsedMilliseconds});
                    if(m.Tipo=="LOGIN"&&respuesta.Codigo=="00")
                    {
                        string token=respuesta.Datos.GetProperty("tokenServidor").GetString()!;
                        TokenGenerado?.Invoke(m.Cajero,token);
                        respuesta=respuesta with {Datos=JsonSerializer.SerializeToElement(new {requiereToken=true})};
                    }
                    if(m.Tipo=="RETIRO"&&respuesta.Codigo=="92")
                    {
                        var inv=respuesta.Datos.GetProperty("inventario").EnumerateArray().ToDictionary(x=>x.GetProperty("denominacion").GetInt32(),x=>x.GetProperty("cantidad").GetInt32());
                        int monto=m.Datos.TryGetProperty("monto",out var me)&&me.TryGetInt32(out var valor)?valor:0;
                        var alternativa=Billetes.Alternativa(monto,inv);
                        respuesta=respuesta with {Datos=JsonSerializer.SerializeToElement(new {alternativa})};
                    }
                    await con.Enviar(respuesta,ct);
                    if(reservado&&respuesta.Codigo!="00"){con.Motivo="REGISTRO_RECHAZADO_"+respuesta.Codigo;return;}
                    if(m.Tipo!="HEARTBEAT") Evento?.Invoke($"{m.Cajero} · {m.Tipo} · {respuesta.Codigo}");
                }
            }
        }
        catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or JsonException or KeyNotFoundException or ObjectDisposedException)
        {
            if(con.Motivo=="REMOTO_CERRO")con.Motivo=ct.IsCancellationRequested?"PARADA_SERVIDOR":
                e is OperationCanceledException && con.Codigo==null?"REGISTRO_TIMEOUT":
                e is SocketException se?"SOCKET_"+se.SocketErrorCode:e.GetType().Name;
        }
        finally
        {
            cliente.Dispose();
            DiagnosticoRed.Registrar("SERVIDOR",con.Codigo??"SIN_REGISTRO","CIERRE",new {remoto,motivo=con.Motivo,ultimoTipo=con.UltimoTipo,msSinRecepcion=(long)Stopwatch.GetElapsedTime(con.UltimaRecepcion).TotalMilliseconds,msSinHeartbeat=con.UltimoHeartbeat==0?(long?)null:(long)Stopwatch.GetElapsedTime(con.UltimoHeartbeat).TotalMilliseconds});
            if(con.Codigo!=null && cajeros.TryGetValue(con.Codigo,out var actual)&&ReferenceEquals(actual,con))
            {
                // Mantener la reserva hasta persistir desconexión para no pisar una reconexión nueva.
                try {
                    await bd.Ejecutar("EXEC dbo.sp_Desconectar @c",("@c",con.Codigo));
                    await bd.Auditoria(con.Codigo,"CONEXION_MOTIVO",con.Motivo+"; último tipo "+con.UltimoTipo);
                }catch(Exception){Evento?.Invoke("No se pudo registrar desconexión en SQL.");}
                cajeros.TryRemove(con.Codigo,out _);
                Evento?.Invoke($"{con.Codigo} desconectado.");
            }
        }
    }
    private async Task Vigilar(CancellationToken ct)
    {
        try {while(!ct.IsCancellationRequested)
        {
            await Task.Delay(2000,ct);
            try
            {
                var caducados=await bd.Consultar("SELECT Codigo FROM dbo.Cajero WHERE Conectado=1 AND UltimoHeartbeat<DATEADD(second,-30,SYSDATETIME())");
                foreach(System.Data.DataRow r in caducados.Rows) if(cajeros.TryGetValue((string)r[0],out var c)){c.Motivo="HEARTBEAT_VENCIDO_30S";c.Cliente.Dispose();}
                await bd.Ejecutar("UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE Fin IS NULL AND UltimaActividad<DATEADD(second,-120,SYSDATETIME())");
            }catch(Exception){Evento?.Invoke("Monitor: SQL Server no está disponible.");}
        }}catch(OperationCanceledException){}
    }
    public async Task Comando(string cajero,string comando,string texto="")
    {
        if(cajeros.TryGetValue(cajero,out var c))
            await c.Enviar(Mensaje.Solicitud("COMANDO",cajero,new {comando,texto}),CancellationToken.None);
    }
    public async ValueTask DisposeAsync()
    {
        cancelacion?.Cancel(); listener?.Stop(); listener=null;
        await Interbancario.DisposeAsync();
        foreach(var c in cajeros.Values)c.Motivo="PARADA_SERVIDOR";
        foreach(var s in sockets.Values)s.Dispose();
        try{await Task.WhenAll(tareas.Values).WaitAsync(TimeSpan.FromSeconds(12));}catch(Exception){}
        cancelacion?.Dispose();cancelacion=null;
    }
}
