# Avisos de terceros — Task Manager

Todas las dependencias son de licencia permisiva y uso comercial permitido (constitución 4: MIT y
monetizable).

| Paquete | Versión | Licencia | Para qué |
|---|---|---|---|
| `sqlite-net-pcl` | 1.9.172 | MIT | Almacén local en Android y Windows |
| `SQLitePCLRaw.bundle_green` | 2.1.11 | Apache-2.0 | Proveedor SQLite del anterior |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.13 | Apache-2.0 (SQLite: dominio público) | Binario nativo de SQLite, fijado al día por NU1903 |
| `SQLitePCLRaw.lib.e_sqlite3.android` | 2.1.13 | Apache-2.0 | Igual, para Android |
| `Microsoft.Maui.Controls` | la del SDK | MIT | Interfaz de la app Android |
| `Microsoft.Extensions.Logging.Debug` | 10.0.2 | MIT | Traza solo en Debug |
| `System.Security.Cryptography.ProtectedData` | 10.0.2 | MIT | Cifrar los tokens de sesión con DPAPI en Windows |
| WPF / Windows Forms (`NotifyIcon`) | .NET 10 | MIT | Bandeja del sistema en Windows |

## Modelo de IA

El desglose no incluye ningún modelo: habla con un servidor local que ponga el usuario. El
recomendado, **Qwen2.5 3B Instruct**, es **Apache-2.0** y admite uso comercial. Cualquier modelo que
se elija tiene que cumplir la misma regla — quedan descartados los de licencia no comercial.

## Datos

Task Manager no envía nada a servicios de terceros por su cuenta. Si se configura Supabase, los
datos van al proyecto que indique el usuario, y a ningún otro sitio.

La entrada con Google la gestiona ese mismo proyecto de Supabase: la aplicación solo abre el
navegador del sistema y recibe la sesión. No se guarda ninguna contraseña, y de Google solo llegan
el correo, el nombre y la foto de perfil.

MIT · Copyright © 2026 Socratic
