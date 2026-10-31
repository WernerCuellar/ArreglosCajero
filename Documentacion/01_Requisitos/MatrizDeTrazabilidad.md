# Matriz de trazabilidad

La implementación se vincula a los identificadores del PDF. Las pruebas automáticas locales no sustituyen las pruebas físicas ni el video. Las rutas abreviadas de C# se ubican en Codigo/Aplicacion, Codigo/ProtocoloCompartido o los proyectos WPF; los scripts SQL en BaseDeDatos/Scripts.

## Servidor

| Código | Requisito | Implementación | Verificación |
|---|---|---|---|
| S-01 | Escucha configurable e indicadores | Codigo/Aplicacion/ServidorTcp.cs; Codigo/Servidor/MainWindow.xaml.cs | Inicio/detención y puerto |
| S-02 | Registro de identidad, IP, equipo y hora | ServidorTcp.cs; 02_Operaciones.sql / REGISTRAR_CAJERO | Registro TCP; rechazo de identidad duplicada/desconocida |
| S-03 | Concurrencia sin congelar dashboard | ServidorTcp.cs / Aceptar y Procesar | Tres conexiones; retiros simultáneos |
| S-04 | Heartbeat, desconexión, reconexión y bitácora | ServidorTcp.cs / Vigilar; ClienteTcp.cs; sp_Desconectar | Heartbeat caducado y reconexión |
| S-05 | Acceso con tarjeta activa, PIN y token | 02_Operaciones.sql / LOGIN y VALIDAR_TOKEN | Login y rechazo de credenciales |
| S-06 | Token 6 dígitos, 2 min, un uso, ventana servidor | sp_GenerarToken; MainWindow.xaml.cs / MostrarToken | Token no expuesto al cliente; vencimiento |
| S-07 | Bloqueo tras tres fallos globales | 02_Operaciones.sql / AccesoFallido | Tres cajeros; desbloqueo administrativo |
| S-08 | Operaciones y reglas | 02_Operaciones.sql / sp_Operacion | Operaciones, límites, PIN e inventario |
| S-09 | Comandos remotos | ServidorTcp.cs / Comando; Cajero/MainWindow.xaml.cs | Fuera de servicio y habilitar |
| S-10 | Persistencia antes de responder y rollback | 02_Operaciones.sql / TRY CATCH; BaseDatos.cs | Trigger de fallo simulado revierte movimiento |
| S-11 | Sesión expira en servidor a 2 min | sp_Operacion; ServidorTcp.cs / Vigilar | Caducar UltimaActividad en base aislada |
| S-12 | Correo de token/alertas | No implementado: deseable | Fuera del alcance obligatorio |

## Dashboard

| Código | Requisito | Implementación | Verificación |
|---|---|---|---|
| D-01 | Login admin con hash | Administracion.cs / Login; Administrador | PBKDF2 válido e inválido |
| D-02 | Indicadores diarios | 05_Vistas.sql / vw_ResumenDiario; DibujarResumen | Dashboard; comparación SQL |
| D-03 | Tres gráficas | Servidor/MainWindow.xaml.cs / DibujarResumen y Grafica | Revisión visual; denominaciones por cajero |
| D-04 | Monitor de estado, IP, billetes y actividad | vw_EstadoCajeros; página Cajeros | Registro y desconexión; tabla |
| D-05 | Gestión, apertura, cargas, comandos | 04_Administracion.sql; AccionesCajero | Una apertura diaria, capacidad y comando |
| D-06 | Transacciones de toda la red | vw_Transacciones; página Transacciones | Movimientos aprobados/rechazados |
| D-07 | Depósitos con filtros, totales y promedio | Refrescar / Depósitos; vw_Transacciones | Filtro fechas/texto y exportación |
| D-08 | Retiros, rechazos y consumo diario | Refrescar / Retiros; vw_RetiradoHoyPorUsuario | Límite global y motivos |
| D-09 | Cambios y restablecimiento de PIN temporal | CambioPin; RESTABLECER_PIN; CambiarPin | Cambio global y PIN temporal |
| D-10 | Gestión de usuarios/tarjetas/límites/bloqueo | 04_Administracion.sql; AccionesUsuario | Cambio tarjeta conserva consumo |
| D-11 | Consulta por usuario y último acceso | vw_Usuarios; Historial del usuario | Saldo, máximo y movimientos |
| D-12 | Control de usuarios del día | vw_ResumenDiario; DibujarResumen; Cambios de PIN | Totales, promedio, quién cambió PIN y último acceso |
| D-13 | Alertas y atención | sp_Alertas; sp_Desconectar; ATENDER_ALERTA | Falta billetes, bloqueo, desconexión, atención |
| D-14 | Reportes PDF o Excel | Exportador.cs; Exportar; Arqueo y Resumen diario | Archivo OpenXML verificado |
| D-15 | Bitácora de auditoría con filtros | Bitacora; tr_TarjetaBitacora; Auditoría | Conexión, acciones, rechazos y exportación |
| D-16 | Parámetros en SQL | Parametro; PARAMETROS; Configuración | Validaciones de máximos y puerto |
| D-17 | Supervisor, tema oscuro y sonido | Deseable: no acreditado como entrega completa | No se incluyen en cumplimiento obligatorio |

