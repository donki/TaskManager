-- ---------------------------------------------------------------------------
-- "is_priority" pasa a llamarse "is_pinned"
--
-- Se llamaba «prioritaria» y no lo era: no ordena por importancia, clava la
-- tarea arriba del todo. «Anclada» es lo que hace, y es lo que dice la
-- interfaz.
--
-- `rename column` conserva lo que hubiera marcado. La columna sigue siendo
-- `not null default false`.
-- ---------------------------------------------------------------------------

alter table public.tasks
    rename column is_priority to is_pinned;
