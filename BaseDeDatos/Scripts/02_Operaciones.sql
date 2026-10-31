USE BD_CajeroRed_Fase2;
GO
CREATE OR ALTER PROCEDURE dbo.sp_Alertas @Cajero varchar(24)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @total decimal(18,2)=(SELECT ISNULL(SUM(Denominacion*Cantidad),0) FROM dbo.InventarioCajero WHERE Cajero=@Cajero);
 IF @total<(SELECT Valor FROM dbo.Parametro WHERE Clave='UmbralEfectivo')
 AND NOT EXISTS(SELECT 1 FROM dbo.Alerta WHERE Cajero=@Cajero AND Tipo='EFECTIVO_BAJO' AND Atendida=0)
 INSERT dbo.Alerta(Tipo,Cajero,Descripcion) VALUES('EFECTIVO_BAJO',@Cajero,N'Efectivo por debajo del umbral.');
 IF EXISTS(SELECT 1 FROM dbo.InventarioCajero WHERE Cajero=@Cajero AND Cantidad=0)
 AND NOT EXISTS(SELECT 1 FROM dbo.Alerta WHERE Cajero=@Cajero AND Tipo='DENOMINACION_AGOTADA' AND Atendida=0)
 INSERT dbo.Alerta(Tipo,Cajero,Descripcion) VALUES('DENOMINACION_AGOTADA',@Cajero,N'Hay denominaciones sin billetes.');
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_Desconectar @Cajero varchar(24)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRAN;
 UPDATE dbo.Cajero SET Conectado=0 WHERE Codigo=@Cajero AND Conectado=1;
 IF @@ROWCOUNT>0
 BEGIN
  INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@Cajero,'DESCONEXION',N'Conexión finalizada o heartbeat vencido.');
  INSERT dbo.Alerta(Tipo,Cajero,Descripcion) VALUES('DESCONECTADO',@Cajero,N'Cajero desconectado.');
  UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE Cajero=@Cajero AND Fin IS NULL;
 END;
 COMMIT;
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_GenerarToken
 @Usuario int, @Tarjeta int, @Cajero varchar(24), @Token varchar(6) OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @v int=(SELECT Valor FROM dbo.Parametro WHERE Clave='VigenciaToken');
 -- CRYPT_GEN_RANDOM evita Random() predecible; el token nunca se persiste en claro.
 SET @Token=RIGHT('000000'+CONVERT(varchar(6),CONVERT(bigint,CRYPT_GEN_RANDOM(4))%1000000),6);
 UPDATE dbo.Token SET Usado=1 WHERE UsuarioId=@Usuario AND Cajero=@Cajero AND Usado=0;
 INSERT dbo.Token(UsuarioId,TarjetaId,Cajero,CodigoHash,Expira)
 VALUES(@Usuario,@Tarjeta,@Cajero,HASHBYTES('SHA2_256',@Token),DATEADD(second,@v,SYSDATETIME()));
