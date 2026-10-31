# Verificación de la entrega

Verificación local del 28 de octubre de 2026: compilación sin errores ni advertencias; 60 comprobaciones automáticas básicas/SQL/TCP y dos comprobaciones de interfaz: carga al navegar a Usuarios y cierre por inactividad. También se compila con MSBuild de la instalación real de Visual Studio 2022. El reloj de actividad se adelanta en el ensayo de interfaz para activar el mismo temporizador sin esperar un minuto. Los logs conservan la salida exacta.

Las pruebas se ejecutan mediante `Pruebas/Program.cs --integracion`, usando una base temporal con prefijo BD_CajeroRed_Pruebas y sufijo aleatorio. El nombre se valida antes de eliminarla. No se elimina ni reinicializa BD_CajeroRed_Fase2 ni la base de fase I.

Cobertura: mensajes JSON, desglose Q239, alternativa con existencias acotadas, exportación OpenXML sin fórmulas procedentes de datos, administrador PBKDF2, usuario SQL limitado, apertura/cargas, tres sockets persistentes, token no expuesto al cliente, límite global, retiros simultáneos, saldo consistente, idempotencia, consulta de referencia, alternativa sin cargo, depósito, cambio PIN, bloqueo global, token vencido, reemplazo tarjeta conservando consumo, comandos remotos, rollback por fallo simulado, expiración de sesión, heartbeat vencido, reconexión y auditoría.

La evidencia textual exacta se guarda en `Pruebas/Evidencias/ultima_ejecucion.txt` durante la verificación final. Una captura de pantalla demuestra la interfaz en este equipo; no demuestra por sí misma la red física.

La comprobación de rollback instala un trigger que provoca un fallo únicamente en la base temporal, después de iniciar un movimiento. La prueba exige saldo sin cambios y respuesta 96; después elimina el trigger. El respaldo de entrega no contiene ese trigger.

La verificación visual navega por servidor y cajero y guarda capturas en Documentacion/06_Manuales/Capturas. Se revisan acceso, token, menú, resumen y tablas. La aplicación conserva la paleta original y usa controles WPF redimensionables.

Pendientes que requieren a los integrantes: tres equipos físicos, red/firewall reales, los doce escenarios grabados, reparto definitivo de responsabilidades y enlace al video. Las pruebas en una sola PC no se anuncian como cumplimiento de esa demostración.

## Verificación NETGEAR del 31/10/2026
Compilación Release: cero errores y cero advertencias, SDK 9.0.311, destino .NET 8.
82 comprobaciones de integración SQL/TCP/UDP y 5 comprobaciones de interfaz aprobadas (87 en total). Evidencia: Pruebas/Evidencias/integracion_20261031.txt y revision_servidor_20261031.txt.
Incluye tres clientes ociosos con heartbeat por más de treinta segundos; el ensayo no demuestra estabilidad de la red física.
Interbancario: ping en listener compartido, alias de cuenta, origen no autorizado, idempotencia, cambio de contenido, cuenta inexistente, monto negativo, campos nulos, rollback de saldo/cupo por fallo SQL, cupo concurrente, consulta de confirmación, UDP, salida confirmada y respuesta perdida con reserva.
El par simulado se ejecuta en loopback; falta probar con servidores de otros grupos. No se modificó saldo operativo para estas pruebas: la base temporal fue eliminada.
La instalación local recibió solo la migración aditiva 08 y la identidad NETGEAR con BANC02 provisional; no se ejecutó la semilla sobre la base restaurada.
