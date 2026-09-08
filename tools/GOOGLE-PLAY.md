# Task Manager en Google Play

`com.socratic.taskmanager` · **pista de pruebas cerradas (`alpha`)** · la app ya está dada de alta.

Estado del 2026-09-08: subida la versión **2026.09.08.1** (versionCode `2026090801`) como
**borrador**, y la ficha (textos, icono y gráfico destacado) publicada por API. Falta lo que solo se
puede hacer en el formulario de la consola, y sin ello no se puede publicar (ver el final).

## La clave de subida es propia de esta app

**No se firma con la clave compartida.** Play la rechazó dos veces, antes de que existiera ningún
envío:

```
403 APK signed with a key that is also used to sign an APK that is delivered to users.
Because this app is enrolled in App Signing, you should create a different key.
```

Así que Task Manager tiene la suya: `Mobile\Shared\socratic-taskmanager-upload.keystore`, alias
`taskmanager`, SHA1 `34:9A:0D:32:01:5E:CF:08:EA:89:81:4C:69:74:18:86:9D:B7:06:D5`, con la misma
contraseña que la compartida (fuera del repositorio).

**Y ya no se puede cambiar por reintento.** Una vez entró el primer bundle, esa clave quedó fijada
como clave de subida: al probar otra vez con la compartida —incluso después de vaciar la pista— la
respuesta fue `The Android App Bundle was signed with the wrong key. Found: C1:CF:43:…, expected:
34:9A:0D:…`. El artefacto se queda en la biblioteca de Play y eso no se borra. Volver atrás exigiría
pedir un *upload key reset* en la consola.

Esto **no cambia nada en los dispositivos**: con App Signing, lo que instala Play va firmado con la
clave que genera Google, no con la nuestra. La clave de subida solo decide quién puede subir.

> Lo que se instala por USB sigue firmado con la clave compartida (`Shared\signing.props`), que es lo
> que permite actualizar una compilación de pruebas sobre otra. Un APK de Play y uno de aquí **no**
> se actualizan entre sí: son firmas distintas.

## Subir una versión

```powershell
$pass = (Get-Content D:\sOCProjects\password.txt -Raw).Trim()

# 1. El AAB, firmado con la clave de subida de ESTA app (la compartida no vale).
dotnet publish TaskManager.Mobile\TaskManager.Mobile.csproj -c Release -f net10.0-android36.0 `
  -p:AndroidPackageFormat=aab `
  -p:AndroidSigningKeyStore="D:\sOCProjects\Mobile\Shared\socratic-taskmanager-upload.keystore" `
  -p:AndroidSigningKeyAlias=taskmanager `
  -p:AndroidSigningStorePass=$pass -p:AndroidSigningKeyPass=$pass

# 2. A la pista cerrada, como borrador (sin mandar a revisión).
pwsh D:\sOCProjects\Mobile\Hiker\Hiker\publish_aab_to_play.ps1 `
  -ServiceAccountJson "D:\sOCProjects\Mobile\Hiker\Hiker\hiker-433118-98861f2881fa.json" `
  -PackageName "com.socratic.taskmanager" `
  -AabPath "...\publish\com.socratic.taskmanager-Signed.aab" `
  -Track alpha -Status draft -ReleaseName "2026.09.08.1" -ReleaseNotes "..." `
  -SkipStoreListing -SkipStoreIcon -SendForReview:$false -AssumeYes
```

Si el `dotnet publish` no vuelve a firmar (porque no ha cambiado nada), hay que borrar antes el
`.aab` de `bin\Release\net10.0-android36.0\` **y** el de `publish\`: si no, sube el de la vez
anterior con la firma de la vez anterior.

## Ficha

Los textos y las imágenes están en `Mobile\GooglePlayConsole\TaskManager\` y se suben con:

```powershell
pwsh .\tools\publicar-ficha-play.ps1
```

El script lee `ficha.md` —los bloques de código de cada idioma, en orden: título, descripción breve
y descripción completa—, comprueba los límites de Play (30, 80 y 4000 caracteres) y sube textos e
imágenes para `es-ES` y `en-US`: el icono, el gráfico destacado y las **capturas** de
`capturas\<idioma>\`, que se suben en el orden del nombre del fichero. Cambiar la ficha es cambiar
ese `.md` (o las imágenes) y volver a ejecutarlo.

Las capturas se hacen con la **compilación de demostración** —`-p:Demo=true`, y `-p:DemoLang=en`
para el juego en inglés—: entra sin cuenta, siembra tareas inventadas y usa otra base de datos, así
que no hay que enseñar las tareas de nadie ni tocar las de verdad. Se instala encima de la buena y
luego se reinstala la buena.

## Verificadores

La pista cerrada lleva los **cuatro grupos de Google de siempre** (los mismos que las otras apps;
la lista está en `d:\sOCProjects\GRUPOS-VERIFICADORES.md`), puestos el 2026-09-08 con:

```powershell
pwsh .\tools\poner-verificadores-play.ps1
```

Ese script **lee primero los que hay y fusiona**: la API reemplaza la lista entera, así que escribir
solo los cuatro se llevaría por delante cualquier otro grupo puesto a mano.

## Lo que falta, y es formulario de la consola

Nada de esto se puede hacer por API:

1. **Clasificación del contenido**: el cuestionario.
2. **Seguridad de los datos**: hay cuenta, hay sincronización y el texto va cifrado en tránsito y en
   reposo.
3. **Público objetivo y anuncios**: no hay anuncios.
4. **Política de privacidad**: la URL, y tiene que ser la **nueva** —la que dice que hay cuenta y
   servidor—, no la vieja que decía que no había ninguna de las dos cosas.

Mientras falte algo de eso, el borrador de la pista cerrada no se puede enviar a revisión.
