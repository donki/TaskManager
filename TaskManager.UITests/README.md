# TaskManager.UITests: pruebas de interfaz del móvil (Appium)

Tanda corta que maneja la app Android de verdad, como un dedo: Appium (Apache-2.0) con el driver
UiAutomator2, desde xUnit con `Appium.WebDriver` (Apache-2.0). No se ejecuta con la compilación
normal: necesita el servidor de Appium y un dispositivo o emulador.

## Qué prueba (10 pruebas, van en orden sobre la misma sesión)

| Prueba | Qué comprueba |
|---|---|
| T01 | La app recién borrada arranca, se entra **sin cuenta** (nunca con una cuenta real) y llega a «Mis tareas» (acepta el permiso de avisos y sale de Novedades con atrás). |
| T02 | El menú lateral abre cada entrada (Mis tareas, Calendario, Mis listas, Tablero, Mis grupos, Ajustes, Novedades, Acerca de): sale su título y el menú se cierra. |
| T03 | Atrás según Mobile §7: desde Calendario vuelve a Mis tareas; con el menú abierto solo lo cierra; desde el detalle vuelve a la lista; en inicio **oculta** la app (queda en segundo plano, no se cierra). |
| T04 | Idioma en → es desde Acerca de: el título y todas las entradas del menú cambian (los textos esperados se leen del propio `LocalizationService`). |
| T05 | Crear una tarea con la captura rápida y borrarla desde su detalle (papelera + confirmar). |
| T06 | Letra al 145 % (`font_scale 1.45`): ningún texto de Mis tareas ni del menú se sale de la pantalla ni se ve recortado por su contenedor. Deja la letra a 1.0 al acabar, pase lo que pase. |
| T07 | Detalle: con 14 etiquetas creadas, las pastillas de las que ya existen saltan de línea (más de una fila) y ninguna se sale por la derecha: sin desplazar de lado. |
| T08 | Filtro de «Mis tareas»: clic normal en una etiqueta deja solo esa; **Ctrl+clic** (tecla Ctrl abajo por acciones W3C, como un teclado físico) suma otra y salen las tareas de las dos; «Todas» lo suelta. |
| T09 | Las notas del detalle anuncian `image/*` al teclado (`dumpsys input_method`): es lo que deja pegar imágenes y recibir las de Gboard. |
| T10 | Pegar una imagen de verdad: el ayudante `TaskManager.UITests.Portapapeles` la deja en el portapapeles como otra aplicación (un `content://` suyo, con y sin tipo), y se pega con el botón de pegar adjunto y con la tecla de pegar en el título, las notas, las etiquetas y el paso nuevo. Cada vez sale arriba «Imagen añadida a Enlaces y ficheros» y hay un adjunto más; el título no cambia. |

Capturas de cada paso en `artifacts/<fecha-hora>/` (ignorada en git), más `06-letra145.txt` con
las medidas de cada texto a 1.0 y a 1.45, los fallos y los **avisos**.

### Lo que la prueba de letra grande puede y no puede ver

Desde fuera, Android da el texto entero aunque se pinte con elipsis, y no dice cuánto querría
ocupar. Lo que sí da es el rectángulo **visible** de cada texto. La prueba falla si un texto se
sale de la pantalla o si se ve **menos** que con la letra normal fuera de una zona desplazable, y
avisa (sin fallar) si apenas crece en alto (<x1.2), que es la señal de una fila de alto fijo. Un
texto de una línea con elipsis dentro de una fila que ya le deja crecer en alto **no** se detecta:
eso sigue necesitando el vistazo con el dedo (o comparar capturas).

## Cómo se lanza

Requisitos (todo en `D:\dev`, nunca en C:):

```powershell
# Una vez: Appium y su driver
cd D:\dev\appium; npm init -y; npm install appium
$env:APPIUM_HOME = 'D:\dev\appium-home'
D:\dev\appium\node_modules\.bin\appium.cmd driver install uiautomator2
```

