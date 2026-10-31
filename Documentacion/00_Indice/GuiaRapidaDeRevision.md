# Guía rápida para revisar y explicar la entrega

Las rutas de esta guía parten de la carpeta CajeroRed_Servidor extraída. Los scripts SQL definen las estructuras; los datos operativos viven en SQL Server, dentro de BD_CajeroRed_Fase2. El archivo BAK es una copia de un momento concreto, no una base que la aplicación modifique directamente.

| Pregunta del ingeniero | Archivo o lugar para mostrar |
|---|---|
| ¿Dónde se crean las tablas, llaves y restricciones? | BaseDeDatos/Scripts/01_CrearBase.sql |
| ¿Dónde están usuarios, saldo y límite diario? | SQL Server: dbo.Usuario; definiciones en 01_CrearBase.sql |
| ¿Dónde están las tarjetas y el PIN? | dbo.Tarjeta; PIN como hash con salt, no texto recuperable |
| ¿Dónde se guarda el efectivo por denominación? | dbo.InventarioCajero y dbo.Denominacion |
| ¿Dónde se registran inicializaciones y cargas? | dbo.CargaEfectivo y dbo.CargaEfectivoDetalle |
| ¿Dónde se guardan los movimientos? | dbo.Transaccion y dbo.TransaccionDetalle |
| ¿Dónde quedan sesiones y tokens? | dbo.Sesion y dbo.Token; no mostrar valores de tokens vigentes en la exposición |
| ¿Dónde está la auditoría? | dbo.Bitacora; además dbo.CambioPin y dbo.HistorialLimite |
| ¿Dónde se controla que no se cobre dos veces? | dbo.SolicitudOperacion y BaseDeDatos/Scripts/02_Operaciones.sql |
| ¿Dónde se programan retiro, depósito y rollback? | 02_Operaciones.sql; contratos públicos en 03_Contratos.sql |
| ¿Dónde están gestión de usuarios y comandos administrativos? | 04_Administracion.sql y Codigo/Aplicacion/Administracion.cs |
| ¿Dónde está el trigger de auditoría? | 04_Administracion.sql, tr_TarjetaBitacora |
| ¿Dónde se consultan las métricas? | 05_Vistas.sql y Codigo/Servidor/MainWindow.xaml.cs |
| ¿Dónde están los datos iniciales? | 06_DatosPrueba.sql; pueden diferir del respaldo de pruebas actual |
| ¿Dónde están los permisos de la aplicación? | 07_Permisos.sql y Codigo/Infraestructura/BaseDatos.cs |
| ¿Dónde está el servidor TCP? | Codigo/Aplicacion/ServidorTcp.cs |
| ¿Dónde está reconexión y heartbeat? | Codigo/Aplicacion/ClienteTcp.cs |
| ¿Dónde se define el mensaje JSON? | Codigo/ProtocoloCompartido/Mensaje.cs; Documentacion/04_Protocolo/ |
| ¿Dónde están las pantallas del cajero y servidor? | Codigo/Cajero/ y Codigo/Servidor/ |
| ¿Dónde está el estilo? | Codigo/PresentacionCompartida/Tema.xaml |
| ¿Dónde se genera Excel? | Codigo/PresentacionCompartida/Exportador.cs |
| ¿Dónde está el modelo de la base? | Documentacion/03_BaseDeDatos/Modelo.md y ModeloRelacional.png |
| ¿Dónde se vinculan los requisitos a la implementación? | Documentacion/01_Requisitos/MatrizDeTrazabilidad.md |
| ¿Dónde están manuales y documentación completa? | Documentacion/Entrega_Fase_II.pdf y las carpetas numeradas de Documentacion/ |
| ¿Dónde están las pruebas automatizadas y resultados? | Pruebas/Program.cs; Pruebas/Evidencias/; Documentacion/07_PruebasYEvidencias/ |
| ¿Dónde está la copia para restaurar en otra PC? | BaseDeDatos/Respaldos/BD_CajeroRed_Fase2.bak y ESTADO_DEL_RESPALDO.md |
| ¿Dónde se configura SQL en esta distribución? | Botón Configurar SQL Server; guarda Configuracion/servidor.local.json al iniciar con el CMD |
| ¿Dónde se guarda la conexión de cada cajero? | En SU paquete: Distribucion/Cajero/cajero.local.json, creado al guardar desde su ventana |
| ¿Dónde está lo de otros grupos? | Documentacion/08_IntegracionEntreGrupos/ y Codigo/Aplicacion/IntegracionInterbancaria.cs; implementación BancoInterbancario.cs según el MD recibido; prueba física pendiente |

Para ver tablas en SSMS: Bases de datos > BD_CajeroRed_Fase2 > Tablas. Para mostrar su definición usar los scripts incluidos. Para mostrar información preferir el dashboard, que enmascara tarjetas y no expone hashes ni tokens.

Un BAK no se abre como documento de texto: se restaura con SQL Server. No ejecutar scripts de creación sobre la base ya restaurada. Mantener saldos, movimientos e inventarios en SQL; no editar archivos JSON para cambiarlos.