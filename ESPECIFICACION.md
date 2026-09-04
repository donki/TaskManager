# Especificación Funcional — Task Manager

> Documento de referencia del producto. Es la fuente de verdad de **qué** hace la aplicación;
> el **cómo** está en [ARQUITECTURA.md](ARQUITECTURA.md).
>
> Versión inicial: 2026-08-29.

## 1. Visión General del Producto

Task Manager es una aplicación de gestión de tareas diarias que combina la simplicidad, rapidez y
estructura de herramientas como Microsoft To-Do con un sistema de gamificación cooperativa,
celebración visual y sincronización en tiempo real.

Diseñada para combatir la procrastinación y facilitar el trabajo colaborativo, la aplicación permite
organizar proyectos en múltiples listas por grupo, desglosar tareas complejas mediante Inteligencia
Artificial y premiar el progreso individual o colectivo con animaciones, sonidos y elementos de
recompensas.

## 2. Gestión de Grupos, Listas y Seguridad (Supabase + RLS)

### A. Jerarquía de Organización

- **Usuario**: cuenta individual con su espacio personal de tareas ("Mi Día", "Mis Listas Privadas").
- **Grupos**: espacios de trabajo o convivencia compartidos (ej. "Familia", "Proyecto Startup",
  "Piso Compartido").
- **Múltiples listas de tareas por grupo**: un solo grupo puede contener distintas listas
  independientes para mantener la organización. Por ejemplo, el grupo "Familia" puede tener las
  listas *Compras del Supermercado*, *Mantenimiento del Hogar* y *Planes de Vacaciones*.

### B. Cuenta de usuario: entrada con Google o con Microsoft

- **Entrar es obligatorio**: es lo que permite recuperar las listas en otro dispositivo y ser
  reconocido dentro de un grupo. Sin cuenta no hay aplicación: la pantalla de entrada no tiene otra
  salida que cerrarla.
- Se entra **con Google o con Microsoft**, hablando con el proveedor directamente (PKCE). La
  aplicación no guarda ninguna contraseña; solo el testigo, en el almacén seguro del dispositivo.
- La identidad es el identificador de la cuenta —el `sub` de Google o el `oid` de Microsoft—: el
  mismo en todos los aparatos, y no cambia aunque el usuario se cambie el nombre o el correo. El
  nombre de la cuenta es también el nombre en la aplicación, y su foto la que ven sus compañeros de
  grupo en el Tablón y en las celebraciones.
- **Cada cuenta tiene sus listas en el mismo aparato.** Cambiar de Google a Microsoft cambia lo que
  se ve; no borra ni mueve nada, y volver a la anterior lo devuelve todo donde estaba.
- **Un usuario pertenece a los grupos que quiera** y, por tanto, tiene listas de tareas de varios
  grupos a la vez, además de las suyas privadas. Todas conviven en "Mi Día".
- Lo escrito antes de que hubiera cuenta (tareas, XP y rachas) se lo queda la primera que entra.

### C. Acceso por Clave Compartida

- **Creación de un nuevo grupo**: al crear un grupo, la aplicación genera o permite definir una
  Clave Compartida única.
- **Unirse a un grupo existente**: los nuevos miembros no requieren invitaciones por correo ni
  aprobaciones complejas; únicamente introducen la Clave Compartida para vincularse al grupo al
  instante.

### D. Seguridad e Infraestructura (Supabase + RLS)

- **Supabase como backend**: autenticación, base de datos PostgreSQL y sincronización en tiempo real.
- **Row Level Security (RLS) basado en Clave Compartida**: las políticas a nivel de fila restringen
  el acceso a los datos. Solo quien haya validado la Clave Compartida correspondiente puede leer,
  crear, editar o eliminar las listas y tareas asociadas a ese grupo específico.
  El mecanismo exacto (canje de la clave por pertenencia, en vez de reenviarla en cada petición)
  está detallado en [ARQUITECTURA.md § Seguridad](ARQUITECTURA.md).

## 3. Experiencia de Usuario e Interfaz (UI/UX)

