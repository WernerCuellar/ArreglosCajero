# Alcance y decisiones

Referencia: `PROYECTO_CAJERO_FASE_II_CLIENTE_SERVIDOR.pdf`, 13 páginas entregadas por el usuario. El enunciado es una fuente de requisitos; las decisiones de alcance del usuario prevalecen sobre instrucciones ajenas al proyecto.

La fase II se implementa en C# con WPF para servidor y cajero. Todo dato operativo reside en SQL Server. Se conserva la identidad visual clásica: azul #163A82, azul de interacción #245FD4, fondo #F4F7FC, tablas alternadas y botones de navegación. Se adapta el diseño a ventanas redimensionables, sin depender de PySide6.

El registro administrativo solicita nombre, tarjeta de 16 dígitos, PIN y confirmación, saldo inicial y límite diario (D-10). Identificador, estado y fecha son automáticos. No se exige llenar dirección, propiedad, actividad económica, ingresos o beneficiarios para una operación de cajero. La fase I original se conserva como referencia y no se migran ni alteran sus datos personales reales.

Se implementan las operaciones de usuario enumeradas por la fase II: retiro, depósito, cambio de PIN, consulta de saldo y últimas cinco transacciones. Pagos de servicios, remesas, adelantos, NFC y ESP32 de la aplicación previa no forman parte del flujo obligatorio descrito en el PDF de fase II. No se afirma su migración; si el catedrático exige adicionales del enunciado original de fase I, revisar ese documento antes de ampliar el alcance.

El número de cajeros se configura en base de datos, sin una constante de cinco. La capacidad efectiva depende del equipo y de la red. Se prueba el mínimo de tres conexiones; no se afirma escalabilidad ilimitada.

Actualización 31/10/2026: se implementa NETGEAR con discovery UDP y protocolo IB del MD recibido. Ver ProtocoloExterno.md para atomicidad local, reservas, deduplicación y límites de interoperabilidad. Los ensayos usan pares simulados y no acreditan una prueba física con otro grupo.

Los requisitos deseables quedan separados: S-12 correo; D-17 supervisor, temas, sonido; C-12 impresión y teclado táctil (PDF implementado al 31/10/2026). La tabla Administrador admite el rol Supervisor y el servicio rechaza escrituras para él, pero el acceso completo de solo lectura no se acredita sin su prueba de aceptación.
