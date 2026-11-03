# NETGEAR — servidor CajeroRed

Servidor C# / WPF con SQL Server, dashboard, sockets para cajeros e integración entre bancos.

## Empezar
- Paquete local con respaldo: leer [LEEME_PRIMERO.md](LEEME_PRIMERO.md).
- Copia desde GitHub: instalar SQL Server y crear una base NUEVA con Instalar-BaseDeDatos.ps1 -Servidor '.'; ajustar la instancia real. No ejecutar el instalador sobre una base existente.
- Base restaurada o anterior: aplicar SOLO Actualizar-Interbancario.ps1 -Servidor '.'; la migración 08 no reinicializa saldos ni inventario.
- Compilar: dotnet build CajeroRed.sln -c Release. Destino .NET 8; SDK .NET 9.0.3xx o posterior compatible con global.json.
- Generar portable del servidor: ejecutar Publicar.ps1 (por defecto solo Servidor).
- Abrir Iniciar-Servidor.cmd, configurar SQL e iniciar sesión. Credenciales ficticias de demostración en LEEME_PRIMERO.md.
- En Bancos de la red → Mi banco NETGEAR, establecer el código único del grupo. BANC02 es una sugerencia provisional, no una asignación confirmada.

## Red
TCP 5000 comparte conexiones ATM y mensajes IB. UDP 5005 responde al descubrimiento.
Configurar-Firewall.ps1 crea reglas para el ejecutable y los puertos con alcance LocalSubnet; ejecutar como administrador en la LAN de pruebas.
Wi-Fi y Ethernet necesitan reservas DHCP independientes e IP distintas. Ver [revisión de requisitos y red](Documentacion/09_Entrega/RevisionServidor_2026-10-31.md).

## Integración
Implementa el [protocolo recibido](Documentacion/08_IntegracionEntreGrupos/PROTOCOLO_CONEXION_BANCOS_LLM.md).
Soporta descubrimiento UDP, IB_PING, IB_VERIFICAR_CUENTA e IB_TRANSFERIR; añade deduplicación y consulta opcional IB_ESTADO_TRANSFERENCIA.
Solo bancos externos autorizados por código e IP pueden acreditar transferencias. Es una restricción de la LAN de pruebas, no autenticación criptográfica.
El cajero permite transferir a otras cuentas NETGEAR y a bancos externos autorizados desde Transferir dinero; Mis transferencias muestra envíos y abonos.
Ver [contrato y límites](Documentacion/08_IntegracionEntreGrupos/ProtocoloExterno.md).

## Verificación
Ejecutar-Pruebas.ps1 -ServidorSql '.' crea y elimina una base temporal aislada. Incluye tres clientes TCP, concurrencia, límites, rollback y protocolo interbancario.
Tras compilar, Pruebas.dll --revision-servidor verifica el ojo y los totales filtrados.
Los ensayos locales no sustituyen las pruebas físicas con otros grupos.

El repositorio incluye también el fuente del cliente; excluye configuración privada, respaldo BAK operativo, ejecutables, cachés y registros locales. El paquete local conserva el respaldo original.

## Cajero NETGEAR 2.2.0
Se implementan los cambios de cambios.txt: recuperación persistente, bloqueo de movimientos, cierre inmediato, validaciones, control de formularios y comprobantes PDF.
Aplicar también 09_CompatibilidadCajero.sql (incluido en Actualizar-Interbancario.ps1).
Publicar-Cajero.ps1 actualiza la carpeta hermana CajeroRed_Cajero y su CMD preservando configuración privada.
Pruebas.dll --revision-cajero ejecuta los ensayos del cliente. Detalle en Documentacion/09_Entrega/CorreccionesCajero_2026-10-31.md.

## Ejecutables y conexión rápida
Descargar el EXE único o el kit de conexión (descarga el EXE al iniciar) en [Releases](https://github.com/WernerCuellar/ArreglosCajero/releases/tag/netgear-2.2.0). Incluyen .NET para Windows x64.
En un clon: abrir Iniciar-Cajero.cmd; Sincronizar-Cajero.cmd vuelve a descargar la versión publicada y comprueba SHA256.
Buscar servidor permite escanear o configurar IP, puerto y código de cajero.
Ver [transferencias, arranque y estabilidad 2.2](Documentacion/09_Entrega/NETGEAR_2.2.0.md). Aplicar migraciones 08–11 en bases existentes.
