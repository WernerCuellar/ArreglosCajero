# Verificación de la entrega

Verificación local del 28 de octubre de 2026: compilación sin errores ni advertencias; 60 comprobaciones automáticas básicas/SQL/TCP y dos comprobaciones de interfaz: carga al navegar a Usuarios y cierre por inactividad. También se compila con MSBuild de la instalación real de Visual Studio 2022. El reloj de actividad se adelanta en el ensayo de interfaz para activar el mismo temporizador sin esperar un minuto. Los logs conservan la salida exacta.

Las pruebas se ejecutan mediante `Pruebas/Program.cs --integracion`, usando una base temporal con prefijo BD_CajeroRed_Pruebas y sufijo aleatorio. El nombre se valida antes de eliminarla. No se elimina ni reinicializa BD_CajeroRed_Fase2 ni la base de fase I.

Cobertura: mensajes JSON, desglose Q239, alternativa con existencias acotadas, exportación OpenXML sin fórmulas procedentes de datos, administrador PBKDF2, usuario SQL limitado, apertura/cargas, tres sockets persistentes, token no expuesto al cliente, límite global, retiros simultáneos, saldo consistente, idempotencia, consulta de referencia, alternativa sin cargo, depósito, cambio PIN, bloqueo global, token vencido, reemplazo tarjeta conservando consumo, comandos remotos, rollback por fallo simulado, expiración de sesión, heartbeat vencido, reconexión y auditoría.

La evidencia textual exacta se guarda en `Pruebas/Evidencias/ultima_ejecucion.txt` durante la verificación final. Una captura de pantalla demuestra la interfaz en este equipo; no demuestra por sí misma la red física.

La comprobación de rollback instala un trigger que provoca un fallo únicamente en la base temporal, después de iniciar un movimiento. La prueba exige saldo sin cambios y respuesta 96; después elimina el trigger. El respaldo de entrega no contiene ese trigger.

La verificación visual navega por servidor y cajero y guarda capturas en Documentacion/06_Manuales/Capturas. Se revisan acceso, token, menú, resumen y tablas. La aplicación conserva la paleta original y usa controles WPF redimensionables.

Pendientes que requieren a los integrantes: tres equipos físicos, red/firewall reales, los doce escenarios grabados, reparto definitivo de responsabilidades y enlace al video. Las pruebas en una sola PC no se anuncian como cumplimiento de esa demostración.

## Verificación vigente NETGEAR del 31/10/2026
Compilación Release: cero errores y cero advertencias, SDK 9.0.311, destino .NET 8.
116 comprobaciones aprobadas: 83 SQL/TCP/UDP, 5 de interfaz del servidor y 28 del cajero.
Evidencia exacta en Pruebas/Evidencias/integracion_20261031.txt, revision_servidor_20261031.txt y revision_cajero_20261031.txt.
Integración: tres clientes estables más de treinta segundos, autenticación, dinero, concurrencia, rollback, autorización por usuario de la consulta pendiente e identidad/fechas compatibles con el nuevo cliente.
Interbancario: UDP, IB_PING, alias de cuenta, banco no autorizado, idempotencia, contenido distinto, cupo concurrente, validación, rollback saldo/cupo, consulta de confirmación, salida confirmada y respuesta perdida.
Cajero: referencia persistida antes del envío, bloqueo, reinicio/identidad, entrada numérica, formulario conservado ante error, actividad, deshabilitación/corte, cierre inmediato, respuesta tardía, configuración dañada/fuera de rango/sin escritura y cuatro tipos de PDF.
Los PDF se revisaron con pypdf y dos vistas rasterizadas: una página, datos legibles y tarjeta enmascarada.
La integración financiera se ejecuta en una base temporal eliminada al terminar. Los ensayos del cliente usan un par TCP simulado. No se mueven fondos operativos para esas pruebas.
Faltan la validación física con otros grupos, decisión final del código y reservas DHCP en el router de la demostración. Los ensayos de loopback no acreditan estabilidad de esa LAN.