## Cajero

| Código | Requisito | Implementación | Verificación |
|---|---|---|---|
| C-01 | Configuración sin cadena SQL | Configuracion/cajero.ejemplo.json; Cajero/MainWindow.xaml.cs | Archivo y pantalla; --cajero/--servidor/--puerto |
| C-02 | Registro y reconexión cada 5s | ClienteTcp.cs / Reconectar | Reconexión automática |
| C-03 | Heartbeat y comandos | ClienteTcp.cs / Latidos; AtenderComando | Conexión persistente y comando |
| C-04 | Tarjeta, PIN enmascarado y token | Cajero/MainWindow.xaml.cs / Login | Capturas de acceso/token |
| C-05 | Retiro libre, desglose y alternativa | Movimiento; Billetes.Alternativa; sp_Retiro | Q239; alternativa acotada |
| C-06 | Depósito con desglose | Movimiento; sp_Deposito | Q239 e inventario |
| C-07 | Cambio PIN con confirmación | CambiarPin; sp_CambiarPin | PIN anterior deja de funcionar |
| C-08 | Saldo, máximo y disponible global | Saldo; sp_ConsultaUsuario | Límite tras retiro en otro ATM |
| C-09 | Últimos cinco movimientos de la red | Historial; sp_UltimasTransacciones | Prueba de orden/límite y pantalla |
| C-10 | Comprobante en pantalla | Comprobante | Revisión de retiro/depósito/PIN |
| C-11 | Cerrar sesión a 60s | Cajero/MainWindow.xaml.cs / reloj.Tick | Verificación visual local y escenario manual |
| C-12 | Recibo PDF/impresión, teclado táctil | PDF implementado con PDFsharp-WPF; impresión/teclado táctil pendientes | 4 PDF y 28 pruebas del cliente |

## Reglas

| Código | Requisito | Implementación | Verificación |
|---|---|---|---|
| RN-01 | Denominaciones | Denominacion; Billetes.Denominaciones | Q239 y rechazo de valor desconocido |
| RN-02 | Apertura diaria máx Q10000 | INICIALIZAR | Reapertura rechazada |
| RN-03 | Carga y capacidad Q30000 | CARGAR; DEPOSITO | Sin inicializar y exceso de capacidad |
| RN-04 | Límite por usuario global | vw_RetiradoHoyPorUsuario; sp_Operacion | Cambio de cajero y de tarjeta |
| RN-05 | Saldo, límite y alternativa | sp_Operacion; Billetes.Alternativa | Retiros y alternativa sin cargo |
| RN-06 | Atomicidad | BEGIN TRAN/COMMIT/ROLLBACK en 02 y 04 | Fallo simulado tras actualizar saldo |
| RN-07 | Concurrencia | UPDLOCK/ROWLOCK; CHECK | Dos retiros y saldo final |
| RN-08 | PIN 4, diferente y hash con salt | sp_Operacion / CAMBIO_PIN; Tarjeta | Hash/salt y cambio global |
| RN-09 | Tres fallos y reinicio por éxito | AccesoFallido; VALIDAR_TOKEN | Tres intentos en distintos cajeros |
| RN-10 | Una tarjeta activa e historial | UX_TarjetaActiva; CAMBIAR_TARJETA | Tarjeta anterior inactiva |
| RN-11 | Fecha oficial SQL | SYSDATETIME en procedimientos/defaults | Recibos y filtros de consumo |
| RN-12 | Baja lógica | BAJA_USUARIO; BAJA_CAJERO; Tarjeta.Activa | Historial conservado |
| RN-13 | Admin, ocho usuarios y >=3 ATM | 06_DatosPrueba.sql | Semilla: 1 admin, 8 usuarios, 8 ATM |

## Entregables y validación pendiente

Solución y código: CajeroRed.sln y Codigo/. Scripts: BaseDeDatos/Scripts/. Manuales, diagramas, protocolo y scripts completos: Documentacion/Entrega_Fase_II.pdf. Respaldo: BaseDeDatos/Respaldos/. Pruebas: Pruebas/Program.cs y Documentacion/07_PruebasYEvidencias/. Paquetes portables: Distribucion/.

Pendiente del grupo: nombres/carnés/responsables reales, tres equipos físicos conectados simultáneamente, reglas de firewall en la red elegida, grabación y enlace al video, fecha formal de entrega y acuerdos interbancarios futuros. No se afirma que estas actividades externas ya estén realizadas.


## Ampliación NETGEAR (MD recibido el 31/10/2026)
Descubrimiento UDP/IB_PING: BancoInterbancario.cs y listener compartido ServidorTcp.cs.
Verificación de cuenta y transferencias: sp_IB_Operacion, BancoInterbancario.cs, Bancos de la red y Transferencias.
Deduplicación, cupos concurrentes y rollback: script 08 y pruebas aisladas. No forman parte del PDF original y se incluyen por solicitud del usuario.
Ojo del login y totales filtrados: 5 comprobaciones de interfaz. Integración: 83 comprobaciones SQL/TCP/UDP; cajero: 28; interfaz servidor: 5. Total: 116. Pendiente ensayo físico con otros grupos y decisión final de IP/código.
