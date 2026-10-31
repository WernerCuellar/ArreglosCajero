# Revisión del servidor — 31/10/2026

Referencia: PDF Fase II, secciones 5.1, 5.2, 7 y 8. Alcance: servidor.

## Login y dashboard
Login con PBKDF2-SHA256 y PasswordBox. Se añade ojo para mostrar/ocultar; se oculta al enviar o desactivar la ventana y se limpia al terminar cada intento.
Están implementados Resumen, Cajeros, Usuarios, Transacciones, Depósitos, Retiros, Cambios de PIN, Alertas, Auditoría, Arqueo, Resumen diario y Configuración. D-11 está en Usuarios/Historial; D-12 en Resumen/Cambios de PIN.
Depósitos y Retiros YA tenían total y promedio en el pie, pero ignoraban el filtro de texto. Se corrige para calcular sobre filas visibles, excluyendo rechazos del dinero.
Existen tres gráficas y refresco programado a cuatro segundos; verificar duración real bajo carga.
Se recarga el puerto guardado al iniciar servicio y se remite al LEEME para restaurar el respaldo sin reinstalar la base.
S-12 (correo) y D-17 (tema/sonido/Supervisor completo) son deseables sin completar. No falta una página obligatoria entera. La demostración en tres equipos y video sigue pendiente.

## Red y reservas DHCP
Ethernet 2: 192.168.0.34; gateway 192.168.0.1; MAC 9C-7F-64-4D-FE-DE.
Wi-Fi: 192.168.2.137; gateway 192.168.2.1; MAC observada F6-23-1D-F5-39-A2.
Ambos en DHCP y perfil Público; son redes distintas. Ethernet sin 2 es VirtualBox con 169.254.*, no sirve para los cajeros físicos.
El servidor ya escucha en IPAddress.Any. La pantalla ahora identifica adaptadores y actualiza IPv4, omitiendo enlace local, loopback e interfaces sin gateway IPv4. Esto no fija IP.

Para mantener una IP por adaptador:
1. Elegir router de demostración y conectar servidor/clientes a su LAN.
2. Desactivar direcciones de hardware aleatorias del perfil Wi-Fi; reconectar y consultar MAC efectiva. Actualmente está habilitado; la MAC observada puede cambiar.
3. Crear una reserva DHCP por MAC en el router: una IP para Ethernet y otra distinta para Wi-Fi. Mantener Windows en DHCP.
4. Si usan dos routers, crear cada reserva en el correspondiente y su subred.
5. Reconectar y verificar direcciones; configurar los cajeros con la IP alcanzable. Cambiar adaptador puede requerir cambiar destino. Un nombre estable requiere resolución comprobada desde cada cliente.

No asignar la misma IP a ambos adaptadores activos. Para IP manual falta conocer máscara y rango/exclusiones DHCP; no elegir direcciones arbitrarias. No instalar un servidor DHCP en esta PC.
No se modificó red ni router: pendiente definir red definitiva con el usuario.

## Firewall
No se encontró regla específica CajeroRed TCP 5000. Las reglas de Proyecto Cajero corresponden a otra aplicación. El ejemplo Privado del manual no cubre las redes Públicas actuales.
Tras elegir LAN de confianza, permitir TCP del ejecutable Distribucion/Servidor/Servidor.exe en puerto configurado y alcance adecuado. Mantener SQL local.
Desde otra PC: Test-NetConnection IP_DEL_SERVIDOR -Port 5000; después registrar tres cajeros simultáneos. Las pruebas locales no acreditan esta conectividad.

Referencia: https://learn.microsoft.com/windows-server/networking/technologies/dhcp/dhcp-scopes

## Repositorio
Se excluyen configuración privada, respaldo BAK con usuarios/hashes/movimientos, ejecutables y cachés. Los scripts con datos ficticios permiten crear una base nueva; el respaldo original permanece local.

## Ampliación solicitada: NETGEAR y desconexiones
Se añaden Bancos de la red y Transferencias conforme al MD recibido; migración SQL aditiva 08.
La bitácora anterior muestra cortes repetidos y algunos simultáneos, pero solo registra un motivo genérico. No prueba de quién fue el fallo.
Se separan aceptación/monitoreo TCP y ciclo del cliente del hilo WPF. El cliente observa la tarea que termina (WhenAny por sí solo no propaga su excepción) y programa heartbeat con PeriodicTimer de diez segundos.
Nuevos logs JSONL en Logs junto al ejecutable: motivo, fase, IP, último tipo, milisegundos sin recepción/heartbeat, código Socket/SQL y consultas lentas. No incluyen payload, PIN, tokens de login o contraseñas.
Motivos: HEARTBEAT_VENCIDO_30S, REMOTO_CERRO, SOCKET_..., PARADA_SERVIDOR, registro rechazado. La bitácora guarda CONEXION_MOTIVO. Correlacionar UTC de Logs con hora local SQL.
La prueba local mantiene tres clientes más de treinta segundos. No demuestra estabilidad de la LAN real. Para diagnóstico de clientes antiguos conviene distribuir después el transporte actualizado del cliente; el alcance de este paquete portable sigue siendo el servidor.

## Estado local tras la actualización
Se aplican 08 y 09 sin reinstalar la base. Identidad NETGEAR con BANC02 provisional.
Reglas NETGEAR-Servidor-TCP-5000 y NETGEAR-Discovery-UDP-5005 habilitadas para Distribucion/Servidor/Servidor.exe, perfil Any y origen LocalSubnet. No se abrió SQL.
Actualizados los portables de servidor y CajeroRed_Cajero; la IP fija permanece pendiente de definir router y reserva DHCP.
No se identificó todavía la causa exacta de los cortes físicos; los nuevos logs distinguen el motivo y las pruebas locales verifican el comportamiento esperado.
