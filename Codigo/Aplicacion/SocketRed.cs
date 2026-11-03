using System.Net.Sockets;
namespace CajeroRed.Aplicacion;
internal static class SocketRed
{
 public static void Configurar(TcpClient cliente)
 {
  cliente.NoDelay=true;cliente.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.KeepAlive,true);
  try
  {
   cliente.Client.SetSocketOption(SocketOptionLevel.Tcp,SocketOptionName.TcpKeepAliveTime,10);
   cliente.Client.SetSocketOption(SocketOptionLevel.Tcp,SocketOptionName.TcpKeepAliveInterval,5);
   cliente.Client.SetSocketOption(SocketOptionLevel.Tcp,SocketOptionName.TcpKeepAliveRetryCount,3);
  }
  catch(Exception e) when(e is SocketException or PlatformNotSupportedException){}
 }
}
