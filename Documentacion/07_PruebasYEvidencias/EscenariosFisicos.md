# Demostración en equipos físicos y guion del video

Preparación: un servidor, tres o más PCs cliente, misma red, puerto autorizado y códigos distintos dados de alta. La excepción de varias instancias solo aplica si el grupo tiene menos integrantes, según el enunciado. Completar integrantes, carnés y responsables en GuiaDeContinuacion.md. Mostrar el servidor y el cajero a la vez cuando corresponda.

| Nº | Acción | Evidencia esperada |
|---|---|---|
| 1 | Conectar tres PCs con ATM-01, ATM-02 y ATM-03 | Dashboard: tres conexiones, IP real e inventario individual |
| 2 | Desconectar un cliente, esperar y volver a conectar | Desconectado en hasta 30 s, alerta, reconexión y bitácora |
| 3 | Usuario límite Q500: retirar Q300 en ATM-01, luego Q300 en ATM-02 | Segundo retiro código 61; consumo global Q300 |
| 4 | Dos retiros simultáneos sobre saldo suficiente para solo uno | Un aprobado; otro rechazado; saldo nunca negativo |
| 5 | Solicitar Q300 en tres billetes de Q100 cuando solo existen dos | Alternativa con otros billetes; aceptar o cancelar sin cargo previo |
| 6 | Depositar en ATM-02 | Saldo e inventario de ATM-02 aumentan; dashboard <=5 s |
| 7 | Cambiar PIN en ATM-03 | Registro de cambio; PIN anterior inválido en otro cajero |
| 8 | Tres PIN incorrectos en distintos cajeros | Bloqueo global; desbloqueo desde dashboard |
| 9 | Cambiar tarjeta después de un retiro del día | Anterior inactiva; nueva conserva consumo del límite |
| 10 | Cargar cajero sin inicializar o superar Q30000 | Rechazo sin cambio de inventario |
| 11 | Generar token y esperar más de dos minutos antes de usarlo | Código 56; no se abre sesión |
| 12 | Poner cajero fuera de servicio desde el servidor | Pantalla bloqueada; no aprueba operaciones hasta habilitar |

Adicionales recomendados: cerrar sesión a los 60 s, consulta de últimas cinco transacciones, restablecimiento de PIN temporal y cambio obligatorio, reportes Excel, arqueo sin diferencias, cambio de router/IP, código duplicado e ID financiero repetido sin doble cargo.

## Guion

1. Portada: proyecto, fecha, nombres y carnés de todos; explicar responsabilidades.
2. Mostrar físicamente servidor, PCs cliente y conexión de red.
3. Mostrar arquitectura: clientes sin cadena SQL y servidor como único acceso a base.
4. Ejecutar los doce escenarios en orden. Preparar datos aislados para que el saldo y límites de un caso no invaliden otro.
5. Cada integrante explica su parte identificable: archivo C#, procedimiento SQL o pantalla, usando el índice de documentación.
6. Mostrar exportación, estructura documental y respaldo. Grabar imagen y audio legibles.
7. Completar y verificar el enlace al video antes de entregar.

No presentar conexiones locales automatizadas como prueba de varias PCs físicas. Registrar fecha, red utilizada, códigos, IP del servidor y resultado de cada escenario en una copia de esta tabla.
