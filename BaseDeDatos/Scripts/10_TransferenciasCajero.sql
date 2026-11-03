USE BD_CajeroRed_Fase2;
GO
IF OBJECT_ID('dbo.TransferenciaCajero') IS NULL
CREATE TABLE dbo.TransferenciaCajero(
 Id bigint IDENTITY PRIMARY KEY,Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo),
 Solicitud varchar(64) NOT NULL,UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id),UsuarioDestino int NULL REFERENCES dbo.Usuario(Id),
 SesionOrigen varchar(36) NOT NULL,BancoOrigen varchar(15) NOT NULL REFERENCES dbo.BancoRed(CodigoBanco),
 BancoDestino varchar(15) NOT NULL REFERENCES dbo.BancoRed(CodigoBanco),
 CuentaOrigen varchar(20) NOT NULL,CuentaDestino varchar(20) NOT NULL,NombreOrigen nvarchar(120) NOT NULL,NombreDestino nvarchar(120) NOT NULL,
 Tipo varchar(10) NOT NULL CHECK(Tipo IN('INTERNA','EXTERNA')),Monto decimal(18,2) NOT NULL CHECK(Monto>0),Concepto nvarchar(200) NOT NULL,
 TokenTransferencia varchar(40) NOT NULL UNIQUE,Huella varbinary(32) NOT NULL,
 Estado varchar(20) NOT NULL CHECK(Estado IN('PENDIENTE','RESERVADA','INCIERTA','APROBADA','RECHAZADA')),
 Codigo varchar(2) NOT NULL DEFAULT '96',Mensaje nvarchar(240) NOT NULL DEFAULT N'En proceso.',
 Datos nvarchar(max) NOT NULL DEFAULT '{}',SaldoPosterior decimal(18,2) NULL,
 Fecha datetime2 NOT NULL DEFAULT SYSDATETIME(),UNIQUE(Cajero,Solicitud));
GO
CREATE OR ALTER FUNCTION dbo.fn_CuentaActiva(@Cuenta varchar(20))
RETURNS TABLE AS RETURN
 SELECT u.Id,u.Nombre FROM dbo.Usuario u
 WHERE u.Activo=1 AND u.Bloqueado=0 AND
 (u.NumeroCuenta=@Cuenta OR EXISTS(SELECT 1 FROM dbo.Tarjeta t WHERE t.UsuarioId=u.Id AND t.Numero=@Cuenta AND t.Activa=1));
GO
CREATE OR ALTER VIEW dbo.vw_TransferenciasUsuario AS
SELECT t.IdTransferencia,t.UsuarioId,t.FechaCreacion Fecha,t.TokenTransferencia,t.Direccion,t.BancoOrigen,t.BancoDestino,
 '**** '+RIGHT(t.CuentaOrigen,4) CuentaOrigen,'**** '+RIGHT(t.CuentaDestino,4) CuentaDestino,
 t.NombreOrigen,t.NombreDestino,t.Monto,t.Estado,t.Concepto
FROM dbo.TransferenciaBancaria t
UNION ALL
SELECT -r.Id*2,r.UsuarioId,r.Fecha,r.TokenTransferencia,CAST('SALIENTE' AS varchar(10)),r.BancoOrigen,r.BancoDestino,
 '**** '+RIGHT(r.CuentaOrigen,4),'**** '+RIGHT(r.CuentaDestino,4),r.NombreOrigen,r.NombreDestino,r.Monto,r.Estado,r.Concepto
FROM dbo.TransferenciaCajero r WHERE r.Tipo='INTERNA' OR NOT EXISTS(
 SELECT 1 FROM dbo.TransferenciaBancaria t WHERE t.BancoOrigen=r.BancoOrigen AND t.TokenTransferencia=r.TokenTransferencia)
UNION ALL
SELECT -r.Id*2-1,r.UsuarioDestino,r.Fecha,r.TokenTransferencia,CAST('ENTRANTE' AS varchar(10)),r.BancoOrigen,r.BancoDestino,
 '**** '+RIGHT(r.CuentaOrigen,4),'**** '+RIGHT(r.CuentaDestino,4),r.NombreOrigen,r.NombreDestino,r.Monto,r.Estado,r.Concepto
