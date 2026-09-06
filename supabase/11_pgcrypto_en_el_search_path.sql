-- =============================================================================
--  11. pgcrypto en el search_path de las funciones de grupo
-- =============================================================================
--
--  Sintoma: crear un grupo devolvia 404 y la aplicacion se quedaba con un grupo
--  local sin codigo. El 404 despistaba —parecia que faltaba la funcion— pero el
--  cuerpo de la respuesta decia otra cosa:
--
--      {"code":"42883","message":"function gen_salt (unknown) does not exist"}
--
--  O sea: create_group existe y se ejecuta, y lo que falla es la llamada a
--  gen_salt() de dentro. PostgREST traduce el 42883 de Postgres a un 404 HTTP.
--
--  Por que. Las tres funciones estan declaradas `set search_path = public`, que
--  es lo correcto para no depender del search_path de quien llame. Pero en un
--  proyecto de Supabase de hoy **pgcrypto vive en el esquema `extensions`**, no
--  en `public`: `create extension if not exists pgcrypto` (01_schema.sql) lo dio
--  por instalado porque ya lo estaba... en otro esquema. Con `public` a secas,
--  crypt() y gen_salt() no se ven desde dentro de la funcion.
--
--  Se arregla añadiendo `extensions` al search_path de las tres, en vez de
--  volver a escribir sus cuerpos: asi no hay dos versiones de la misma funcion
--  que puedan separarse con el tiempo.
--
--  Idempotente: se puede ejecutar las veces que haga falta.
-- =============================================================================

alter function public.create_group(uuid, text, text, text)
    set search_path = public, extensions;

alter function public.join_group(text, text, text)
    set search_path = public, extensions;

alter function public.rotate_group_key(uuid, text)
    set search_path = public, extensions;

-- Comprobacion rapida, sin crear nada: tiene que devolver una cadena que empiece
-- por «$2a$» (un salt de bcrypt). Si esto falla, pgcrypto no esta instalado en
-- ningun sitio y hay que instalarlo desde el panel (Database > Extensions).
do $$
begin
    perform extensions.gen_salt('bf');
    raise notice 'pgcrypto accesible: las funciones de grupo pueden hashear la clave';
end;
$$;
