# Avisos de terceros — Task Manager

Todas las dependencias son de licencia permisiva y uso comercial permitido (constitución general,
sección 1: MIT-compatible y monetizable).

| Paquete | Versión | Licencia | Para qué |
|---|---|---|---|
| `HandyControl` | 3.5.1 | MIT | Controles de la aplicación de escritorio: etiquetas, avisos emergentes, selectores de fecha y hora, desplegables y casillas |
| `sqlite-net-pcl` | 1.9.172 | MIT | Almacén local en Android y Windows |
| `SQLitePCLRaw.bundle_green` | 2.1.11 | Apache-2.0 | Proveedor SQLite del anterior |
| `SQLitePCLRaw.core` | 2.1.11 | Apache-2.0 | Igual |
| `SQLitePCLRaw.provider.e_sqlite3` | 2.1.11 | Apache-2.0 | Igual |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.11 | Apache-2.0 (SQLite: dominio público) | Binario nativo de SQLite |
| `MailKit` / `MimeKit` | 4.17.0 | MIT | Lectura de correo (hoy oculta, ver `FeatureOptions.MailEnabled`) |
| `BouncyCastle.Cryptography` | 2.6.2 | MIT | Criptografía que necesita MailKit |
| `Microsoft.Maui.Controls` | la del SDK | MIT | Interfaz de la aplicación Android |
| `Microsoft.Extensions.Logging.Debug` | 10.0.2 | MIT | Traza solo en Debug |
| `System.Security.Cryptography.ProtectedData` | 10.0.2 | MIT | Cifrar los tokens de sesión con DPAPI en Windows |
| WPF / Windows Forms (`NotifyIcon`) | .NET 10 | MIT | Bandeja del sistema en Windows |

**Apache-2.0 no es MIT**, pero cumple la regla: es permisiva, admite uso comercial y es compatible
con MIT. Añade una concesión de patentes y la obligación de conservar el aviso, que es este fichero.

**HandyControl viene con sus textos en chino** y sin ningún otro idioma dentro. No se ha modificado
la biblioteca: la aplicación le sustituye el diccionario en el arranque
(`Localization/HandyControlLang.cs`) para enseñarlos en castellano o en inglés.

## Modelo de IA

El desglose no incluye ningún modelo: habla con un servidor local que ponga el usuario. El
recomendado, **Qwen2.5 3B Instruct**, es **Apache-2.0** y admite uso comercial. Cualquier modelo que
se elija tiene que cumplir la misma regla — quedan descartados los de licencia no comercial.

## Datos

La aplicación **necesita cuenta** y sincroniza contra un servidor: es lo que permite que las mismas
tareas estén en el móvil y en el ordenador. Sin eso no habría forma de saber que dos aparatos son la
misma persona.

- Se entra con una cuenta que el usuario ya tiene (Google o Microsoft). No se crea ninguna cuenta
  propia y no se ve, ni se recibe, ni se guarda ninguna contraseña; del proveedor solo llegan el
  identificador, el nombre, el correo y la foto de perfil.
- Los datos se guardan en **Supabase** (región `eu-west-2`, Londres), que actúa como alojamiento y
  no los usa para nada suyo.
- **El texto que escribe el usuario se cifra en el dispositivo antes de subir** y se guarda cifrado:
  en el servidor no es legible. Sin cifrar quedan solo las fechas, las marcas de estado y los
  identificadores, que son los que el servidor necesita para funcionar.
- No hay anuncios, ni rastreadores, ni analítica.

MIT · Copyright © 2026 Socratic
