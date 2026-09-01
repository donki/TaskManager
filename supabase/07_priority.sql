-- ---------------------------------------------------------------------------
-- "tasks.is_priority": la tarea va por encima de todas las demas
--
-- Ordenar lo hace el cliente (hecha, prioritaria, vencimiento, orden manual,
-- creacion): aqui solo hace falta que la columna exista y viaje entre
-- dispositivos.
--
-- NOT NULL con default false: las filas que ya estaban quedan resueltas sin
-- tocarlas, y una version antigua de la aplicacion que suba una fila sin este
-- campo sigue funcionando.
-- ---------------------------------------------------------------------------

alter table public.tasks
    add column if not exists is_priority boolean not null default false;
