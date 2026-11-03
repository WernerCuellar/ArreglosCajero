USE BD_CajeroRed_Fase2;
GO
-- El latido no espera los bloqueos de una operación financiera.
CREATE OR ALTER PROCEDURE dbo.sp_Heartbeat
 @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS
BEGIN
 SET NOCOUNT ON; SET LOCK_TIMEOUT 200;
 DECLARE @activo bit,@habilitado bit,@inicializado date,@datos nvarchar(max);
 BEGIN TRY
  SELECT @activo=Activo,@habilitado=Habilitado,@inicializado=FechaInicializacion
  FROM dbo.Cajero WITH(READPAST,READCOMMITTEDLOCK) WHERE Codigo=@Cajero;
  IF @activo IS NULL
   SELECT '96' Codigo,N'Conexión activa; estado SQL ocupado.' Mensaje,'{}' Datos;
  ELSE IF @activo=0
   SELECT '91' Codigo,N'Cajero inactivo.' Mensaje,'{}' Datos;
  ELSE
  BEGIN
   UPDATE dbo.Cajero SET UltimoHeartbeat=SYSDATETIME() WHERE Codigo=@Cajero;
   SET @datos=(SELECT @habilitado habilitado,CAST(CASE WHEN @inicializado=CAST(SYSDATETIME() AS date) THEN 1 ELSE 0 END AS bit) inicializado FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
   SELECT '00' Codigo,N'Conexión activa.' Mensaje,@datos Datos;
  END;
 END TRY
 BEGIN CATCH
  IF ERROR_NUMBER()=1222 SELECT '96' Codigo,N'Conexión activa; estado SQL ocupado.' Mensaje,'{}' Datos;
  ELSE THROW;
 END CATCH;
END;
GO