1. Emulador en marcha: MuMu (`127.0.0.1:16416`). Toma el cerrojo `D:\sOCProjects\.mumu.lock` y
   suéltalo al acabar. Si adb no responde:
   `MuMuManager.exe control -v 1 shutdown` y luego `launch`.
2. La app instalada o pásala en `TM_APK`. APK Release firmado con la clave compartida:
   `dotnet publish TaskManager.Mobile -c Release -f net10.0-android36.0 -p:AndroidPackageFormat=apk -p:AndroidSigningKeyStore=... -p:AndroidSigningKeyAlias=... -p:AndroidSigningStorePass=... -p:AndroidSigningKeyPass=...`
   y `adb -s 127.0.0.1:16416 install -r <apk>`.
3. El ayudante del portapapeles (para T10; la prueba lo instala si no está y lo quita al acabar):
   `dotnet build TaskManager.UITests.Portapapeles -c Release` (o su APK en `TM_CLIP_APK`).
4. Ejecuta:

```powershell
dotnet build TaskManager.UITests -m:1 -nodeReuse:false
dotnet test TaskManager.UITests --no-build
```

La prueba arranca sola el servidor de Appium si no hay uno escuchando en el 4723, y lo cierra al
acabar. **Borra los datos de la app** en el dispositivo al empezar (`pm clear`, por `noReset=false`):
úsala solo en un emulador o dispositivo de pruebas, nunca en el móvil de alguien con su cuenta.

Variables (todas opcionales):

| Variable | Por defecto | Para qué |
|---|---|---|
| `TM_UDID` | `127.0.0.1:16416` | Serie adb del dispositivo. |
| `TM_APPIUM_URL` | `http://127.0.0.1:4723/` | Servidor ya arrancado. |
| `TM_APPIUM` | `D:\dev\appium\node_modules\.bin\appium.cmd` | Appium para arrancarlo si no responde. |
| `APPIUM_HOME` | `D:\dev\appium-home` | Donde están los drivers. |
| `ANDROID_HOME` | `D:\dev\android-sdk` | SDK con adb. |
| `TM_APK` | (ninguna) | APK a instalar antes de empezar. Ojo: con el mismo versionCode que el instalado, Appium no lo reinstala; instálalo antes con `adb install -r`. |
| `TM_CLIP_APK` | el de `TaskManager.UITests.Portapapeles/bin/Release` | APK del ayudante que deja una imagen en el portapapeles (T10). |

## Identificadores

Los controles que se tocan llevan `AutomationId` en el XAML. En Android, MAUI lo publica como
`resource-id` (`com.socratic.taskmanager:id/Nombre`) en los controles normales, pero como
`content-desc` en los botones de la cabecera (`ToolbarItem`); `UiSession.Id()` busca por los dos.
Si añades una prueba, pon `AutomationId` al control en vez de buscarlo por su texto (cambia con
el idioma).

## Medidas del piloto (2026-09-30, MuMu Android 15, 1080×1920)

6 pruebas, 6 pasan, en 3 tandas seguidas con el mismo resultado: 116, 113 y 114 s por tanda
(unos 105 s de pruebas más ~10 s de arrancar Appium y la sesión), sin contar compilar ni arrancar
el emulador.

## Tanda del 2026-10-07 (AVD Phone_API34 sin ventana, Android 14, 1080×2400)

10 pruebas, 9 pasan, en 5 min 14 s. T09 y T10 (pegar una imagen) pasan, también sueltas. T08 falla
en este emulador al sumar la segunda etiqueta con Ctrl+clic (la tecla Ctrl por acciones W3C no
llega como en MuMu); ahora suelta el filtro pase lo que pase, para no esconder las tareas de las
pruebas siguientes, y busca las pastillas desplazando la tira de etiquetas. En MuMu no se ha
vuelto a lanzar.