END;
GO
-- Motor transaccional compartido. Los procedimientos públicos abajo conservan contratos explícitos.
CREATE OR ALTER PROCEDURE dbo.sp_Operacion
 @Tipo varchar(40), @Json nvarchar(max), @Cajero varchar(24), @Sesion varchar(36)=NULL, @Solicitud varchar(64)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @codigo varchar(2)='00',@mensaje nvarchar(240)=N'Operación aprobada',@datos nvarchar(max)='{}',
 @u int,@t int,@activo bit,@bloq bit,@intentos int,@saldo decimal(18,2),@limite decimal(18,2),@retirado decimal(18,2),
 @hash varbinary(32),@salt varchar(64),@temporal bit,@hoy date=CAST(SYSDATETIME() AS date),@fecha datetime2=SYSDATETIME(),
 @habilitado bit,@inicializado date,@cactivo bit,@monto decimal(18,2),@efectivo decimal(18,2),@id bigint,@token varchar(6),
 @ses varchar(36),@tokenId bigint,@huella varbinary(32)=HASHBYTES('SHA2_256',@Tipo+':'+@Json),@replay bit=0;
 DECLARE @billetes TABLE(Denominacion int PRIMARY KEY,Cantidad int NOT NULL);
 BEGIN TRY
  IF ISJSON(@Json)<>1 THROW 50002,'JSON inválido.',1;
  BEGIN TRAN;
  SELECT @cactivo=Activo,@habilitado=Habilitado,@inicializado=FechaInicializacion
  FROM dbo.Cajero WITH(UPDLOCK,ROWLOCK) WHERE Codigo=@Cajero;
  IF @cactivo IS NULL OR @cactivo=0 BEGIN SET @codigo='91';SET @mensaje=N'Cajero no dado de alta o inactivo.';GOTO Terminar;END;
  IF @Tipo='REGISTRAR_CAJERO'
  BEGIN
   UPDATE dbo.Cajero SET IP=JSON_VALUE(@Json,'$.ip'),Equipo=JSON_VALUE(@Json,'$.equipo'),Version=JSON_VALUE(@Json,'$.version'),
   Conectado=1,UltimoHeartbeat=@fecha,UltimaConexion=@fecha WHERE Codigo=@Cajero;
   INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@Cajero,'CONEXION',N'Cajero registrado o reconectado.');
   SET @datos=(SELECT @habilitado habilitado,CASE WHEN @inicializado=@hoy THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END inicializado FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   GOTO Terminar;
  END;
  IF @Tipo='HEARTBEAT'
  BEGIN
   UPDATE dbo.Cajero SET UltimoHeartbeat=@fecha WHERE Codigo=@Cajero;
   SET @datos=(SELECT @habilitado habilitado,CASE WHEN @inicializado=@hoy THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END inicializado FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   GOTO Terminar;
  END;
  IF @Tipo='LOGOUT'
  BEGIN UPDATE dbo.Sesion SET Fin=@fecha WHERE Id=@Sesion AND Cajero=@Cajero AND Fin IS NULL;GOTO Terminar;END;
  IF @habilitado=0 OR @inicializado IS NULL OR @inicializado<>@hoy
  BEGIN SET @codigo='91';SET @mensaje=N'Cajero fuera de servicio o sin inicializar hoy.';GOTO Terminar;END;
  IF @Tipo IN('LOGIN','VALIDAR_TOKEN')
  BEGIN
   SELECT @t=Id,@u=UsuarioId,@hash=PinHash,@salt=Salt,@temporal=PinTemporal FROM dbo.Tarjeta WITH(UPDLOCK,ROWLOCK)
   WHERE Numero=JSON_VALUE(@Json,'$.tarjeta');
  END
  ELSE
  BEGIN
   SELECT @u=UsuarioId,@t=TarjetaId FROM dbo.Sesion WITH(UPDLOCK,ROWLOCK)
   WHERE Id=@Sesion AND Cajero=@Cajero AND Fin IS NULL AND UltimaActividad>=DATEADD(second,-120,@fecha) AND Resultado='00';
   IF @u IS NOT NULL
   SELECT @temporal=PinTemporal,@hash=PinHash,@salt=Salt FROM dbo.Tarjeta WHERE Id=@t AND Activa=1;
   ELSE BEGIN SET @codigo='57';SET @mensaje=N'Sesión vencida. Inicie sesión nuevamente.';GOTO Terminar;END;
  END;
  SELECT @activo=Activo,@bloq=Bloqueado,@intentos=Intentos,@saldo=Saldo,@limite=LimiteDiario FROM dbo.Usuario WITH(UPDLOCK,ROWLOCK) WHERE Id=@u;
  IF @u IS NULL BEGIN SET @codigo='55';SET @mensaje=N'Tarjeta o credenciales incorrectas.';GOTO AccesoFallido;END;
  IF @activo=0 OR @bloq=1 OR NOT EXISTS(SELECT 1 FROM dbo.Tarjeta WHERE Id=@t AND Activa=1)
  BEGIN SET @codigo='75';SET @mensaje=N'Tarjeta bloqueada o inactiva.';GOTO Terminar;END;
  IF @Tipo='LOGIN'
  BEGIN
   IF LEN(ISNULL(JSON_VALUE(@Json,'$.pin'),''))<>4 OR JSON_VALUE(@Json,'$.pin') LIKE '%[^0-9]%'
    OR HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+JSON_VALUE(@Json,'$.pin')))<>@hash
   BEGIN SET @codigo='55';SET @mensaje=N'PIN incorrecto.';GOTO AccesoFallido;END;
   EXEC dbo.sp_GenerarToken @u,@t,@Cajero,@token OUTPUT;
   SET @datos=(SELECT @token tokenServidor FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   GOTO Terminar;
  END;
  IF @Tipo='VALIDAR_TOKEN'
  BEGIN
   SELECT TOP(1) @tokenId=Id FROM dbo.Token WITH(UPDLOCK,ROWLOCK) WHERE UsuarioId=@u AND TarjetaId=@t AND Cajero=@Cajero
   AND Usado=0 AND Expira>@fecha AND CodigoHash=HASHBYTES('SHA2_256',CONVERT(varchar(6),JSON_VALUE(@Json,'$.token'))) ORDER BY Id DESC;
   IF @tokenId IS NULL OR LEN(ISNULL(JSON_VALUE(@Json,'$.token'),''))<>6
   BEGIN SET @codigo='56';SET @mensaje=N'Token inválido, usado o expirado.';GOTO AccesoFallido;END;
   UPDATE dbo.Token SET Usado=1 WHERE Id=@tokenId;
   UPDATE dbo.Usuario SET Intentos=0 WHERE Id=@u;
   UPDATE dbo.Sesion SET Fin=@fecha WHERE UsuarioId=@u AND Cajero=@Cajero AND Fin IS NULL;
   SET @ses=CONVERT(varchar(36),NEWID());
   INSERT dbo.Sesion(Id,UsuarioId,TarjetaId,Cajero,Resultado) VALUES(@ses,@u,@t,@Cajero,'00');
   INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@Cajero,'ACCESO',CONCAT('Usuario ',@u));
   SET @datos=(SELECT @ses sesion,@u usuarioId,@temporal debeCambiarPin,(SELECT Nombre FROM dbo.Usuario WHERE Id=@u) nombre FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   GOTO Terminar;
  END;
  UPDATE dbo.Sesion SET UltimaActividad=@fecha WHERE Id=@Sesion;
  IF @Tipo='ESTADO_SOLICITUD'
  BEGIN
   SELECT @datos=(SELECT Solicitud idOriginal,UsuarioId usuarioId,Codigo codigo,Mensaje mensaje,JSON_QUERY(Datos) datos FROM dbo.SolicitudOperacion
    WHERE Cajero=@Cajero AND UsuarioId=@u AND Solicitud=JSON_VALUE(@Json,'$.idOriginal') FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   IF NULLIF(@datos,'') IS NULL BEGIN SET @codigo='25';SET @mensaje=N'No existe resultado confirmado. Consulte al administrador antes de repetir.';SET @datos='{}';END;
   GOTO Terminar;
  END;
  IF @Tipo IN('RETIRO','DEPOSITO','CAMBIO_PIN') AND EXISTS(SELECT 1 FROM dbo.SolicitudOperacion WHERE Cajero=@Cajero AND Solicitud=@Solicitud)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.SolicitudOperacion WHERE Cajero=@Cajero AND Solicitud=@Solicitud AND UsuarioId=@u AND Tipo=@Tipo AND Huella=@huella)
   BEGIN SET @codigo='94';SET @mensaje=N'Identificador reutilizado con datos distintos.';GOTO Terminar;END;
   SELECT @codigo=Codigo,@mensaje=Mensaje,@datos=Datos FROM dbo.SolicitudOperacion WHERE Cajero=@Cajero AND Solicitud=@Solicitud;
   SET @replay=1;GOTO Terminar;
  END;
  IF @temporal=1 AND @Tipo NOT IN('CAMBIO_PIN')
  BEGIN SET @codigo='58';SET @mensaje=N'Debe cambiar su PIN temporal antes de operar.';GOTO Terminar;END;
  SELECT @retirado=ISNULL(SUM(Monto),0) FROM dbo.Transaccion WHERE UsuarioId=@u AND Tipo='RETIRO' AND CodigoRespuesta='00' AND Fecha>=@hoy AND Fecha<DATEADD(day,1,@hoy);
  IF @Tipo='CONSULTA_SALDO'
  BEGIN SET @datos=(SELECT @fecha fecha,@saldo saldo,@limite limiteDiario,CASE WHEN @limite>@retirado THEN @limite-@retirado ELSE 0 END disponibleHoy,@retirado retiradoHoy FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);GOTO Terminar;END;
  IF @Tipo='ULTIMAS_TRANSACCIONES'
  BEGIN SET @datos=(SELECT TOP(5) Fecha fecha,Tipo tipo,Monto monto,Cajero cajero FROM dbo.Transaccion WHERE UsuarioId=@u AND CodigoRespuesta='00' ORDER BY Fecha DESC,Id DESC FOR JSON PATH);GOTO Terminar;END;
  IF @Tipo='CAMBIO_PIN'
  BEGIN
   DECLARE @actual varchar(50)=JSON_VALUE(@Json,'$.pinActual'),@nuevo varchar(50)=JSON_VALUE(@Json,'$.pinNuevo');
   IF LEN(ISNULL(@actual,''))<>4 OR HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+@actual))<>@hash
   BEGIN SET @codigo='55';SET @mensaje=N'PIN actual incorrecto.';
    INSERT dbo.CambioPin(UsuarioId,Cajero,Origen,Resultado) VALUES(@u,@Cajero,'USUARIO',@mensaje);GOTO AccesoFallido;END;
   IF LEN(ISNULL(@nuevo,''))<>4 OR @nuevo LIKE '%[^0-9]%' OR @actual=@nuevo
   BEGIN SET @codigo='30';SET @mensaje=N'El nuevo PIN debe tener 4 dígitos y ser distinto al actual.';GOTO Terminar;END;
   SET @salt=CONVERT(varchar(64),CRYPT_GEN_RANDOM(32),2);
   UPDATE dbo.Tarjeta SET Salt=@salt,PinHash=HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+@nuevo)),PinTemporal=0 WHERE Id=@t;
   INSERT dbo.CambioPin(UsuarioId,Cajero,Origen,Resultado) VALUES(@u,@Cajero,'USUARIO','00');
   UPDATE dbo.Token SET Usado=1 WHERE UsuarioId=@u;
   UPDATE dbo.Sesion SET Fin=@fecha WHERE UsuarioId=@u AND Id<>@Sesion AND Fin IS NULL;
   SET @datos=(SELECT @fecha fecha FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   SET @mensaje=N'PIN actualizado en toda la red.';GOTO GuardarResultado;
  END;
  IF @Tipo NOT IN('RETIRO','DEPOSITO') BEGIN SET @codigo='30';SET @mensaje=N'Operación desconocida.';GOTO Terminar;END;
  SET @monto=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.monto'));
  IF @monto IS NULL OR @monto<=0 OR @monto>30000 OR @monto<>FLOOR(@monto)
  BEGIN SET @codigo='30';SET @mensaje=N'Monto inválido; use quetzales enteros entre 1 y 30000.';GOTO Terminar;END;
  IF JSON_QUERY(@Json,'$.desglose') IS NULL BEGIN SET @codigo='30';SET @mensaje=N'Falta desglose.';GOTO Terminar;END;
  IF EXISTS(SELECT 1 FROM OPENJSON(@Json,'$.desglose') WHERE TRY_CONVERT(int,[key]) NOT IN(200,100,50,20,10,5,1)
   OR TRY_CONVERT(int,[key]) IS NULL OR TRY_CONVERT(int,value) IS NULL OR TRY_CONVERT(int,value)<0 OR TRY_CONVERT(int,value)>30000 OR type<>2)
   OR EXISTS(SELECT [key] FROM OPENJSON(@Json,'$.desglose') GROUP BY [key] HAVING COUNT(*)>1)
  BEGIN SET @codigo='30';SET @mensaje=N'Denominaciones o cantidades inválidas.';GOTO Terminar;END;
  INSERT @billetes SELECT CONVERT(int,[key]),CONVERT(int,value) FROM OPENJSON(@Json,'$.desglose');
  IF ISNULL((SELECT SUM(CONVERT(bigint,Denominacion)*Cantidad) FROM @billetes),0)<>@monto
  BEGIN SET @codigo='30';SET @mensaje=N'El desglose no coincide con el monto.';GOTO Terminar;END;
  SELECT @efectivo=SUM(CONVERT(bigint,Denominacion)*Cantidad) FROM dbo.InventarioCajero WITH(UPDLOCK,ROWLOCK) WHERE Cajero=@Cajero;
  IF @Tipo='RETIRO'
  BEGIN
   IF @saldo<@monto BEGIN SET @codigo='51';SET @mensaje=N'Saldo insuficiente.';GOTO RegistrarMovimiento;END;
   IF @retirado+@monto>@limite BEGIN SET @codigo='61';SET @mensaje=N'Excede el límite diario global disponible.';GOTO RegistrarMovimiento;END;
   IF EXISTS(SELECT 1 FROM @billetes b LEFT JOIN dbo.InventarioCajero i ON i.Cajero=@Cajero AND i.Denominacion=b.Denominacion WHERE b.Cantidad>ISNULL(i.Cantidad,0))
   BEGIN
    SET @codigo='92';SET @mensaje=N'No hay billetes para ese desglose.';
    SET @datos=(SELECT JSON_QUERY((SELECT Denominacion denominacion,Cantidad cantidad FROM dbo.InventarioCajero WHERE Cajero=@Cajero FOR JSON PATH)) inventario FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
    INSERT dbo.Alerta(Tipo,Cajero,Descripcion) VALUES('RETIRO_SIN_BILLETES',@Cajero,N'Retiro rechazado por falta de billetes.');
    GOTO RegistrarMovimiento;
   END;
  END
  ELSE IF @efectivo+@monto>(SELECT Valor FROM dbo.Parametro WHERE Clave='MaxEfectivo')
   BEGIN SET @codigo='93';SET @mensaje=N'El depósito supera la capacidad del cajero.';GOTO RegistrarMovimiento;END;
  UPDATE dbo.Usuario SET Saldo=Saldo+CASE WHEN @Tipo='RETIRO' THEN -@monto ELSE @monto END WHERE Id=@u;
  UPDATE i SET Cantidad=i.Cantidad+CASE WHEN @Tipo='RETIRO' THEN -b.Cantidad ELSE b.Cantidad END
  FROM dbo.InventarioCajero i JOIN @billetes b ON i.Denominacion=b.Denominacion WHERE i.Cajero=@Cajero;
  SET @saldo=@saldo+CASE WHEN @Tipo='RETIRO' THEN -@monto ELSE @monto END;
  SET @retirado=@retirado+CASE WHEN @Tipo='RETIRO' THEN @monto ELSE 0 END;
  RegistrarMovimiento:
  INSERT dbo.Transaccion(UsuarioId,TarjetaId,Cajero,Solicitud,Tipo,Monto,CodigoRespuesta,Motivo) VALUES(@u,@t,@Cajero,@Solicitud,@Tipo,@monto,@codigo,@mensaje);
  SET @id=SCOPE_IDENTITY();
  INSERT dbo.TransaccionDetalle SELECT @id,Denominacion,Cantidad FROM @billetes;
  IF @codigo='00'
  SET @datos=(SELECT @id idTransaccion,@saldo saldo,CASE WHEN @limite>@retirado THEN @limite-@retirado ELSE 0 END disponibleHoy,@fecha fecha,@monto monto,@Tipo tipo,@Cajero cajero FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  EXEC dbo.sp_Alertas @Cajero;
  GOTO GuardarResultado;
  AccesoFallido:
  INSERT dbo.Sesion(UsuarioId,TarjetaId,Cajero,Fin,Resultado) VALUES(@u,@t,@Cajero,@fecha,@codigo);
  IF @u IS NOT NULL
  BEGIN
   UPDATE dbo.Usuario SET Intentos=CASE WHEN Intentos<3 THEN Intentos+1 ELSE 3 END,Bloqueado=CASE WHEN Intentos>=2 THEN 1 ELSE Bloqueado END WHERE Id=@u;
   IF @intentos>=2
   BEGIN
    SET @codigo='75';SET @mensaje=N'Tarjeta bloqueada por tres intentos fallidos.';
    INSERT dbo.Alerta(Tipo,Cajero,Descripcion) VALUES('TARJETA_BLOQUEADA',@Cajero,CONCAT('Usuario ',@u,' bloqueado.'));
    UPDATE dbo.Sesion SET Fin=@fecha WHERE UsuarioId=@u AND Fin IS NULL;
   END;
  END;
  GOTO Terminar;
  GuardarResultado:
  INSERT dbo.SolicitudOperacion(Cajero,Solicitud,UsuarioId,Tipo,Huella,Codigo,Mensaje,Datos) VALUES(@Cajero,@Solicitud,@u,@Tipo,@huella,@codigo,@mensaje,@datos);
  Terminar:
  IF @codigo='00' AND @datos='{}' SET @datos=(SELECT @fecha fecha FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  IF @Tipo NOT IN('HEARTBEAT') AND @replay=0
   INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@Cajero,@Tipo,CONCAT('Usuario ',@u,'; resultado ',@codigo,'; solicitud ',@Solicitud));
  COMMIT;
  SELECT @codigo Codigo,@mensaje Mensaje,ISNULL(@datos,'{}') Datos;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  -- El servidor no debe anunciar éxito si SQL no confirmó. Sin datos sensibles en errores.
  SELECT '96' Codigo,N'Error de base de datos; consulte el estado de la solicitud antes de repetir.' Mensaje,'{}' Datos;
 END CATCH;
END;
GO
