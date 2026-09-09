-- ---------------------------------------------------------------------------
-- 12 · «En curso» y series de repeticion
--
-- Dos columnas nuevas en `tasks`:
--
--   · in_progress — la columna del medio del tablero de Windows: empezada pero sin terminar. Es un
--     campo aparte y no un estado de tres valores porque `is_done` ya manda en los filtros, las
--     rachas y la XP, y convertirlo en un numero obligaria a repasar cada consulta.
--
--   · series_id  — de que serie de repeticion es cada tarea. Una tarea que se repite ya no es una
--     fila que reaparece al completarla: son todas sus vueltas escritas de una vez, una por cada
--     dia en que toca entre la fecha de planificacion y la de finalizacion. Esto es lo que las
--     mantiene juntas para poder rehacer lo que queda cuando cambian las fechas, y lo que evita el
--     duplicado (con la serie escrita, completar una vuelta no crea la siguiente).
--
-- Hay que ejecutarlo ANTES de usar la version nueva: la aplicacion manda las dos columnas en cada
-- tarea que sube, y sin ellas PostgREST rechaza el lote entero.
--
-- Se puede ejecutar mas de una vez sin romper nada.
-- ---------------------------------------------------------------------------

alter table public.tasks add column if not exists in_progress boolean not null default false;
alter table public.tasks add column if not exists series_id   uuid;

-- Las vueltas de una serie se piden siempre juntas y ordenadas por fecha.
create index if not exists tasks_series_idx on public.tasks (series_id)
    where series_id is not null;

-- El tablero pregunta por lo que esta empezado, que siempre es un puñado de filas.
create index if not exists tasks_in_progress_idx on public.tasks (list_id)
    where in_progress;
