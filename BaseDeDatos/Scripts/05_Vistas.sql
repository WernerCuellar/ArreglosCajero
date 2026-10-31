USE BD_CajeroRed_Fase2;
GO
CREATE OR ALTER VIEW dbo.vw_RetiradoHoyPorUsuario AS
SELECT UsuarioId, SUM(Monto) RetiradoHoy FROM dbo.Transaccion
WHERE Tipo='RETIRO' AND CodigoRespuesta='00' AND Fecha>=CAST(SYSDATETIME() AS date) AND Fecha<DATEADD(day,1,CAST(SYSDATETIME() AS date)) GROUP BY UsuarioId;
GO
CREATE OR ALTER VIEW dbo.vw_EstadoCajeros AS
SELECT c.Codigo,c.Nombre,c.Ubicacion,c.IP,c.Equipo,c.Version,c.Activo,c.Habilitado,c.Conectado,c.FechaInicializacion,c.UltimoHeartbeat,
 ISNULL(i.Efectivo,0) Efectivo,c.UltimaConexion,t.UltimaTransaccion,
 CASE WHEN c.Activo=0 THEN N'Inactivo' WHEN c.Conectado=0 THEN N'Desconectado' WHEN c.Habilitado=0 THEN N'Fuera de servicio'
 WHEN c.FechaInicializacion IS NULL OR c.FechaInicializacion<>CAST(SYSDATETIME() AS date) THEN N'Sin inicializar'
 WHEN ISNULL(i.Efectivo,0)<(SELECT Valor FROM dbo.Parametro WHERE Clave='UmbralEfectivo') THEN N'Efectivo bajo' ELSE N'En línea' END Estado,
 ISNULL(i.B200,0) B200,ISNULL(i.B100,0) B100,ISNULL(i.B50,0) B50,ISNULL(i.B20,0) B20,ISNULL(i.B10,0) B10,ISNULL(i.B5,0) B5,ISNULL(i.B1,0) B1
FROM dbo.Cajero c
OUTER APPLY(SELECT SUM(Denominacion*Cantidad) Efectivo,SUM(CASE WHEN Denominacion=200 THEN Cantidad ELSE 0 END) B200,
SUM(CASE WHEN Denominacion=100 THEN Cantidad ELSE 0 END) B100,SUM(CASE WHEN Denominacion=50 THEN Cantidad ELSE 0 END) B50,
SUM(CASE WHEN Denominacion=20 THEN Cantidad ELSE 0 END) B20,SUM(CASE WHEN Denominacion=10 THEN Cantidad ELSE 0 END) B10,
SUM(CASE WHEN Denominacion=5 THEN Cantidad ELSE 0 END) B5,SUM(CASE WHEN Denominacion=1 THEN Cantidad ELSE 0 END) B1 FROM dbo.InventarioCajero WHERE Cajero=c.Codigo)i
OUTER APPLY(SELECT MAX(Fecha) UltimaTransaccion FROM dbo.Transaccion WHERE Cajero=c.Codigo)t;
GO
CREATE OR ALTER VIEW dbo.vw_ResumenDiario AS
SELECT (SELECT COUNT(*) FROM dbo.Cajero WHERE Conectado=1 AND Activo=1) CajerosEnLinea,
 (SELECT COUNT(*) FROM dbo.Cajero WHERE Activo=1) TotalCajeros,
 (SELECT ISNULL(SUM(CONVERT(bigint,Denominacion)*Cantidad),0) FROM dbo.InventarioCajero) EfectivoRed,
 ISNULL(SUM(CASE WHEN Tipo='RETIRO' AND CodigoRespuesta='00' THEN Monto ELSE 0 END),0) TotalRetirado,
 ISNULL(SUM(CASE WHEN Tipo='DEPOSITO' AND CodigoRespuesta='00' THEN Monto ELSE 0 END),0) TotalDepositado,
 ISNULL(SUM(CASE WHEN Tipo='DEPOSITO' AND CodigoRespuesta='00' THEN Monto ELSE 0 END)/NULLIF(COUNT(DISTINCT CASE WHEN Tipo='DEPOSITO' AND CodigoRespuesta='00' THEN UsuarioId END),0),0) PromedioDepositadoPorUsuario,
 COUNT(*) Transacciones,SUM(CASE WHEN CodigoRespuesta<>'00' THEN 1 ELSE 0 END) Rechazadas,
 (SELECT COUNT(*) FROM dbo.CambioPin WHERE Fecha>=CAST(SYSDATETIME() AS date)) CambiosPin,
 (SELECT COUNT(*) FROM dbo.Alerta WHERE Atendida=0) AlertasActivas
