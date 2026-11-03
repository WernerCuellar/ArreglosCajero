# NETGEAR 2.2.0 — arranque, transferencias y conexiones

## Abrir el cajero
En Windows x64, descargar NETGEAR_Cajero_2.2.0.exe desde Releases y abrirlo.
Incluye .NET; no requiere Visual Studio ni SQL Server en el cajero.
También puede descargar NETGEAR_Conexion_2.2.0.zip, extraerlo y abrir Iniciar-Cajero.cmd (requiere Internet para descargar el EXE la primera vez).
En un clon del repositorio, el CMD descarga y verifica el ejecutable publicado mediante SHA256.
Sincronizar-Cajero.cmd descarga otra vez la versión indicada por el lanzador. Cerrar el cajero antes de actualizar.
Para versiones futuras: git pull y luego Sincronizar-Cajero.cmd.
Los ejecutables se publican como archivos de Releases del mismo repositorio.

Buscar servidor abre una ventana con Escanear servidores, selección de IP/puerto y código asignado.
El escaneo combina UDP 5005 y consultas TCP NETGEAR_INFO sin acceso SQL; incluye la IP configurada y las subredes IPv4 locales /24.
Solo marque el servidor de su grupo. También puede escribir una IP manualmente.
Cada equipo necesita un código distinto dado de alta en el servidor.
No usar 127.0.0.1 en otra PC: esa IP apunta a la propia computadora.
Si no aparece: misma LAN, servidor en Iniciar servicio, firewall TCP 5000/UDP 5005 y sin aislamiento entre clientes en el router.
La configuración y referencias pendientes permanecen locales.

## Enviar y recibir
Ingresar tarjeta, PIN y token; pulsar Transferir dinero.
Elegir NETGEAR para otra cuenta de nuestro banco, o el banco externo autorizado.
Ingresar cuenta o tarjeta destino, Verificar destinatario, monto y concepto; revisar nombre, banco y cuenta antes de confirmar.
Mis transferencias muestra envíos y abonos. El destinatario recibe el saldo sin tener una sesión abierta.
La transferencia no mueve billetes ni consume el límite de retiro físico.
Los bancos externos deben registrar mutuamente código, IP y puerto y autorizar la conexión.
BANC02 continúa provisional hasta confirmar el código con los grupos.
El cajero usa su servidor; el servidor habla el protocolo IB recibido con el otro banco.

La transferencia interna debita y acredita en una misma transacción SQL.
La externa persiste referencia antes del contacto y reserva saldo antes de IB_TRANSFERIR.
Los dos bancos conservan el mismo token. Repetir referencia y datos devuelve el resultado sin otro cargo.
Si se pierde una respuesta, se bloquean movimientos y se consulta la referencia; no se reenvía dinero automáticamente.
IB_ESTADO_TRANSFERENCIA permite conciliar si el otro banco lo implementa. Si no, el administrador debe coordinar la confirmación.
No devolver reservas inciertas sin confirmar que el otro banco no acreditó.
El nombre del destinatario se verifica otra vez antes de reservar. El usuario origen se deriva de la sesión SQL.

## Actualización del servidor
Conservar servidor.local.json y saldos existentes.
Aplicar Actualizar-Interbancario.ps1 -Servidor '.' con la instancia real: migraciones aditivas 08/09/10/11.
No ejecutar el instalador 01 sobre una base existente.
Publicar.ps1 genera portables completos; Publicar-Ejecutables.ps1 genera los EXE únicos.
Configurar-Firewall.ps1 permite indicar -Programa para el ejecutable real del servidor.
El cliente nunca recibe la cadena SQL.

## Estabilidad y diagnóstico del 3 de noviembre
El proceso del servidor permaneció ejecutándose durante los cortes observados.
Los cierres simultáneos de varios cajeros coincidieron con eventos de desconexión de la LAN en Windows.
Hubo errores de renovación DHCP y una dirección 169.254.x.x en Ethernet; posteriormente recuperó dirección del router.
No se observó saturación ni bloqueo SQL persistente en la captura operativa.
Se deshabilitó la opción que permite apagar los adaptadores Ethernet y Wi-Fi para ahorrar energía.
Esto no equivale a reservar una dirección en el router: aún se deben configurar reservas DHCP distintas por MAC para Ethernet y Wi-Fi.
No asignar a mano una IP dentro de un rango DHCP desconocido.

El servidor ahora mantiene lectura TCP mientras procesa solicitudes, con escritura serializada y operaciones limitadas por conexión.
Los heartbeats disponen de un pool SQL separado (16 conexiones); operaciones usan hasta 100 conexiones reutilizables.
El contexto SQL limitado se restaura con REVERT antes de devolver una conexión al pool.
El latido SQL espera como máximo 200 ms ante bloqueo y devuelve estado ocupado; el transporte continúa.
La detección de conexión usa llegada de heartbeat en memoria, no una fila SQL atrasada.
Un heartbeat perdido aislado no provoca reconexión inmediata; un cajero sin latidos durante 30 segundos sí se desconecta.
Se registran causas SocketError, operaciones lentas y cambios de interfaces para distinguir red y SQL.
TCP KeepAlive complementa el heartbeat. Estos cambios no evitan cortes físicos, fallos DHCP o aislamiento del router.

## Verificación reproducible
Pruebas.dll --transferencias crea dos bases y dos bancos TCP aislados.
37 comprobaciones: débito/crédito interno, banco externo real, recepción sin sesión, deduplicación, respuesta perdida y conciliación, cupos, concurrencia, rollback,
heartbeat y búsqueda durante un bloqueo SQL, identidad y reutilización del pool y desconexión de un cliente silencioso.
No modifica la base operativa. Ejecutar con CAJERO_SQL_SERVIDOR apuntando a la instancia de pruebas.
Mantener también --integracion, --revision-cajero y --revision-servidor.
La prueba física con otros grupos y sus implementaciones IB sigue siendo necesaria.

Validación completada: 83 comprobaciones de integración, 37 de transferencias entre dos bancos, 31 de interfaz y recuperación del cajero y 5 del servidor: 156 en total.
