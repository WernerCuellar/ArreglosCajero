# Entrega al jefe de grupo

## Contenido y primer recorrido

1. Abrir LEEME.md y CajeroRed.sln en Visual Studio 2022.
2. Localizar la matriz y el índice de archivos para la defensa.
3. En otra PC servidor: instalar o restaurar la base, configurar la identidad SQL y la cadena local.
4. Iniciar Servidor, inicializar cajeros para el día y conectar al menos tres PCs con códigos diferentes.
5. Ejecutar/verificar los doce escenarios y completar el video grupal.

La fase I permanece intacta en Proyecto_Cajero. Esta solución nueva usa C#/WPF y SQL Server. Se simplificó el registro a los datos de D-10, manteniendo el estilo azul de las pantallas anteriores. Los módulos de remesas, pagos, adelantos, NFC y ESP32 de Python no se presentan como migrados en esta entrega; consultar el enunciado original de fase I si se solicitan extras.

## Subir a GitHub

No se ha creado repositorio remoto ni se ha publicado nada. Subir la carpeta de esta solución como proyecto independiente, sin mezclar la fase I ni su Fase_II ignorada. La solución no depende de rutas al proyecto Python.

Incluir código, solución, scripts SQL, ejemplos de configuración, manuales, diagramas, PDF y pruebas. El .gitignore excluye bin/obj, cachés, configuraciones privadas y ejecutables publicados. Adjuntar el .bak y paquetes portables por el mecanismo de entrega o publicaciones del repositorio; no versionar carpetas de dependencias ni contraseñas.

Antes de publicar, revisar los archivos que se van a agregar. Los usuarios y credenciales del seed son ficticios de demostración. No copiar datos, secretos de hardware ni configuraciones de la fase I. El enlace al video y los datos reales de integrantes se completan por el jefe.

## Integración futura con otro grupo

Compartir solo Documentacion/08_IntegracionEntreGrupos. Completar la ficha, acordar campos y versiones y ensayar con datos ficticios. La implementación vigente es Codigo/Aplicacion/BancoInterbancario.cs; Bancos de la red permite verificar y transferir con bancos autorizados. Leer el contrato vigente antes del ensayo físico.

Las transferencias futuras deben ejecutarse servidor a servidor, con autenticación de instituciones, identificación de cuentas, idempotencia persistida, consulta de estado y conciliación. No agregar una cadena SQL remota a los cajeros. No interpretar un timeout como rechazo ni revertir dinero sin confirmar el estado remoto.

## Completar antes de entregar

- Fecha de entrega: por confirmar con el catedrático.
- Integrantes, carnés y responsables: completar con datos reales.
- PC servidor, red utilizada y códigos de cada integrante: completar.
- Resultados y evidencia de los doce escenarios físicos: completar.
- Enlace del video: pendiente de grabación.
- Banco/identificador del otro grupo y operaciones acordadas: pendiente de coordinación posterior.

Distribuir responsabilidades reales y explicables. Esta guía no inventa quién desarrolló cada módulo ni sustituye la explicación individual exigida.
