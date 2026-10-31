USE BD_CajeroRed_Fase2;
GO
-- Migración aditiva y repetible: no reinicializa usuarios ni inventarios.
IF COL_LENGTH('dbo.Usuario','NumeroCuenta') IS NULL ALTER TABLE dbo.Usuario ADD NumeroCuenta varchar(20) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_UsuarioNumeroCuenta' AND object_id=OBJECT_ID('dbo.Usuario'))
 EXEC('CREATE UNIQUE INDEX UX_UsuarioNumeroCuenta ON dbo.Usuario(NumeroCuenta) WHERE NumeroCuenta IS NOT NULL');
IF OBJECT_ID('dbo.BancoRed') IS NULL
CREATE TABLE dbo.BancoRed(
 CodigoBanco varchar(15) PRIMARY KEY,NombreBanco nvarchar(100) NOT NULL,IP varchar(50) NOT NULL,
 Puerto int NOT NULL DEFAULT 5000 CHECK(Puerto BETWEEN 1 AND 65535),EsPropio bit NOT NULL DEFAULT 0,
 Confiable bit NOT NULL DEFAULT 0,Activo bit NOT NULL DEFAULT 1,
 LimiteDiario decimal(18,2) NOT NULL DEFAULT 25000 CHECK(LimiteDiario>0),
 TransferidoHoy decimal(18,2) NOT NULL DEFAULT 0 CHECK(TransferidoHoy>=0),
 FechaUltimoReinicio date NOT NULL DEFAULT CAST(SYSDATETIME() AS date));
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_BancoPropio' AND object_id=OBJECT_ID('dbo.BancoRed'))
 CREATE UNIQUE INDEX UX_BancoPropio ON dbo.BancoRed(EsPropio) WHERE EsPropio=1;
IF OBJECT_ID('dbo.TransferenciaBancaria') IS NULL
CREATE TABLE dbo.TransferenciaBancaria(
 IdTransferencia bigint IDENTITY PRIMARY KEY,TokenTransferencia varchar(40) NOT NULL,
 Direccion varchar(10) NOT NULL CHECK(Direccion IN('ENTRANTE','SALIENTE')),
 BancoOrigen varchar(15) NOT NULL REFERENCES dbo.BancoRed(CodigoBanco),
 BancoDestino varchar(15) NOT NULL REFERENCES dbo.BancoRed(CodigoBanco),
 UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id),
 CuentaOrigen varchar(20) NOT NULL,CuentaDestino varchar(20) NOT NULL,
 NombreOrigen nvarchar(120) NOT NULL,NombreDestino nvarchar(120) NOT NULL,
 Monto decimal(18,2) NOT NULL CHECK(Monto>0),Concepto nvarchar(200) NOT NULL,
 Estado varchar(20) NOT NULL CHECK(Estado IN('RESERVADA','APROBADA','RECHAZADA','INCIERTA')),
 Huella varbinary(32) NOT NULL,RespuestaDatos nvarchar(max) NOT NULL DEFAULT '{}',
 FechaCreacion datetime2 NOT NULL DEFAULT SYSDATETIME(),FechaActualizacion datetime2 NOT NULL DEFAULT SYSDATETIME(),
 UNIQUE(BancoOrigen,TokenTransferencia));
GO
CREATE OR ALTER VIEW dbo.vw_TransferenciasUsuario AS
SELECT t.IdTransferencia,t.UsuarioId,t.FechaCreacion Fecha,t.TokenTransferencia,t.Direccion,t.BancoOrigen,t.BancoDestino,
 '**** '+RIGHT(t.CuentaOrigen,4) CuentaOrigen,'**** '+RIGHT(t.CuentaDestino,4) CuentaDestino,
 t.NombreOrigen,t.NombreDestino,t.Monto,t.Estado,t.Concepto
FROM dbo.TransferenciaBancaria t;
GO
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
  SELECT TOP(1) @u=u.Id,@nd=u.Nombre FROM dbo.Usuario u
  WHERE u.Activo=1 AND u.Bloqueado=0 AND (u.NumeroCuenta=@cd OR EXISTS(SELECT 1 FROM dbo.Tarjeta t WHERE t.UsuarioId=u.Id AND t.Numero=@cd AND t.Activa=1));
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
  SELECT TOP(1) @u=u.Id,@nd=u.Nombre FROM dbo.Usuario u WITH(UPDLOCK,ROWLOCK)
  WHERE u.Activo=1 AND u.Bloqueado=0 AND (u.NumeroCuenta=@cd OR EXISTS(SELECT 1 FROM dbo.Tarjeta t WHERE t.UsuarioId=u.Id AND t.Numero=@cd AND t.Activa=1));
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
