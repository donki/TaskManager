-- ---------------------------------------------------------------------------
-- Los limites de longitud ya no valen: lo que se guarda es texto cifrado
--
-- `tasks.title`, `task_steps.title` y `task_lists.name` llevaban un tope de
-- caracteres (200, 200 y 60). Tenia sentido cuando la columna guardaba lo que
-- el usuario habia escrito; con el texto cifrado (`enc1:` + base64) la longitud
-- ya no es la del titulo: 28 bytes de cabecera y un tercio mas por el base64.
-- Un titulo de 130 caracteres se pasaba de 200 y el servidor rechazaba el lote
-- entero — una sola tarea larga dejaba sin subir a todas las demas.
--
-- Se conserva la parte que sigue significando algo: que no este vacio. El tope
-- de verdad se aplica donde se escribe, que es donde se le puede decir al
-- usuario que se ha pasado; aqui solo llegaria como un rechazo sin explicacion.
-- ---------------------------------------------------------------------------

alter table public.tasks       drop constraint if exists tasks_title_check;
alter table public.task_steps  drop constraint if exists task_steps_title_check;
alter table public.task_lists  drop constraint if exists task_lists_name_check;

alter table public.tasks
    add constraint tasks_title_check check (length(btrim(title)) >= 1);

alter table public.task_steps
    add constraint task_steps_title_check check (length(btrim(title)) >= 1);

alter table public.task_lists
    add constraint task_lists_name_check check (length(btrim(name)) >= 1);