- **Estilo visual**: limpio, moderno y minimalista, con soporte nativo para Modo Claro y Modo Oscuro.
- **Navegación principal**:
  - **Mi Día** — vista enfocada únicamente en las tareas seleccionadas para la jornada actual
    (personales o de grupos). Se reinicia cada medianoche.
  - **Mis Listas Privadas** — listas de tareas de uso personal e individual.
  - **Mis Grupos** — panel con los grupos a los que pertenece el usuario. Al seleccionar un grupo se
    despliegan sus diferentes listas de tareas.
  - **El Tablón del Gremio** — estadísticas de productividad, nivel del equipo, racha actual y
    elementos estéticos desbloqueados.

## 4. Celebración y Gamificación

### A. Momento de la celebración (al completar una tarea)

- **Efectos visuales**: explosión sutil de confeti en pantalla, destellos dorados e indicador
  flotante de puntos de experiencia (ej. "+50 XP").
- **Feedback háptico y sonoro**: vibración agradable en el móvil y un efecto de sonido alegre y
  gratificante (configurable o silenciable).
- **Mecánica de combos**: al completar varias tareas seguidas en un lapso corto se activa un
  multiplicador de racha ("¡Racha x3!") con efectos visuales incrementales.
- **Celebración grupal**: cuando un integrante completa una tarea de una lista compartida, el resto
  recibe una animación sutil o notificación interactiva para enviar reacciones rápidas (aplausos,
  emojis o felicitaciones).

### B. Sistema de progresión

- **XP y niveles**: se ganan puntos al completar tareas o desglosar objetivos con IA. Subir de nivel
  desbloquea temas de color, nuevos estilos de confeti o insignias para el grupo.
- **Sin castigos punitivos**: se premia la constancia diaria sin penalizar agresivamente al usuario
  cuando necesita tomarse días de descanso.

## 5. Desglose de Tareas con IA ("Pasos Mágicos")

- **Creación rápida**: el usuario escribe un objetivo amplio dentro de cualquier lista
  (ej. "Organizar la mudanza").
- **Botón "Desglosar con IA"**: al pulsar la varita mágica, la aplicación genera en menos de un
  segundo una sublista de 3 a 5 micro-pasos ejecutables de 5 a 10 minutos (ej. "1. Conseguir cajas de
  cartón", "2. Clasificar ropa por temporada", "3. Etiquetar cajas frágiles").
- **Progreso progresivo**: cada micro-paso completado suma porciones de XP y hace avanzar la barra de
  progreso general de la tarea.

## 6. Aplicación de Escritorio en Windows (Tray Icon)

### A. Comportamiento en segundo plano

- **Inicio con Windows**: la aplicación se ejecuta automáticamente al encender el equipo y se
  minimiza discretamente en la bandeja del sistema.
- **Tray icon interactivo**: muestra un indicador con el número de tareas pendientes de "Mi Día".

### B. Ventana flotante (flyout) y atajos

- **Acceso de un clic**: al hacer clic en el icono de la bandeja se despliega una ventana flotante
  rápida (estilo panel de Windows 11).
- **Añadido ultrarrápido**: escribir y guardar una tarea en cualquier lista (privada o de grupo) en
  menos de 2 segundos.
- **Atajo de teclado global**: una combinación configurable (por defecto `Ctrl + Alt + T`) despliega
  el panel de captura rápida sobre cualquier aplicación o juego en ejecución.
- **Celebración en escritorio**: completar una tarea desde la ventana flotante activa una
  mini-animación de confeti en la esquina de la pantalla.

## 7. Aplicación Móvil (Android)

- **Sincronización inmediata**: los cambios en listas compartidas se reflejan en tiempo real entre
  Windows y Android mediante Supabase Realtime.
- **Widgets para pantalla de inicio**: widget transparente con la lista "Mi Día" y acceso directo
  para añadir tareas o desglosar con IA.
- **Funcionamiento offline**: consultar, crear y completar tareas sin conexión, sincronizando los
  cambios en cuanto se restablezca la red.
