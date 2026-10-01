# Solicitud de acceso a producción en Google Play — Task Manager

Respuestas para el cuestionario de Play Console › **Panel › Solicitar acceso a producción**, en
catalán (el idioma de la consola). Cada texto cabe en los 300 caracteres del formulario; el número
entre paréntesis es su longitud. Constitución Mobile §11. **Última actualización: 2026-10-01**
(versión 2026.09.30.00). Estado en Play: prueba cerrada (alpha 2026.09.08.1 publicada; la 2026.09.30.00 en borrador; en la pista desde el 2026-09-08).

> Lo marcado con ⚠ no lo puedo saber yo: compruébalo en la consola antes de enviarlo y cámbialo si
> no es así.
>
> - ⚠ En alpha publicada solo está la 2026.09.08.1: envía a revisión el borrador 2026.09.30.00 antes de pedir producción (los cambios de abajo son de las versiones posteriores).
> - ⚠ Es la única con **cuenta obligatoria (Google o Microsoft) y servidor**: la Seguridad de los datos tiene que declarar cuenta, sincronización y cifrado, y la política de privacidad decir que hay cuenta y servidor.

---

## Informació sobre la prova tancada

**Com has reclutat usuaris per a la prova tancada?** (273) ⚠ *comprueba que la usas a diario en el Xiaomi; si no, deja solo «en un mòbil real».*

```
He afegit a la prova tancada quatre grups públics de Google de verificadors voluntaris (comunitats d'intercanvi de proves de 12 persones durant 14 dies). No he fet servir cap proveïdor de pagament. També l'he fet servir cada dia en un mòbil real amb Android 16 i a Windows.
```

**Fins a quin punt t'ha resultat fàcil reclutar verificadors?** — Propuesta: **Ni fàcil ni difícil** (los grupos públicos dan el número, pero participan poco).

**Descriu la implicació dels verificadors** (253) ⚠ *comprueba en Estadísticas / Prova tancada que de verdad la abrieron; si no hay datos, quita la parte de las funciones.*

```
Els verificadors han entrat amb el seu compte de Google o Microsoft i han creat tasques, micropassos i llistes. Un usuari real també faria servir els grups per compartir llistes amb la família o l'equip i la versió de Windows, que en la prova no s'usen.
```

**Resum dels suggeriments i com els has recollit** (241) ⚠ *si algún verificador dejó comentarios (en la consola o por correo), menciónalos.*

```
Pocs comentaris escrits dels verificadors; els he recollit des de la consola de Play i GitHub. Les millores han sortit del meu ús diari al mòbil i a Windows i del banc de proves: el botó enrere, no perdre el que s'escriu i la sincronització.
```

## Informació sobre l'aplicació

**A quin públic objectiu va dirigida?** (196)

```
Persones que volen organitzar les tasques del dia, soles o en grup (família, pis compartit, equip petit), al mòbil i a Windows. Cal un compte de Google o Microsoft per sincronitzar; sense anuncis.
```

**Com proporciona valor als usuaris?** (246)

```
Tasques del dia amb micropassos, repeticions, etiquetes, calendari i tauler, XP i ratxes per motivar, i llistes compartides en grup. Se sincronitza entre mòbil i Windows i el text de les tasques viatja i es desa xifrat al servidor. Sense anuncis.
```

**Instal·lacions esperades el primer any** — Propuesta: **0 - 10.000** (app nueva, sin promoción).

## Preparació per a la producció

**Quins canvis has fet en funció de la prova tancada?** (244) ⚠ *todo eso va en el borrador 2026.09.30.00: envíalo a revisión antes.*

```
He afegit repeticions, etiquetes i enganxar imatges, he tret l'assistent d'IA, el botó enrere ja no tanca l'app a Android 16, pregunta abans de perdre el que s'escriu, un error ja no la tanca, la sincronització no repeteix pujades i 254 proves.
```

**Com has decidit que està preparada per a producció?** (268) ⚠ *comprueba en Qualitat › Android Vitals que no hay fallos; si los hay, quita «sense tancaments a la consola». Comprueba que la Seguridad de los datos ya declara la cuenta y la sincronización.*

```
Les 254 proves automàtiques passen totes, l'he provada en un mòbil real amb Android 16 sense errors, els verificadors l'han fet servir 14 dies sense tancaments a la consola i la fitxa, la privadesa i la seguretat de les dades (compte i sincronització) estan completes.
```
