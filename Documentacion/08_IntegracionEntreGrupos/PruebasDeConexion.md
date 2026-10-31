# Lista para probar la integración futura

Registrar fecha, instituciones, versiones y responsables. Usar datos ficticios y guardar referencias en ambos servidores.

1. Comprobar IP/DNS y puerto sin realizar un cargo.
2. Verificar autenticación institucional correcta e incorrecta.
3. Consultar una cuenta válida e inexistente según el contrato acordado.
4. Solicitar una transferencia autorizada y comprobar los dos asientos y referencias.
5. Solicitar con saldo insuficiente y demostrar que no se acredita el destino.
6. Repetir exactamente el mismo ID y comprobar que no duplica el débito ni el crédito.
7. Reutilizar ID con otro monto y exigir rechazo.
8. Cortar la red después de enviar y antes de recibir respuesta. Consultar estado sin repetir el cargo.
9. Reiniciar un servidor con una operación pendiente y recuperar su estado persistido.
10. Conciliar las operaciones de ambos bancos por ID y referencia remota.
11. Cambiar de router/IP y demostrar que basta actualizar configuración.
12. Ejecutar dos operaciones concurrentes sobre el mismo saldo y validar límites.

Hay pruebas automáticas locales del protocolo y del par simulado (Resultados.md). Los escenarios de esta lista con instituciones reales siguen pendientes; no se anuncian como demostrados.
