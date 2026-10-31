namespace CajeroRed.Aplicacion;

// Contrato anterior conservado como referencia.
// Implementación vigente del MD de bancos: BancoInterbancario.cs.
public enum EstadoInterbancario { Pendiente, Confirmada, Rechazada, RequiereConciliacion }
public sealed record SolicitudInterbancaria(string IdOperacion,string BancoOrigen,string BancoDestino,
    string CuentaOrigen,string CuentaDestino,decimal Monto,string Moneda="GTQ");
public sealed record ResultadoInterbancario(string IdOperacion,EstadoInterbancario Estado,string ReferenciaRemota,string Mensaje);
public interface IConexionInterbancaria
{
    Task<ResultadoInterbancario> Transferir(SolicitudInterbancaria solicitud,CancellationToken cancelacion);
    Task<ResultadoInterbancario> ConsultarEstado(string idOperacion,CancellationToken cancelacion);
}
