using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CajeroRed.Protocolo;
namespace CajeroRed.Aplicacion;

public static class DescubridorServidores
{
 public sealed record Servidor(string Nombre,string IP,int Puerto,bool Compatible)
 {
  public string Estado=>Compatible?"NETGEAR disponible":"Requiere actualizar el servidor";
 }
 public static async Task<IReadOnlyList<Servidor>> Escanear(string? direccionConocida=null,int puerto=5000,CancellationToken ct=default,bool incluirSubred=true)
 {
  var encontrados=new ConcurrentDictionary<string,Servidor>();
  var candidatos=new HashSet<IPAddress>{IPAddress.Loopback};var broadcasts=new HashSet<IPAddress>(incluirSubred?new[]{IPAddress.Broadcast}:[]);
  foreach(var n in NetworkInterface.GetAllNetworkInterfaces().Where(n=>incluirSubred&&n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
  foreach(var a in n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!a.Address.ToString().StartsWith("169.254.")))
  {
   byte[] ip=a.Address.GetAddressBytes(),mask=a.IPv4Mask.GetAddressBytes();
   broadcasts.Add(new IPAddress(ip.Zip(mask,(b,m)=>(byte)(b|~m)).ToArray()));
   candidatos.Add(a.Address);
   // Barrido acotado a una /24 por interfaz, nunca a una /8 de VPN.
   if(mask[0]==255&&mask[1]==255&&mask[2]==255)
    for(int i=1;i<255;i++)candidatos.Add(new IPAddress(new byte[]{ip[0],ip[1],ip[2],(byte)i}));
  }
  if(!string.IsNullOrWhiteSpace(direccionConocida))
  {
   try{foreach(var ip in await Dns.GetHostAddressesAsync(direccionConocida,ct))if(ip.AddressFamily==AddressFamily.InterNetwork)candidatos.Add(ip);}
   catch(SocketException){}
  }
  using var udp=new UdpClient(new IPEndPoint(IPAddress.Any,0)){EnableBroadcast=true};
  foreach(var destino in broadcasts)
  {
   try{await udp.SendAsync(Encoding.UTF8.GetBytes("DISCOVER_BANCO_ATM|CLIENTE|Cajero NETGEAR|0.0.0.0|5000"),new IPEndPoint(destino,5005),ct);}
   catch(SocketException){}
  }
  using(var espera=CancellationTokenSource.CreateLinkedTokenSource(ct))
  {
   espera.CancelAfter(TimeSpan.FromMilliseconds(incluirSubred?2000:1));
   while(!espera.IsCancellationRequested)
   {
    try
    {
     var r=await udp.ReceiveAsync(espera.Token);if(r.Buffer.Length>1024)continue;
     var c=Encoding.UTF8.GetString(r.Buffer).Split('|');
     if(c.Length==7&&c[0]=="HERE_BANCO_ATM")candidatos.Add(r.RemoteEndPoint.Address);
    }catch(OperationCanceledException){break;}
   }
  }
  using var turnos=new SemaphoreSlim(32);
  var puertos=(incluirSubred?new[]{puerto,5000}:new[]{puerto}).Distinct().ToArray();
  await Task.WhenAll(candidatos.SelectMany(ip=>puertos.Select(async port=>
  {
   await turnos.WaitAsync(ct);
   try
   {
    using var limite=CancellationTokenSource.CreateLinkedTokenSource(ct);limite.CancelAfter(TimeSpan.FromMilliseconds(650));
    using var tcp=new TcpClient();await tcp.ConnectAsync(ip,port,limite.Token);
    using var writer=new StreamWriter(tcp.GetStream(),new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
    var solicitud=Mensaje.Solicitud("NETGEAR_INFO","BUSQUEDA");
    await writer.WriteLineAsync(solicitud.Serializar().AsMemory(),limite.Token);
    var bytes=new List<byte>();var uno=new byte[1];
    while(bytes.Count<4096)
    {
     int n=await tcp.GetStream().ReadAsync(uno,limite.Token);if(n==0)return;
     if(uno[0]==10)break;bytes.Add(uno[0]);
    }
    var respuesta=Mensaje.Leer(Encoding.UTF8.GetString(bytes.ToArray()));
    if(respuesta.Codigo!="00"||respuesta.IdSolicitud!=solicitud.IdSolicitud)return;
    var d=respuesta.Datos;
    if(d.TryGetProperty("protocoloCajero",out var protocolo)&&protocolo.GetString()=="NETGEAR-ATM/2.2")
     encontrados[ip+":"+port]=new Servidor(d.GetProperty("nombreBanco").GetString()??"NETGEAR",ip.ToString(),port,true);
   }
   catch(Exception e) when(e is SocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException){}
   finally{turnos.Release();}
  })));
  return encontrados.Values.OrderBy(s=>s.IP=="127.0.0.1"?1:0).ThenBy(s=>s.IP).ToArray();
 }
}