FROM dbo.TransferenciaCajero r WHERE r.Tipo='INTERNA' AND r.Estado='APROBADA';
GO
CREATE OR ALTER PROCEDURE dbo.sp_TransferenciaCajero
 @Tipo varchar(30),@Cajero varchar(24),@Sesion varchar(36),@Solicitud varchar(64),@Json nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @codigo varchar(2)='00',@mensaje nvarchar(240)=N'Operación aprobada.',@datos nvarchar(max)='{}',@accion varchar(30)='',@ruta nvarchar(max)='{}',
 @u int,@tarjeta int,@propio varchar(15),@banco varchar(15)=UPPER(JSON_VALUE(@Json,'$.bancoDestino')),
 @cuenta varchar(20)=JSON_VALUE(@Json,'$.cuentaDestino'),@m decimal(18,2)=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.monto')),
 @concepto nvarchar(200)=ISNULL(JSON_VALUE(@Json,'$.concepto'),''),@esperado nvarchar(120)=JSON_VALUE(@Json,'$.nombreDestino'),
 @id bigint,@token varchar(40),@estado varchar(20),@tipoTransferencia varchar(10),@origenCuenta varchar(20),@no nvarchar(120),@nd nvarchar(120),
 @dest int,@cantidad int,@saldo decimal(18,2),@huella varbinary(32),@guardada varbinary(32),@lock int,@dummy int,
 @ip varchar(50),@puerto int,@hoy date,@fecha datetime2;
 BEGIN TRY
 BEGIN TRAN;
 EXEC @lock=sys.sp_getapplock @Resource='CajeroRed_Interbancario',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=2000;
 IF @lock<0 THROW 50400,'Operaciones bancarias ocupadas.',1;
 SET @fecha=SYSDATETIME();SET @hoy=CAST(@fecha AS date);
 -- FINALIZAR puede terminar una operación autorizada aunque su sesión se cerrara mientras esperaba al banco.
 IF @Tipo='FINALIZAR'
 BEGIN
  SELECT @id=Id,@u=UsuarioId FROM dbo.TransferenciaCajero WHERE Cajero=@Cajero AND Solicitud=@Solicitud
   AND (SesionOrigen=@Sesion OR EXISTS(SELECT 1 FROM dbo.Sesion s WHERE s.Id=@Sesion AND s.Cajero=@Cajero AND s.UsuarioId=TransferenciaCajero.UsuarioId AND s.Fin IS NULL AND s.UltimaActividad>=DATEADD(second,-120,@fecha)));
  IF @id IS NULL BEGIN SET @codigo='57';SET @mensaje=N'Operación no autorizada.';GOTO Fin;END;
  GOTO Finalizar;
 END;
 SELECT @u=s.UsuarioId,@tarjeta=s.TarjetaId FROM dbo.Sesion s
 JOIN dbo.Cajero c ON c.Codigo=s.Cajero
 JOIN dbo.Tarjeta t ON t.Id=s.TarjetaId AND t.Activa=1
 JOIN dbo.Usuario u ON u.Id=s.UsuarioId AND u.Activo=1 AND u.Bloqueado=0
 WHERE s.Id=@Sesion AND s.Cajero=@Cajero AND s.Fin IS NULL AND s.Resultado='00'
  AND s.UltimaActividad>=DATEADD(second,-120,@fecha) AND c.Activo=1 AND c.Habilitado=1 AND c.FechaInicializacion=@hoy;
 IF @u IS NULL BEGIN SET @codigo='57';SET @mensaje=N'Sesión vencida, tarjeta inactiva o cajero fuera de servicio.';GOTO Fin;END;
 IF EXISTS(SELECT 1 FROM dbo.Tarjeta WHERE Id=@tarjeta AND PinTemporal=1)
 BEGIN SET @codigo='58';SET @mensaje=N'Cambie el PIN temporal antes de transferir.';GOTO Fin;END;
 UPDATE dbo.Sesion SET UltimaActividad=@fecha WHERE Id=@Sesion;
 SELECT @propio=CodigoBanco FROM dbo.BancoRed WHERE EsPropio=1 AND Activo=1;
 IF @propio IS NULL BEGIN SET @codigo='91';SET @mensaje=N'El administrador debe configurar NETGEAR.';GOTO Fin;END;
 IF @Tipo='BANCOS'
 BEGIN
  SET @datos=ISNULL((SELECT CodigoBanco codigoBanco,NombreBanco nombreBanco,EsPropio esPropio FROM dbo.BancoRed
   WHERE Activo=1 AND (EsPropio=1 OR Confiable=1) ORDER BY EsPropio DESC,NombreBanco FOR JSON PATH),'[]');GOTO Fin;
 END;
 IF @Tipo='HISTORIAL'
 BEGIN
  SET @datos=ISNULL((SELECT TOP(30) Fecha fecha,TokenTransferencia tokenTransferencia,Direccion direccion,BancoOrigen bancoOrigen,BancoDestino bancoDestino,
   CuentaOrigen cuentaOrigen,CuentaDestino cuentaDestino,NombreOrigen nombreOrigen,NombreDestino nombreDestino,Monto monto,Estado estado,Concepto concepto
   FROM dbo.vw_TransferenciasUsuario WHERE UsuarioId=@u ORDER BY Fecha DESC,IdTransferencia DESC FOR JSON PATH),'[]');GOTO Fin;
 END;
 IF @Tipo='ESTADO'
 BEGIN
  SELECT @id=Id FROM dbo.TransferenciaCajero WHERE Cajero=@Cajero AND Solicitud=JSON_VALUE(@Json,'$.idOriginal') AND UsuarioId=@u;
  IF @id IS NULL BEGIN SET @codigo='25';SET @mensaje=N'No hay resultado confirmado de ese usuario. No repita el envío.';GOTO Fin;END;
  GOTO Leer;
 END;
 IF @Tipo IN('PREPARAR','DESTINO')
 BEGIN
  IF @banco IS NULL OR @cuenta IS NULL OR LEN(JSON_VALUE(@Json,'$.cuentaDestino')) NOT BETWEEN 1 AND 20 OR @cuenta LIKE '%[^0-9]%'
  BEGIN SET @codigo='30';SET @mensaje=N'Banco o cuenta inválidos.';GOTO Fin;END;
  SELECT @ip=IP,@puerto=Puerto FROM dbo.BancoRed WHERE CodigoBanco=@banco AND Activo=1 AND (EsPropio=1 OR Confiable=1);
  IF @ip IS NULL BEGIN SET @codigo='14';SET @mensaje=N'Banco no disponible o sin autorización del administrador.';GOTO Fin;END;
 END;
 IF @Tipo='DESTINO'
 BEGIN
  IF @banco=@propio
  BEGIN
   SELECT @cantidad=COUNT(*),@dest=MAX(Id),@nd=MAX(Nombre) FROM dbo.fn_CuentaActiva(@cuenta);
   IF @cantidad<>1 BEGIN SET @codigo='14';SET @mensaje=N'Cuenta inexistente, bloqueada o ambigua.';GOTO Fin;END;
   IF @dest=@u BEGIN SET @codigo='30';SET @mensaje=N'Elija la cuenta de otra persona.';GOTO Fin;END;
   SET @datos=(SELECT CAST(1 AS bit) existe,@banco codigoBanco,@nd nombreTitular,'**** '+RIGHT(@cuenta,4) cuentaDestino FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  END
  ELSE
  BEGIN
   SET @accion='VERIFICAR';
   SET @ruta=(SELECT @ip ip,@puerto puerto,@propio bancoOrigen,@banco bancoDestino,@cuenta numeroCuenta FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  END;
  GOTO Fin;
 END;
 IF @Tipo='PREPARAR'
 BEGIN
  IF @m IS NULL OR @m<=0 OR TRY_CONVERT(decimal(38,18),JSON_VALUE(@Json,'$.monto'))<>@m
   OR LEN(ISNULL(JSON_VALUE(@Json,'$.concepto'),''))>200 OR LEN(ISNULL(JSON_VALUE(@Json,'$.nombreDestino'),'')) NOT BETWEEN 1 AND 120
  BEGIN SET @codigo='30';SET @mensaje=N'Monto o concepto inválidos.';GOTO Fin;END;
  SET @huella=HASHBYTES('SHA2_256',CONCAT(@banco,'|',@cuenta,'|',@m,'|',@concepto,'|',@esperado));
  SELECT @id=Id,@guardada=Huella FROM dbo.TransferenciaCajero WHERE Cajero=@Cajero AND Solicitud=@Solicitud;
  IF @id IS NOT NULL
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.TransferenciaCajero WHERE Id=@id AND UsuarioId=@u AND Huella=@huella)
   BEGIN SET @codigo='94';SET @mensaje=N'Referencia ya utilizada con datos distintos.';GOTO Fin;END;
   GOTO Leer;
  END;
  SELECT @saldo=u.Saldo,@origenCuenta=COALESCE(u.NumeroCuenta,t.Numero),@no=u.Nombre FROM dbo.Usuario u JOIN dbo.Tarjeta t ON t.Id=@tarjeta WHERE u.Id=@u;
  IF @saldo<@m BEGIN SET @codigo='51';SET @mensaje=N'Saldo insuficiente.';GOTO Fin;END;
  IF @banco=@propio
  BEGIN
   SELECT @cantidad=COUNT(*),@dest=MAX(Id),@nd=MAX(Nombre) FROM dbo.fn_CuentaActiva(@cuenta);
   IF @cantidad<>1 BEGIN SET @codigo='14';SET @mensaje=N'Cuenta inexistente, bloqueada o ambigua.';GOTO Fin;END;
   IF @dest=@u BEGIN SET @codigo='30';SET @mensaje=N'No se puede transferir a la misma cuenta.';GOTO Fin;END;
   IF @nd<>@esperado BEGIN SET @codigo='30';SET @mensaje=N'El destinatario cambió. Verifique nuevamente.';GOTO Fin;END;
   -- Orden consistente, sin esperar a la red mientras se bloquean saldos.
   SELECT @dummy=Id FROM dbo.Usuario WITH(UPDLOCK,ROWLOCK) WHERE Id IN(@u,@dest) ORDER BY Id;
   IF NOT EXISTS(SELECT 1 FROM dbo.Usuario WHERE Id=@u AND Saldo>=@m AND Activo=1 AND Bloqueado=0)
   BEGIN SET @codigo='51';SET @mensaje=N'Saldo insuficiente.';GOTO Fin;END;
  END;
  WHILE 1=1
  BEGIN
   SET @token='TRF-'+CONVERT(char(8),@fecha,112)+'-'+RIGHT('000000'+CONVERT(varchar(6),CONVERT(bigint,CRYPT_GEN_RANDOM(4))%1000000),6);
   IF NOT EXISTS(SELECT 1 FROM dbo.TransferenciaCajero WHERE TokenTransferencia=@token)
    AND NOT EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@propio AND TokenTransferencia=@token) BREAK;
  END;
  INSERT dbo.TransferenciaCajero(Cajero,Solicitud,UsuarioId,UsuarioDestino,SesionOrigen,BancoOrigen,BancoDestino,CuentaOrigen,CuentaDestino,
   NombreOrigen,NombreDestino,Tipo,Monto,Concepto,TokenTransferencia,Huella,Estado)
  VALUES(@Cajero,@Solicitud,@u,@dest,@Sesion,@propio,@banco,@origenCuenta,@cuenta,@no,COALESCE(@nd,@esperado),
   CASE WHEN @banco=@propio THEN 'INTERNA' ELSE 'EXTERNA' END,@m,@concepto,@token,@huella,'PENDIENTE');
  SET @id=SCOPE_IDENTITY();
  IF @banco=@propio
  BEGIN
   UPDATE dbo.Usuario SET Saldo=Saldo-@m WHERE Id=@u;
   UPDATE dbo.Usuario SET Saldo=Saldo+@m WHERE Id=@dest;
   UPDATE dbo.TransferenciaCajero SET Estado='APROBADA',Codigo='00',Mensaje=N'Transferencia NETGEAR aprobada.',
    SaldoPosterior=(SELECT Saldo FROM dbo.Usuario WHERE Id=@u) WHERE Id=@id;
   INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@Cajero,'TRANSFERENCIA_INTERNA',@token);
   GOTO Recibo;
  END;
  SET @accion='VERIFICAR_ENVIO';
  SET @ruta=(SELECT @ip ip,@puerto puerto,@propio bancoOrigen,@banco bancoDestino,@cuenta numeroCuenta,@esperado nombreDestino FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  SET @codigo='96';SET @mensaje=N'Transferencia en validación.';GOTO Fin;
 END;
 IF @Tipo='RESERVAR'
 BEGIN
  SELECT @id=Id,@token=TokenTransferencia,@banco=BancoDestino,@m=Monto FROM dbo.TransferenciaCajero WHERE Cajero=@Cajero AND Solicitud=@Solicitud AND UsuarioId=@u;
  IF @id IS NULL BEGIN SET @codigo='25';SET @mensaje=N'Solicitud no encontrada.';GOTO Fin;END;
  IF NOT EXISTS(SELECT 1 FROM dbo.TransferenciaCajero WHERE Id=@id AND Estado='PENDIENTE') GOTO Leer;
  SELECT @ip=IP,@puerto=Puerto FROM dbo.BancoRed WHERE CodigoBanco=@banco AND Activo=1 AND Confiable=1 AND EsPropio=0;
  IF @ip IS NULL BEGIN SET @codigo='57';SET @mensaje=N'Banco destino no autorizado.';GOTO RechazarPrevio;END;
  UPDATE dbo.BancoRed SET TransferidoHoy=0,FechaUltimoReinicio=@hoy WHERE FechaUltimoReinicio<>@hoy;
  SELECT @saldo=Saldo FROM dbo.Usuario WITH(UPDLOCK,ROWLOCK) WHERE Id=@u;
  IF @saldo<@m BEGIN SET @codigo='51';SET @mensaje=N'Saldo insuficiente.';GOTO RechazarPrevio;END;
  IF EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco IN(@propio,@banco) AND LimiteDiario-TransferidoHoy<@m)
  BEGIN SET @codigo='65';SET @mensaje=N'Cupo diario bancario insuficiente.';GOTO RechazarPrevio;END;
  UPDATE dbo.Usuario SET Saldo=Saldo-@m WHERE Id=@u;
  UPDATE dbo.BancoRed SET TransferidoHoy=TransferidoHoy+@m WHERE CodigoBanco IN(@propio,@banco);
  INSERT dbo.TransferenciaBancaria(TokenTransferencia,Direccion,BancoOrigen,BancoDestino,UsuarioId,CuentaOrigen,CuentaDestino,NombreOrigen,NombreDestino,Monto,Concepto,Estado,Huella)
  SELECT TokenTransferencia,'SALIENTE',BancoOrigen,BancoDestino,UsuarioId,CuentaOrigen,CuentaDestino,NombreOrigen,NombreDestino,Monto,Concepto,'RESERVADA',Huella FROM dbo.TransferenciaCajero WHERE Id=@id;
  UPDATE dbo.TransferenciaCajero SET Estado='RESERVADA',SaldoPosterior=(SELECT Saldo FROM dbo.Usuario WHERE Id=@u) WHERE Id=@id;
  SET @accion='ENVIAR';
  SET @ruta=(SELECT @ip ip,@puerto puerto,'IB_TRANSFERIR' tipo,'SERVER' cajero,REPLACE(CONVERT(varchar(36),NEWID()),'-','') idMensaje,
    CONVERT(varchar(19),@fecha,120) timestamp,TokenTransferencia tokenTransferencia,BancoOrigen bancoOrigen,BancoDestino bancoDestino,
    CuentaOrigen cuentaOrigen,CuentaDestino cuentaDestino,NombreOrigen nombreOrigen,NombreDestino nombreDestino,Monto monto,Concepto concepto FROM dbo.TransferenciaCajero WHERE Id=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  SET @codigo='96';SET @mensaje=N'Enviando transferencia.';GOTO Fin;
 END;
 SET @codigo='30';SET @mensaje=N'Operación no reconocida.';GOTO Fin;
 RechazarPrevio:
 UPDATE dbo.TransferenciaCajero SET Estado='RECHAZADA',Codigo=@codigo,Mensaje=@mensaje,SaldoPosterior=(SELECT Saldo FROM dbo.Usuario WHERE Id=@u) WHERE Id=@id;
 GOTO Recibo;
 Finalizar:
 SELECT @token=TokenTransferencia,@estado=Estado,@m=Monto,@propio=BancoOrigen,@banco=BancoDestino FROM dbo.TransferenciaCajero WHERE Id=@id;
 IF EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@propio AND TokenTransferencia=@token AND Estado='APROBADA')
 BEGIN
  UPDATE dbo.TransferenciaCajero SET Estado='APROBADA',Codigo='00',Mensaje=N'Transferencia confirmada por el banco destino.' WHERE Id=@id;
  SET @estado='APROBADA';
 END;
 IF @estado IN('APROBADA','RECHAZADA') GOTO Recibo;
 SET @codigo=ISNULL(JSON_VALUE(@Json,'$.codigo'),'96');
 SET @mensaje=LEFT(COALESCE(JSON_VALUE(@Json,'$.mensaje'),N'Transferencia sin confirmar.'),240);
 IF EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@propio AND TokenTransferencia=@token)
 BEGIN
  IF @codigo='00' SET @estado='APROBADA';
  ELSE IF @codigo IN('14','65','57','30')
  BEGIN
   SET @estado='RECHAZADA';
   IF EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@propio AND TokenTransferencia=@token AND Estado IN('RESERVADA','INCIERTA'))
   BEGIN
    UPDATE dbo.Usuario SET Saldo=Saldo+@m WHERE Id=@u;
    UPDATE dbo.BancoRed SET TransferidoHoy=TransferidoHoy-@m WHERE CodigoBanco IN(@propio,@banco)
     AND FechaUltimoReinicio=CAST((SELECT Fecha FROM dbo.TransferenciaCajero WHERE Id=@id) AS date);
   END;
  END
  ELSE BEGIN SET @estado='INCIERTA';SET @codigo='96';END;
  UPDATE dbo.TransferenciaBancaria SET Estado=@estado,FechaActualizacion=@fecha WHERE BancoOrigen=@propio AND TokenTransferencia=@token;
 END
 ELSE
 BEGIN
  -- Nunca se envió IB_TRANSFERIR: se puede rechazar sin devolver fondos inexistentes.
  SET @estado='RECHAZADA';IF @codigo IN('00','96')SET @codigo='91';
 END;
 UPDATE dbo.TransferenciaCajero SET Estado=@estado,Codigo=@codigo,Mensaje=@mensaje WHERE Id=@id;
 INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@Cajero,'TRANSFERENCIA_'+@estado,@token);
 GOTO Recibo;
 Leer:
 SELECT @estado=r.Estado,@token=r.TokenTransferencia,@propio=r.BancoOrigen,@banco=r.BancoDestino,@m=r.Monto FROM dbo.TransferenciaCajero r WHERE Id=@id;
 IF @estado='PENDIENTE' AND EXISTS(SELECT 1 FROM dbo.TransferenciaCajero WHERE Id=@id AND Fecha<DATEADD(second,-40,@fecha))
 BEGIN
  UPDATE dbo.TransferenciaCajero SET Estado='RECHAZADA',Codigo='91',Mensaje=N'Cancelada antes del envío. No se cargó dinero.' WHERE Id=@id;
 END;
 IF EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria t WHERE t.BancoOrigen=@propio AND t.TokenTransferencia=@token AND t.Estado IN('APROBADA','RECHAZADA'))
 BEGIN
  UPDATE r SET Estado=t.Estado,Codigo=CASE WHEN t.Estado='APROBADA' THEN '00' ELSE '14' END,Mensaje=N'Resultado confirmado por el banco.'
  FROM dbo.TransferenciaCajero r JOIN dbo.TransferenciaBancaria t ON t.BancoOrigen=r.BancoOrigen AND t.TokenTransferencia=r.TokenTransferencia
  WHERE r.Id=@id AND r.Estado NOT IN('APROBADA','RECHAZADA');
 END;
 IF @Tipo='ESTADO' AND EXISTS(SELECT 1 FROM dbo.TransferenciaCajero WHERE Id=@id AND Estado IN('RESERVADA','INCIERTA'))
 BEGIN
  SELECT @ip=IP,@puerto=Puerto FROM dbo.BancoRed WHERE CodigoBanco=@banco AND Activo=1 AND Confiable=1;
  IF @ip IS NOT NULL
  BEGIN
   SET @accion='CONCILIAR';
   SET @ruta=(SELECT @ip ip,@puerto puerto,'IB_ESTADO_TRANSFERENCIA' tipo,REPLACE(CONVERT(varchar(36),NEWID()),'-','') idMensaje,
    @propio bancoOrigen,@banco bancoDestino,@token tokenTransferencia,@m monto,CuentaDestino cuentaDestino FROM dbo.TransferenciaCajero WHERE Id=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  END;
 END;
 Recibo:
 SELECT @codigo=Codigo,@mensaje=Mensaje,@estado=Estado FROM dbo.TransferenciaCajero WHERE Id=@id;
 IF @estado IN('APROBADA','RECHAZADA') AND EXISTS(SELECT 1 FROM dbo.TransferenciaCajero WHERE Id=@id AND Datos='{}')
 BEGIN
  UPDATE r SET Datos=(SELECT r.TokenTransferencia tokenTransferencia,r.Estado estado,r.Monto monto,r.BancoDestino bancoDestino,
   r.NombreDestino nombreDestino,'**** '+RIGHT(r.CuentaDestino,4) cuentaDestino,r.Concepto concepto,r.Fecha fecha,
   COALESCE(r.SaldoPosterior,(SELECT Saldo FROM dbo.Usuario WHERE Id=r.UsuarioId)) saldo FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) FROM dbo.TransferenciaCajero r WHERE r.Id=@id;
 END;
 SELECT @datos=CASE WHEN Estado IN('APROBADA','RECHAZADA') THEN Datos ELSE
  (SELECT TokenTransferencia tokenTransferencia,Estado estado,Monto monto,Fecha fecha FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END FROM dbo.TransferenciaCajero WHERE Id=@id;
 IF @estado NOT IN('APROBADA','RECHAZADA') BEGIN SET @codigo='96';SET @mensaje=N'Transferencia pendiente. No repita el envío.';END;
 IF @Tipo='ESTADO' AND @estado IN('APROBADA','RECHAZADA')
 BEGIN
  SET @datos=(SELECT Solicitud idOriginal,UsuarioId usuarioId,Codigo codigo,Mensaje mensaje,JSON_QUERY(Datos) datos FROM dbo.TransferenciaCajero WHERE Id=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  SET @codigo='00';SET @mensaje=N'Resultado confirmado.';
 END;
 Fin:
 COMMIT;
 SELECT @codigo Codigo,@mensaje Mensaje,@datos Datos,@accion Accion,@ruta Ruta;
 END TRY
 BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
 END CATCH;
END;
GO

-- Recepción externa: rechazar identificadores ambiguos.
CREATE OR ALTER PROCEDURE dbo.sp_IB_Operacion
 @Tipo varchar(40),@Json nvarchar(max),@IP varchar(50)='',@Administrador int=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 DECLARE @codigo varchar(2)='00',@mensaje nvarchar(240)=N'Operación aprobada.',@datos nvarchar(max)='{}',
 @propio varchar(15),@nombre nvarchar(100),@bo varchar(15)=JSON_VALUE(@Json,'$.bancoOrigen'),
 @bd varchar(15)=JSON_VALUE(@Json,'$.bancoDestino'),@token varchar(40)=JSON_VALUE(@Json,'$.tokenTransferencia'),
 @co varchar(20)=JSON_VALUE(@Json,'$.cuentaOrigen'),@cd varchar(20)=COALESCE(JSON_VALUE(@Json,'$.cuentaDestino'),JSON_VALUE(@Json,'$.numeroCuenta')),
 @no nvarchar(120)=ISNULL(JSON_VALUE(@Json,'$.nombreOrigen'),''),@nd nvarchar(120),
 @concepto nvarchar(200)=ISNULL(JSON_VALUE(@Json,'$.concepto'),''),
 @m decimal(18,2)=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.monto')),
 @u int=TRY_CONVERT(int,JSON_VALUE(@Json,'$.usuarioId')),@hoy date=CAST(SYSDATETIME() AS date),
 @huella varbinary(32),@anterior varbinary(32),@estado varchar(20),@id bigint,@lock int;
 BEGIN TRY
 BEGIN TRAN;
 EXEC @lock=sys.sp_getapplock @Resource='CajeroRed_Interbancario',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=2000;
 IF @lock<0 THROW 50200,'Integración ocupada; reintente.',1;
 IF @Tipo IN('CONFIGURAR','ASIGNAR_CUENTA','RESERVAR','FINALIZAR')
 AND NOT EXISTS(SELECT 1 FROM dbo.Administrador WHERE Id=@Administrador AND Activo=1 AND Rol='Administrador')
 THROW 50201,'Administrador no autorizado.',1;
 IF @Tipo='CONFIGURAR'
 BEGIN
  DECLARE @c varchar(15)=JSON_VALUE(@Json,'$.codigoBanco'),@n nvarchar(100)=JSON_VALUE(@Json,'$.nombreBanco'),
  @ipb varchar(50)=JSON_VALUE(@Json,'$.ip'),@p int=TRY_CONVERT(int,JSON_VALUE(@Json,'$.puerto')),
  @es bit=TRY_CONVERT(bit,JSON_VALUE(@Json,'$.esPropio')),@conf bit=TRY_CONVERT(bit,JSON_VALUE(@Json,'$.confiable')),
  @lim decimal(18,2)=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.limiteDiario'));
  IF NULLIF(@c,'') IS NULL OR @c LIKE '%[^A-Z0-9_-]%' OR NULLIF(TRIM(@n),'') IS NULL OR NULLIF(@ipb,'') IS NULL
   OR @p IS NULL OR @p NOT BETWEEN 1 AND 65535 OR @es IS NULL OR @conf IS NULL OR @lim IS NULL OR @lim<=0
   THROW 50202,'Configuración bancaria inválida.',1;
  IF @es=1 AND EXISTS(SELECT 1 FROM dbo.BancoRed WHERE EsPropio=1 AND CodigoBanco<>@c)
  BEGIN
   IF EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria) OR EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco=@c)
    THROW 50203,'No se puede cambiar un código con historial ni usar uno ya registrado.',1;
   UPDATE dbo.BancoRed SET CodigoBanco=@c WHERE EsPropio=1;
  END;
  IF EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco=@c AND EsPropio<>@es)
   THROW 50204,'No se puede cambiar la propiedad de un banco existente.',1;
  IF EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco=@c AND FechaUltimoReinicio=@hoy AND TransferidoHoy>@lim)
   THROW 50205,'El límite no puede ser menor al cupo ya consumido.',1;
  IF EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco=@c)
   UPDATE dbo.BancoRed SET NombreBanco=@n,IP=@ipb,Puerto=@p,Confiable=@conf,LimiteDiario=@lim,Activo=1 WHERE CodigoBanco=@c;
  ELSE INSERT dbo.BancoRed(CodigoBanco,NombreBanco,IP,Puerto,EsPropio,Confiable,LimiteDiario) VALUES(@c,@n,@ipb,@p,@es,@conf,@lim);
  INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(CONCAT('ADMIN-',@Administrador),'IB_CONFIGURAR',CONCAT('Banco ',@c));
  GOTO Fin;
 END;
 IF @Tipo='ASIGNAR_CUENTA'
 BEGIN
  SET @cd=JSON_VALUE(@Json,'$.numeroCuenta');
  IF @cd IS NULL OR LEN(@cd) NOT BETWEEN 1 AND 20 OR @cd LIKE '%[^0-9]%' THROW 50206,'Cuenta inválida: 1 a 20 dígitos.',1;
  -- Evita que una cuenta de otro usuario se confunda con una tarjeta.
  IF EXISTS(SELECT 1 FROM dbo.Tarjeta WHERE Numero=@cd AND UsuarioId<>@u) THROW 50207,'La cuenta coincide con la tarjeta de otro usuario.',1;
  UPDATE dbo.Usuario SET NumeroCuenta=@cd WHERE Id=@u AND Activo=1;
  IF @@ROWCOUNT=0 THROW 50208,'Usuario no disponible.',1;
  INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(CONCAT('ADMIN-',@Administrador),'IB_ASIGNAR_CUENTA',CONCAT('Usuario ',@u));
  GOTO Fin;
 END;
 SELECT @propio=CodigoBanco,@nombre=NombreBanco FROM dbo.BancoRed WHERE EsPropio=1 AND Activo=1;
 IF @propio IS NULL BEGIN SET @codigo='91';SET @mensaje=N'Configure primero el banco propio.';GOTO Fin;END;
 UPDATE dbo.BancoRed SET TransferidoHoy=0,FechaUltimoReinicio=@hoy WHERE FechaUltimoReinicio<>@hoy;
 IF @Tipo='IB_PING'
 BEGIN
  SET @datos=(SELECT CodigoBanco codigoBanco,NombreBanco nombreBanco,LimiteDiario-TransferidoHoy cupoDisponibleHoy,'EN_LINEA' estado FROM dbo.BancoRed WHERE CodigoBanco=@propio FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  SET @mensaje=N'Banco '+@propio+N' en línea';GOTO Fin;
 END;
 IF @Tipo IN('IB_VERIFICAR_CUENTA','IB_TRANSFERIR','IB_ESTADO_TRANSFERENCIA') AND ISNULL(@bd,@propio)<>@propio
 BEGIN SET @codigo='14';SET @mensaje=N'Banco destino incorrecto.';GOTO Fin;END;
 IF @Tipo='IB_VERIFICAR_CUENTA'
 BEGIN
  SET @u=NULL;
  IF (SELECT COUNT(*) FROM dbo.fn_CuentaActiva(@cd))=1
   SELECT @u=u.Id,@nd=u.Nombre FROM dbo.Usuario u WITH(UPDLOCK,ROWLOCK) JOIN dbo.fn_CuentaActiva(@cd) f ON f.Id=u.Id;
  SET @datos=(SELECT CAST(CASE WHEN @u IS NULL THEN 0 ELSE 1 END AS bit) existe,@propio codigoBanco,@nombre nombreBanco,
   @cd numeroCuenta,ISNULL(@nd,'') nombreTitular,LimiteDiario-TransferidoHoy cupoDisponibleBanco FROM dbo.BancoRed WHERE CodigoBanco=@propio FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  IF @u IS NULL BEGIN SET @codigo='14';SET @mensaje=N'La cuenta no existe en este banco.';END;
  GOTO Fin;
 END;
 IF @Tipo IN('IB_TRANSFERIR','IB_ESTADO_TRANSFERENCIA')
 AND NOT EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco=@bo AND EsPropio=0 AND Activo=1 AND Confiable=1 AND IP=@IP)
 BEGIN SET @codigo='57';SET @mensaje=N'Banco origen o IP no autorizado.';GOTO Fin;END;
 IF @Tipo='IB_ESTADO_TRANSFERENCIA'
 BEGIN
  SELECT @estado=Estado,@datos=RespuestaDatos FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@bo AND TokenTransferencia=@token AND Direccion='ENTRANTE';
  IF @estado IS NULL BEGIN SET @codigo='25';SET @mensaje=N'Transferencia no registrada; no confirma rechazo ni autoriza devolución.';END;
  GOTO Fin;
 END;
 IF @Tipo='IB_TRANSFERIR'
 BEGIN
  IF @token IS NULL OR LEN(@token)<>19 OR @token NOT LIKE 'TRF-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'
   OR @m IS NULL OR @m<=0 OR @co IS NULL OR @cd IS NULL OR @bo=@propio
  BEGIN SET @codigo='30';SET @mensaje=N'Token, cuentas o monto inválidos.';GOTO Fin;END;
  SET @huella=HASHBYTES('SHA2_256',CONCAT(@bo,'|',@bd,'|',@co,'|',@cd,'|',@m,'|',@no,'|',@concepto));
  SELECT @anterior=Huella,@datos=RespuestaDatos FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@bo AND TokenTransferencia=@token;
  IF @anterior IS NOT NULL
  BEGIN
   IF @anterior<>@huella BEGIN SET @codigo='94';SET @mensaje=N'Token reutilizado con contenido distinto.';SET @datos='{}';END;
   GOTO Fin;
  END;
  SET @u=NULL;
  SET @u=NULL;
  IF (SELECT COUNT(*) FROM dbo.fn_CuentaActiva(@cd))=1
   SELECT @u=u.Id,@nd=u.Nombre FROM dbo.Usuario u WITH(UPDLOCK,ROWLOCK) JOIN dbo.fn_CuentaActiva(@cd) f ON f.Id=u.Id;
  IF @u IS NULL BEGIN SET @codigo='14';SET @mensaje=N'La cuenta no existe en este banco.';GOTO Fin;END;
  IF EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco IN(@propio,@bo) AND LimiteDiario-TransferidoHoy<@m)
  BEGIN SET @codigo='65';SET @mensaje=N'Cupo diario bancario insuficiente.';GOTO Fin;END;
  UPDATE dbo.Usuario SET Saldo=Saldo+@m WHERE Id=@u;
  UPDATE dbo.BancoRed SET TransferidoHoy=TransferidoHoy+@m WHERE CodigoBanco IN(@propio,@bo);
  SET @datos=(SELECT @token tokenTransferencia,@propio bancoDestino,@cd cuentaDestino,@nd nombreDestino,@m monto,LimiteDiario-TransferidoHoy cupoRestanteBancoDestino FROM dbo.BancoRed WHERE CodigoBanco=@propio FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.TransferenciaBancaria(TokenTransferencia,Direccion,BancoOrigen,BancoDestino,UsuarioId,CuentaOrigen,CuentaDestino,NombreOrigen,NombreDestino,Monto,Concepto,Estado,Huella,RespuestaDatos)
  VALUES(@token,'ENTRANTE',@bo,@propio,@u,@co,@cd,@no,@nd,@m,@concepto,'APROBADA',@huella,@datos);
  INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@bo,'IB_RECIBIDA',@token);
  SET @mensaje=N'Transferencia interbancaria acreditada y token registrado.';GOTO Fin;
 END;
 IF @Tipo='RESERVAR'
 BEGIN
  IF @m IS NULL OR @m<=0 OR @bd=@propio OR NOT EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco=@bd AND Activo=1 AND EsPropio=0 AND Confiable=1)
   THROW 50209,'Monto o banco destino no autorizado.',1;
  SELECT @co=COALESCE(u.NumeroCuenta,t.Numero),@no=u.Nombre FROM dbo.Usuario u WITH(UPDLOCK,ROWLOCK)
  LEFT JOIN dbo.Tarjeta t ON t.UsuarioId=u.Id AND t.Activa=1 WHERE u.Id=@u AND u.Activo=1 AND u.Bloqueado=0 AND u.Saldo>=@m;
  IF @co IS NULL BEGIN SET @codigo='51';SET @mensaje=N'Usuario sin cuenta activa o saldo insuficiente.';GOTO Fin;END;
  IF EXISTS(SELECT 1 FROM dbo.BancoRed WHERE CodigoBanco IN(@propio,@bd) AND LimiteDiario-TransferidoHoy<@m)
  BEGIN SET @codigo='65';SET @mensaje=N'Cupo diario bancario insuficiente.';GOTO Fin;END;
  IF EXISTS(SELECT 1 FROM dbo.TransferenciaBancaria WHERE BancoOrigen=@propio AND TokenTransferencia=@token)
  BEGIN SET @codigo='94';SET @mensaje=N'Token ya registrado.';GOTO Fin;END;
  SET @nd=ISNULL(JSON_VALUE(@Json,'$.nombreDestino'),'');
  UPDATE dbo.Usuario SET Saldo=Saldo-@m WHERE Id=@u;
  UPDATE dbo.BancoRed SET TransferidoHoy=TransferidoHoy+@m WHERE CodigoBanco IN(@propio,@bd);
  SET @huella=HASHBYTES('SHA2_256',CONCAT(@propio,'|',@bd,'|',@co,'|',@cd,'|',@m,'|',@no,'|',@concepto));
  INSERT dbo.TransferenciaBancaria(TokenTransferencia,Direccion,BancoOrigen,BancoDestino,UsuarioId,CuentaOrigen,CuentaDestino,NombreOrigen,NombreDestino,Monto,Concepto,Estado,Huella)
  VALUES(@token,'SALIENTE',@propio,@bd,@u,@co,@cd,@no,@nd,@m,@concepto,'RESERVADA',@huella);
  SET @datos=(SELECT 'IB_TRANSFERIR' tipo,'SERVER' cajero,REPLACE(CONVERT(varchar(36),NEWID()),'-','') idMensaje,
   CONVERT(varchar(19),SYSDATETIME(),120) timestamp,@token tokenTransferencia,@propio bancoOrigen,@co cuentaOrigen,@no nombreOrigen,
   @bd bancoDestino,@cd cuentaDestino,@nd nombreDestino,@m monto,@concepto concepto FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(CONCAT('ADMIN-',@Administrador),'IB_RESERVADA',@token);
  GOTO Fin;
 END;
 IF @Tipo='FINALIZAR'
 BEGIN
  SELECT @id=IdTransferencia,@estado=Estado,@u=UsuarioId,@m=Monto,@bo=BancoOrigen,@bd=BancoDestino FROM dbo.TransferenciaBancaria
   WHERE TokenTransferencia=@token AND BancoOrigen=@propio AND Direccion='SALIENTE';
  IF @id IS NULL THROW 50210,'Transferencia saliente no encontrada.',1;
  IF @estado IN('APROBADA','RECHAZADA') GOTO Fin;
  DECLARE @resultado varchar(2)=JSON_VALUE(@Json,'$.codigo');
  IF @resultado='00' SET @estado='APROBADA';
  ELSE IF @resultado IN('14','65','57','30')
  BEGIN
   SET @estado='RECHAZADA';
   UPDATE dbo.Usuario SET Saldo=Saldo+@m WHERE Id=@u;
   UPDATE dbo.BancoRed SET TransferidoHoy=TransferidoHoy-@m
   WHERE CodigoBanco IN(@bo,@bd) AND FechaUltimoReinicio=CAST((SELECT FechaCreacion FROM dbo.TransferenciaBancaria WHERE IdTransferencia=@id) AS date);
  END
  ELSE SET @estado='INCIERTA';
  UPDATE dbo.TransferenciaBancaria SET Estado=@estado,FechaActualizacion=SYSDATETIME() WHERE IdTransferencia=@id;
  INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(CONCAT('ADMIN-',@Administrador),'IB_'+@estado,@token);
  SET @datos=(SELECT @token tokenTransferencia,@estado estado FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  GOTO Fin;
 END;
 SET @codigo='30';SET @mensaje=N'Operación interbancaria no soportada.';
 Fin:
 COMMIT;
 SELECT @codigo Codigo,@mensaje Mensaje,@datos Datos,CONVERT(varchar(19),SYSDATETIME(),120) Fecha;
 END TRY
 BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
 END CATCH;
END;
GO
