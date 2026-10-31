USE BD_CajeroRed_Fase2;
GO
CREATE OR ALTER PROCEDURE dbo.sp_RegistrarCajero @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'REGISTRAR_CAJERO',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_Heartbeat @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'HEARTBEAT',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_ValidarCredenciales @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'LOGIN',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_ValidarToken @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'VALIDAR_TOKEN',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_Retiro @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'RETIRO',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_Deposito @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'DEPOSITO',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_CambiarPin @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'CAMBIO_PIN',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_ConsultaUsuario @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'CONSULTA_SALDO',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_UltimasTransacciones @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'ULTIMAS_TRANSACCIONES',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_CerrarSesion @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'LOGOUT',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_EstadoSolicitud @Json nvarchar(max),@Cajero varchar(24),@Sesion varchar(36)=NULL,@Solicitud varchar(64)
AS BEGIN SET NOCOUNT ON; EXEC dbo.sp_Operacion 'ESTADO_SOLICITUD',@Json,@Cajero,@Sesion,@Solicitud; END;
GO
