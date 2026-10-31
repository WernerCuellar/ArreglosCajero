# Integración NETGEAR
Implementación del MD recibido: descubrimiento UDP 5005 y solicitudes IB por TCP 5000.
Leer ProtocoloExterno.md y PROTOCOLO_CONEXION_BANCOS_LLM.md.
Nombre confirmado: NETGEAR. Código provisional: BANC02, confirmar con los grupos.
En Bancos de la red configurar el propio, registrar al otro banco y autorizar su IP después de confirmarla con el responsable.
Comprobar primero Ping y Verificar cuenta; después una transferencia pequeña y el mismo token en ambos historiales.
No se modificó la interfaz del cajero: el envío de prueba se realiza en el dashboard con autorización administrativa y del usuario origen.
Las extensiones de deduplicación/consulta de estado deben probarse en ambos servidores. El MD no garantiza atomicidad entre instituciones.
La configuración de bancos reside en SQL Server. ConfiguracionEjemplo.json solo documenta valores de red; no contiene secretos.
No intercambiar PIN, hashes, contraseñas SQL ni respaldos operativos.
No se afirma haber probado aún con servidores físicos de otros grupos.
