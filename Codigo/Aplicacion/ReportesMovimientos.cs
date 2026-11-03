namespace CajeroRed.Aplicacion;

// Solo consultas: reutilizan los registros persistidos sin modificar dinero ni duplicar asientos.
public static class ReportesMovimientos
{
 public const string TransferenciasBase = """
 SELECT v.IdTransferencia,v.Fecha,
 CASE WHEN v.BancoOrigen=v.BancoDestino THEN 'INTERNA' ELSE 'INTERBANCARIA' END Tipo,
 v.Direccion,v.UsuarioId,v.BancoOrigen,bo.NombreBanco NombreBancoOrigen,v.CuentaOrigen,v.NombreOrigen,
 v.BancoDestino,bd.NombreBanco NombreBancoDestino,v.CuentaDestino,v.NombreDestino,
 v.Monto,v.Estado,v.TokenTransferencia,v.Concepto
 FROM dbo.vw_TransferenciasUsuario v
 LEFT JOIN dbo.BancoRed bo ON bo.CodigoBanco=v.BancoOrigen
 LEFT JOIN dbo.BancoRed bd ON bd.CodigoBanco=v.BancoDestino
 WHERE v.BancoOrigen<>v.BancoDestino OR v.Direccion='SALIENTE'
 """;
 public const string Transferencias = "SELECT * FROM ("+TransferenciasBase+") r WHERE Fecha>=@d AND Fecha<@h ORDER BY Fecha DESC,IdTransferencia DESC";
 public const string MovimientosBase = """
 SELECT CONCAT('OP-',t.Id) Id,t.Fecha,t.Cajero,t.Usuario,t.UsuarioId,t.Tarjeta,t.Tipo,t.Monto,
 t.Resultado,t.Motivo,t.Desglose,t.Solicitud,
 CAST(NULL AS varchar(40)) TokenTransferencia,CAST(NULL AS varchar(15)) BancoOrigen,
 CAST(NULL AS varchar(15)) BancoDestino,CAST(NULL AS nvarchar(120)) NombreOrigen,
 CAST(NULL AS nvarchar(120)) NombreDestino,CAST(NULL AS varchar(25)) CuentaOrigen,
 CAST(NULL AS varchar(25)) CuentaDestino,CAST(NULL AS varchar(20)) EstadoTransferencia
 FROM dbo.vw_Transacciones t
 UNION ALL
 SELECT CONCAT('TRF-',v.IdTransferencia),v.Fecha,COALESCE(r.Cajero,'SERVIDOR'),
 u.Nombre,v.UsuarioId,CASE WHEN v.Direccion='ENTRANTE' THEN v.CuentaDestino ELSE v.CuentaOrigen END,
 CASE WHEN v.Direccion='ENTRANTE' THEN 'TRANSFERENCIA_RECIBIDA' ELSE 'TRANSFERENCIA_ENVIADA' END,
 v.Monto,CASE WHEN v.Estado='APROBADA' THEN '00' WHEN v.Estado='RECHAZADA' THEN COALESCE(r.Codigo,'30') ELSE '96' END,
 v.Concepto,'',COALESCE(r.Solicitud,v.TokenTransferencia),v.TokenTransferencia,v.BancoOrigen,v.BancoDestino,
 v.NombreOrigen,v.NombreDestino,v.CuentaOrigen,v.CuentaDestino,v.Estado
 FROM dbo.vw_TransferenciasUsuario v
 JOIN dbo.Usuario u ON u.Id=v.UsuarioId
 LEFT JOIN dbo.TransferenciaCajero r ON r.BancoOrigen=v.BancoOrigen AND r.TokenTransferencia=v.TokenTransferencia
 """;
 public const string Movimientos = "SELECT * FROM ("+MovimientosBase+") r WHERE Fecha>=@d AND Fecha<@h ORDER BY Fecha DESC,Id";
 public const string PorUsuario = "SELECT * FROM ("+MovimientosBase+") r WHERE UsuarioId=@u ORDER BY Fecha DESC,Id";
}