FROM dbo.Transaccion WHERE Fecha>=CAST(SYSDATETIME() AS date) AND Fecha<DATEADD(day,1,CAST(SYSDATETIME() AS date));
GO
CREATE OR ALTER VIEW dbo.vw_CambiosPinHoy AS
SELECT p.Id,u.Nombre,p.Cajero,p.Fecha,p.Origen,p.Resultado FROM dbo.CambioPin p JOIN dbo.Usuario u ON u.Id=p.UsuarioId WHERE p.Fecha>=CAST(SYSDATETIME() AS date);
GO
CREATE OR ALTER VIEW dbo.vw_Usuarios AS
SELECT u.Id,u.Nombre,u.Saldo,u.LimiteDiario,u.Activo,u.Bloqueado,u.Intentos,
 ISNULL(r.RetiradoHoy,0) RetiradoHoy,CASE WHEN u.LimiteDiario>ISNULL(r.RetiradoHoy,0) THEN u.LimiteDiario-ISNULL(r.RetiradoHoy,0) ELSE 0 END DisponibleHoy,
 CASE WHEN u.Saldo<CASE WHEN u.LimiteDiario>ISNULL(r.RetiradoHoy,0) THEN u.LimiteDiario-ISNULL(r.RetiradoHoy,0) ELSE 0 END THEN u.Saldo
 ELSE CASE WHEN u.LimiteDiario>ISNULL(r.RetiradoHoy,0) THEN u.LimiteDiario-ISNULL(r.RetiradoHoy,0) ELSE 0 END END MaximoRetirable,
 '**** '+RIGHT(t.Numero,4) Tarjeta,t.PinTemporal,a.UltimoAcceso,a.UltimoCajero
FROM dbo.Usuario u LEFT JOIN dbo.Tarjeta t ON t.UsuarioId=u.Id AND t.Activa=1 LEFT JOIN dbo.vw_RetiradoHoyPorUsuario r ON r.UsuarioId=u.Id
OUTER APPLY(SELECT TOP(1) Inicio UltimoAcceso,Cajero UltimoCajero FROM dbo.Sesion WHERE UsuarioId=u.Id AND Resultado='00' ORDER BY Inicio DESC)a;
GO
CREATE OR ALTER VIEW dbo.vw_Transacciones AS
SELECT t.Id,t.Fecha,t.Cajero,u.Nombre Usuario,t.UsuarioId,'**** '+RIGHT(c.Numero,4) Tarjeta,t.Tipo,t.Monto,t.CodigoRespuesta Resultado,t.Motivo,
 ISNULL(d.Desglose,'') Desglose,t.Solicitud FROM dbo.Transaccion t LEFT JOIN dbo.Usuario u ON u.Id=t.UsuarioId LEFT JOIN dbo.Tarjeta c ON c.Id=t.TarjetaId
OUTER APPLY(SELECT STRING_AGG(CONCAT(Denominacion,' x ',Cantidad),'; ') Desglose FROM dbo.TransaccionDetalle WHERE TransaccionId=t.Id)d;
GO
CREATE OR ALTER VIEW dbo.vw_Arqueo AS
SELECT c.Codigo,c.Nombre,ISNULL(a.Total,0) Inicializacion,ISNULL(g.Total,0) CargasAdicionales,ISNULL(m.Depositos,0) Depositos,
 ISNULL(m.Retiros,0) Retiros,ISNULL(a.Total,0)+ISNULL(g.Total,0)+ISNULL(m.Depositos,0)-ISNULL(m.Retiros,0) Esperado,
 ISNULL(i.Efectivo,0) InventarioActual,ISNULL(i.Efectivo,0)-(ISNULL(a.Total,0)+ISNULL(g.Total,0)+ISNULL(m.Depositos,0)-ISNULL(m.Retiros,0)) Diferencia
FROM dbo.Cajero c
OUTER APPLY(SELECT TOP(1) Total,Fecha FROM dbo.CargaEfectivo WHERE Cajero=c.Codigo AND Tipo='INICIALIZACION' ORDER BY Fecha DESC,Id DESC)a
OUTER APPLY(SELECT SUM(Total) Total FROM dbo.CargaEfectivo WHERE Cajero=c.Codigo AND Tipo='CARGA' AND Fecha>=a.Fecha)g
OUTER APPLY(SELECT SUM(CASE WHEN Tipo='DEPOSITO' THEN Monto ELSE 0 END) Depositos,SUM(CASE WHEN Tipo='RETIRO' THEN Monto ELSE 0 END) Retiros FROM dbo.Transaccion WHERE Cajero=c.Codigo AND CodigoRespuesta='00' AND Fecha>=a.Fecha)m
OUTER APPLY(SELECT SUM(Denominacion*Cantidad) Efectivo FROM dbo.InventarioCajero WHERE Cajero=c.Codigo)i;
GO
