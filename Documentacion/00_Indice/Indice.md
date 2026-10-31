# Índice para localizar y explicar archivos

| Si el ingeniero solicita... | Abrir... |
|---|---|
| La solución de Visual Studio | `CajeroRed.sln` |
| El servidor TCP y sus conexiones simultáneas | `Codigo/Aplicacion/ServidorTcp.cs` |
| La reconexión y los heartbeats del cliente | `Codigo/Aplicacion/ClienteTcp.cs` |
| La estructura de los mensajes | `Codigo/ProtocoloCompartido/Mensaje.cs` y `Documentacion/04_Protocolo/ProtocoloInterno.md` |
| La creación de tablas y restricciones | `BaseDeDatos/Scripts/01_CrearBase.sql` |
| Las transacciones, bloqueos y rollback | `BaseDeDatos/Scripts/02_Operaciones.sql`, procedimiento `sp_Operacion` |
| Un procedimiento específico de retiro o depósito | `03_Contratos.sql`; delega al motor transaccional de 02 |
| La inicialización o cambio administrativo de tarjeta | `04_Administracion.sql` |
| El trigger de auditoría | `04_Administracion.sql`, `tr_TarjetaBitacora` |
| Las consultas del dashboard | `05_Vistas.sql` y `Codigo/Servidor/MainWindow.xaml.cs`, método `Refrescar` |
| Los datos de prueba | `06_DatosPrueba.sql` y `01_Requisitos/DatosDePrueba.md` |
| Los permisos SQL | `07_Permisos.sql` y `Codigo/Infraestructura/BaseDatos.cs`, método `Abrir` |
| El hash del PIN | `02_Operaciones.sql`: SHA2_256 de salt + PIN; nunca aparece en respuestas |
| La contraseña administrativa | `Codigo/Aplicacion/Administracion.cs`: PBKDF2-SHA256 y comparación constante |
| El formulario breve de usuario | `Codigo/Servidor/MainWindow.xaml.cs`, método `AccionesUsuario` |
| El estilo original adaptado a WPF | `Codigo/PresentacionCompartida/Tema.xaml` |
| El recibo del cajero | `Codigo/Cajero/MainWindow.xaml.cs`, método `Comprobante` |
| El reporte Excel | `Codigo/PresentacionCompartida/Exportador.cs` |
| El modelo relacional | `03_BaseDeDatos/Modelo.md` y `03_BaseDeDatos/ModeloRelacional.png` |
| Los pasos para cambiar de router | `05_InstalacionYRed/ManualDeInstalacion.md` |
| Las pruebas y qué falta demostrar | `07_PruebasYEvidencias/Resultados.md` y `EscenariosFisicos.md` |
| La conexión con otro grupo | `08_IntegracionEntreGrupos/LEEME_Integracion.md` |
| El respaldo | `BaseDeDatos/Respaldos/BD_CajeroRed_Fase2.bak` y su LEEME |
| La entrega al jefe de grupo | `09_Entrega/GuiaDeContinuacion.md` |

Las rutas de esta tabla son relativas a la raíz de `CajeroRed_Fase2`, salvo los enlaces entre documentos. La matriz de trazabilidad vincula cada requisito con su archivo y prueba. El PDF reúne arquitectura, modelo, protocolo, manuales, pruebas y scripts íntegros como anexos.

## Cambios NETGEAR
- BancoInterbancario.cs: protocolo de bancos; MainWindow.Interbancario.cs: dashboard.
- BaseDeDatos/Scripts/08_Interbancario.sql y Actualizar-Interbancario.ps1: actualización aditiva.
- Configurar-Firewall.ps1: TCP y UDP para la subred de pruebas.
- Documentacion/09_Entrega/RevisionServidor_2026-10-31.md: requisitos, DHCP y diagnóstico.
