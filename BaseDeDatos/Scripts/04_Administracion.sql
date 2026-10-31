USE BD_CajeroRed_Fase2;
GO
CREATE OR ALTER TRIGGER dbo.tr_TarjetaBitacora ON dbo.Tarjeta AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.Bitacora(Origen,Accion,Detalle)
 SELECT 'SQL','CAMBIO_TARJETA',CONCAT('Usuario ',i.UsuarioId,'; tarjeta **** ',RIGHT(i.Numero,4),'; activa=',i.Activa)
 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL OR d.Activa<>i.Activa OR d.Numero<>i.Numero;
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_AdminOperacion @Administrador int,@Tipo varchar(40),@Json nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF NOT EXISTS(SELECT 1 FROM dbo.Administrador WHERE Id=@Administrador AND Activo=1 AND Rol='Administrador') THROW 50003,'Administrador no autorizado.',1;
 DECLARE @u int=TRY_CONVERT(int,JSON_VALUE(@Json,'$.usuarioId')),@c varchar(24)=JSON_VALUE(@Json,'$.cajero'),
 @nombre nvarchar(120)=JSON_VALUE(@Json,'$.nombre'),@pin varchar(50)=JSON_VALUE(@Json,'$.pin'),@tarjeta varchar(50)=JSON_VALUE(@Json,'$.tarjeta'),
 @saldo decimal(18,2)=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.saldo')),@limite decimal(18,2)=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.limite')),
 @salt varchar(64)=CONVERT(varchar(64),CRYPT_GEN_RANDOM(32),2),@total decimal(18,2),@anterior decimal(18,2),@id bigint,@fecha date=CAST(SYSDATETIME() AS date);
 BEGIN TRY
 BEGIN TRAN;
 IF @Tipo='CREAR_USUARIO'
 BEGIN
  IF NULLIF(TRIM(@nombre),'') IS NULL OR @saldo IS NULL OR @saldo<0 OR @limite IS NULL OR @limite<0 THROW 50004,'Nombre, saldo o límite inválidos.',1;
  IF LEN(ISNULL(@pin,''))<>4 OR @pin LIKE '%[^0-9]%' OR LEN(ISNULL(@tarjeta,''))<>16 OR @tarjeta LIKE '%[^0-9]%' THROW 50005,'Tarjeta o PIN inválidos.',1;
  INSERT dbo.Usuario(Nombre,Saldo,LimiteDiario) VALUES(@nombre,@saldo,@limite);SET @u=SCOPE_IDENTITY();
  INSERT dbo.Tarjeta(UsuarioId,Numero,PinHash,Salt) VALUES(@u,@tarjeta,HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+@pin)),@salt);
 END
 ELSE IF @Tipo IN('EDITAR_USUARIO','CAMBIAR_LIMITE','CAMBIAR_TARJETA','BLOQUEAR','DESBLOQUEAR','BAJA_USUARIO','RESTABLECER_PIN')
 BEGIN
  SELECT @anterior=LimiteDiario FROM dbo.Usuario WITH(UPDLOCK,ROWLOCK) WHERE Id=@u;
  IF @anterior IS NULL THROW 50006,'Usuario inexistente.',1;
  IF @Tipo='EDITAR_USUARIO'
  BEGIN
   IF NULLIF(TRIM(@nombre),'') IS NULL THROW 50004,'Nombre requerido.',1;
   UPDATE dbo.Usuario SET Nombre=@nombre WHERE Id=@u;
  END;
  IF @Tipo='CAMBIAR_LIMITE'
  BEGIN
   IF @limite IS NULL OR @limite<0 THROW 50004,'Límite inválido.',1;
   INSERT dbo.HistorialLimite(UsuarioId,Anterior,Nuevo,AdministradorId) VALUES(@u,@anterior,@limite,@Administrador);
   UPDATE dbo.Usuario SET LimiteDiario=@limite WHERE Id=@u;
  END;
  IF @Tipo IN('CAMBIAR_TARJETA','RESTABLECER_PIN')
  BEGIN
   IF LEN(ISNULL(@pin,''))<>4 OR @pin LIKE '%[^0-9]%' THROW 50005,'PIN temporal inválido.',1;
   IF @Tipo='CAMBIAR_TARJETA'
   BEGIN
    IF LEN(ISNULL(@tarjeta,''))<>16 OR @tarjeta LIKE '%[^0-9]%' THROW 50005,'Tarjeta inválida.',1;
    UPDATE dbo.Tarjeta SET Activa=0,Baja=SYSDATETIME() WHERE UsuarioId=@u AND Activa=1;
    INSERT dbo.Tarjeta(UsuarioId,Numero,PinHash,Salt,PinTemporal) VALUES(@u,@tarjeta,HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+@pin)),@salt,1);
   END
   ELSE UPDATE dbo.Tarjeta SET Salt=@salt,PinHash=HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+@pin)),PinTemporal=1 WHERE UsuarioId=@u AND Activa=1;
   INSERT dbo.CambioPin(UsuarioId,Origen,Resultado) VALUES(@u,'ADMINISTRADOR','PIN temporal asignado');
  END;
  IF @Tipo='BLOQUEAR' UPDATE dbo.Usuario SET Bloqueado=1 WHERE Id=@u;
  IF @Tipo='DESBLOQUEAR' UPDATE dbo.Usuario SET Bloqueado=0,Intentos=0 WHERE Id=@u;
  IF @Tipo='BAJA_USUARIO'
  BEGIN UPDATE dbo.Usuario SET Activo=0 WHERE Id=@u;UPDATE dbo.Tarjeta SET Activa=0,Baja=SYSDATETIME() WHERE UsuarioId=@u AND Activa=1;END;
  IF @Tipo NOT IN('EDITAR_USUARIO','CAMBIAR_LIMITE')
  BEGIN UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE UsuarioId=@u AND Fin IS NULL;UPDATE dbo.Token SET Usado=1 WHERE UsuarioId=@u;END;
 END
 ELSE IF @Tipo='ALTA_CAJERO'
 BEGIN
  IF NULLIF(TRIM(@c),'') IS NULL OR LEN(@c)>24 OR NULLIF(TRIM(@nombre),'') IS NULL THROW 50007,'Código y nombre de cajero requeridos.',1;
  INSERT dbo.Cajero(Codigo,Nombre,Ubicacion) VALUES(@c,@nombre,ISNULL(JSON_VALUE(@Json,'$.ubicacion'),''));
  INSERT dbo.InventarioCajero SELECT @c,Valor,0 FROM dbo.Denominacion;
 END
 ELSE IF @Tipo IN('EDITAR_CAJERO','BAJA_CAJERO','HABILITAR','FUERA_DE_SERVICIO','INICIALIZAR','CARGAR')
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.Cajero WITH(UPDLOCK,ROWLOCK) WHERE Codigo=@c AND Activo=1) THROW 50008,'Cajero inexistente o inactivo.',1;
  IF @Tipo='EDITAR_CAJERO'
  BEGIN
   IF NULLIF(TRIM(@nombre),'') IS NULL THROW 50007,'Nombre requerido.',1;
   UPDATE dbo.Cajero SET Nombre=@nombre,Ubicacion=ISNULL(JSON_VALUE(@Json,'$.ubicacion'),'') WHERE Codigo=@c;
  END;
  IF @Tipo='BAJA_CAJERO' UPDATE dbo.Cajero SET Activo=0,Habilitado=0 WHERE Codigo=@c;
  IF @Tipo='HABILITAR' UPDATE dbo.Cajero SET Habilitado=1 WHERE Codigo=@c;
  IF @Tipo IN('FUERA_DE_SERVICIO','BAJA_CAJERO')
  BEGIN UPDATE dbo.Cajero SET Habilitado=0 WHERE Codigo=@c;UPDATE dbo.Sesion SET Fin=SYSDATETIME() WHERE Cajero=@c AND Fin IS NULL;END;
  IF @Tipo IN('INICIALIZAR','CARGAR')
  BEGIN
   IF @Tipo='INICIALIZAR' AND EXISTS(SELECT 1 FROM dbo.Cajero WHERE Codigo=@c AND FechaInicializacion=@fecha) THROW 50009,'El cajero ya fue inicializado hoy.',1;
   IF @Tipo='CARGAR' AND NOT EXISTS(SELECT 1 FROM dbo.Cajero WHERE Codigo=@c AND FechaInicializacion=@fecha) THROW 50010,'Inicialice el cajero hoy antes de agregar efectivo.',1;
   IF JSON_QUERY(@Json,'$.desglose') IS NULL THROW 50011,'Falta desglose.',1;
   IF EXISTS(SELECT 1 FROM OPENJSON(@Json,'$.desglose') WHERE TRY_CONVERT(int,[key]) IS NULL OR TRY_CONVERT(int,[key]) NOT IN(200,100,50,20,10,5,1)
    OR TRY_CONVERT(int,value) IS NULL OR TRY_CONVERT(int,value)<0 OR TRY_CONVERT(int,value)>30000 OR type<>2)
    OR EXISTS(SELECT [key] FROM OPENJSON(@Json,'$.desglose') GROUP BY [key] HAVING COUNT(*)>1) THROW 50011,'Desglose inválido.',1;
   DECLARE @b TABLE(d int PRIMARY KEY,n int);
   INSERT @b SELECT CONVERT(int,[key]),CONVERT(int,value) FROM OPENJSON(@Json,'$.desglose');
   SELECT @total=SUM(CONVERT(bigint,d)*n) FROM @b;
   SELECT @anterior=SUM(CONVERT(bigint,Denominacion)*Cantidad) FROM dbo.InventarioCajero WHERE Cajero=@c;
   IF @total IS NULL OR @total<=0 THROW 50011,'Carga vacía.',1;
   IF @Tipo='INICIALIZAR' AND @total>(SELECT Valor FROM dbo.Parametro WHERE Clave='MaxInicializacion') THROW 50012,'Inicialización supera el máximo permitido.',1;
   IF @Tipo='CARGAR' AND @anterior+@total>(SELECT Valor FROM dbo.Parametro WHERE Clave='MaxEfectivo') THROW 50013,'Se supera la capacidad de efectivo.',1;
   INSERT dbo.CargaEfectivo(Cajero,Tipo,Total,EfectivoAnterior,AdministradorId) VALUES(@c,CASE WHEN @Tipo='INICIALIZAR' THEN 'INICIALIZACION' ELSE 'CARGA' END,@total,@anterior,@Administrador);
   SET @id=SCOPE_IDENTITY(); INSERT dbo.CargaEfectivoDetalle SELECT @id,d,n FROM @b;
   UPDATE i SET Cantidad=CASE WHEN @Tipo='INICIALIZAR' THEN ISNULL(b.n,0) ELSE i.Cantidad+ISNULL(b.n,0) END FROM dbo.InventarioCajero i LEFT JOIN @b b ON b.d=i.Denominacion WHERE i.Cajero=@c;
   IF @Tipo='INICIALIZAR' UPDATE dbo.Cajero SET FechaInicializacion=@fecha WHERE Codigo=@c;
   EXEC dbo.sp_Alertas @c;
  END;
 END
 ELSE IF @Tipo='ATENDER_ALERTA'
  UPDATE dbo.Alerta SET Atendida=1,AdministradorId=@Administrador,FechaAtencion=SYSDATETIME() WHERE Id=TRY_CONVERT(bigint,JSON_VALUE(@Json,'$.alertaId')) AND Atendida=0;
 ELSE IF @Tipo='PARAMETROS'
 BEGIN
  DECLARE @clave varchar(40)=JSON_VALUE(@Json,'$.clave'),@valor decimal(18,2)=TRY_CONVERT(decimal(18,2),JSON_VALUE(@Json,'$.valor'));
  IF @valor IS NULL OR @valor<=0 OR @clave NOT IN('Puerto','UmbralEfectivo','VigenciaToken','MaxInicializacion','MaxEfectivo') THROW 50014,'Parámetro inválido.',1;
  IF @clave='Puerto' AND (@valor>65535 OR @valor<>FLOOR(@valor)) THROW 50014,'Puerto inválido.',1;
  IF @clave='VigenciaToken' AND (@valor>120 OR @valor<>FLOOR(@valor)) THROW 50014,'Token: entre 1 y 120 segundos.',1;
  IF @clave='MaxInicializacion' AND @valor>10000 THROW 50014,'El máximo de inicialización no puede superar Q 10000.',1;
  IF @clave='MaxEfectivo' AND @valor>30000 THROW 50014,'La capacidad no puede superar Q 30000.',1;
  IF @clave='MaxInicializacion' AND @valor>(SELECT Valor FROM dbo.Parametro WHERE Clave='MaxEfectivo') THROW 50014,'Inicialización supera capacidad.',1;
  IF @clave='MaxEfectivo' AND (@valor<(SELECT Valor FROM dbo.Parametro WHERE Clave='MaxInicializacion') OR EXISTS(SELECT Cajero FROM dbo.InventarioCajero GROUP BY Cajero HAVING SUM(Denominacion*Cantidad)>@valor)) THROW 50014,'La capacidad es inferior a un inventario o a la inicialización.',1;
  UPDATE dbo.Parametro SET Valor=@valor WHERE Clave=@clave;
 END
 ELSE IF @Tipo='MENSAJE' BEGIN IF NULLIF(JSON_VALUE(@Json,'$.texto'),'') IS NULL THROW 50015,'Mensaje requerido.',1;END
 ELSE THROW 50016,'Acción administrativa desconocida.',1;
 INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(CONCAT('ADMIN-',@Administrador),@Tipo,CONCAT('Usuario ',@u,'; cajero ',@c));
 COMMIT;
 SELECT @u UsuarioId,N'Operación administrativa completada.' Mensaje;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_InicializarCajero @Administrador int,@Json nvarchar(max)
AS EXEC dbo.sp_AdminOperacion @Administrador,'INICIALIZAR',@Json;
GO
CREATE OR ALTER PROCEDURE dbo.sp_AgregarEfectivo @Administrador int,@Json nvarchar(max)
AS EXEC dbo.sp_AdminOperacion @Administrador,'CARGAR',@Json;
GO
CREATE OR ALTER PROCEDURE dbo.sp_CambiarTarjeta @Administrador int,@Json nvarchar(max)
AS EXEC dbo.sp_AdminOperacion @Administrador,'CAMBIAR_TARJETA',@Json;
GO
CREATE OR ALTER PROCEDURE dbo.sp_CambiarLimite @Administrador int,@Json nvarchar(max)
AS EXEC dbo.sp_AdminOperacion @Administrador,'CAMBIAR_LIMITE',@Json;
GO
