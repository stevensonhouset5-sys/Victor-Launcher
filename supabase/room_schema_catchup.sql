create extension if not exists pgcrypto;

alter table if exists public.mod_rooms
  add column if not exists manifest_version integer not null default 1;

alter table if exists public.mod_rooms
  add column if not exists download_enabled boolean not null default true;

alter table if exists public.mod_rooms
  add column if not exists max_file_count integer not null default 32;

create or replace function public.normalize_room_code(p_room_code text)
returns text
language sql
immutable
as $func$
  select upper(trim(coalesce(p_room_code, '')));
$func$;

create or replace function public.validate_manifest_shape(p_manifest jsonb, p_max_file_count integer default 32)
returns jsonb
language plpgsql
as $func$
declare
  v_manifest jsonb := coalesce(p_manifest, '{}'::jsonb);
  v_files jsonb := coalesce(v_manifest -> 'files', '[]'::jsonb);
  v_file jsonb;
  v_path text;
  v_storage_path text;
  v_sha256 text;
  v_size bigint;
begin
  if jsonb_typeof(v_files) <> 'array' then
    raise exception 'Manifest files must be an array.';
  end if;

  if jsonb_array_length(v_files) > p_max_file_count then
    raise exception 'Manifest contains too many files. Limit: %', p_max_file_count;
  end if;

  for v_file in
    select value
    from jsonb_array_elements(v_files)
  loop
    v_path := coalesce(v_file ->> 'path', '');
    v_storage_path := coalesce(v_file ->> 'storagePath', '');
    v_sha256 := lower(coalesce(v_file ->> 'sha256', ''));
    v_size := coalesce((v_file ->> 'sizeBytes')::bigint, 0);

    if v_path = '' then
      raise exception 'Manifest file is missing path.';
    end if;

    if v_storage_path = '' then
      raise exception 'Manifest file is missing storagePath.';
    end if;

    if lower(v_path) !~ '^bepinex/plugins/.+\.dll$' then
      raise exception 'Manifest file path % is not allowed.', v_path;
    end if;

    if lower(v_storage_path) !~ '^[a-z0-9_-]+/[a-z0-9/_\.-]+\.dll$' then
      raise exception 'Manifest storagePath % is not allowed.', v_storage_path;
    end if;

    if v_sha256 !~ '^[a-f0-9]{64}$' then
      raise exception 'Manifest file % is missing a valid sha256.', v_path;
    end if;

    if v_size <= 0 or v_size > 26214400 then
      raise exception 'Manifest file % has an invalid size.', v_path;
    end if;
  end loop;

  return jsonb_build_object(
    'id', coalesce(v_manifest ->> 'id', ''),
    'name', left(coalesce(v_manifest ->> 'name', ''), 120),
    'version', left(coalesce(v_manifest ->> 'version', '1.0.0'), 32),
    'gameVersion', left(coalesce(v_manifest ->> 'gameVersion', ''), 32),
    'loader', 'BepInEx',
    'dependencies', coalesce(v_manifest -> 'dependencies', '[]'::jsonb),
    'conflicts', coalesce(v_manifest -> 'conflicts', '[]'::jsonb),
    'files', v_files
  );
end;
$func$;

create or replace function public.create_mod_room(
  p_room_code text,
  p_room_name text,
  p_password text
)
returns table (
  room_code text,
  room_name text,
  manifest jsonb,
  manifest_version integer,
  updated_at timestamptz,
  download_enabled boolean
)
language plpgsql
security definer
set search_path = public
as $func$
declare
  v_room_code text := public.normalize_room_code(p_room_code);
  v_room_name text := trim(coalesce(p_room_name, ''));
begin
  if v_room_code = '' then
    raise exception 'Room code is required.';
  end if;

  if v_room_code !~ '^[A-Z0-9_-]{4,12}$' then
    raise exception 'Room code must be 4 to 12 letters, numbers, underscores, or hyphens.';
  end if;

  if v_room_name = '' then
    raise exception 'Room name is required.';
  end if;

  if p_password is null or length(trim(p_password)) < 8 then
    raise exception 'Password must be at least 8 characters.';
  end if;

  insert into public.mod_rooms (room_code, room_name, password_hash)
  values (
    v_room_code,
    left(v_room_name, 120),
    extensions.crypt(p_password, extensions.gen_salt('bf'))
  );

  return query
  select r.room_code, r.room_name, r.manifest, r.manifest_version, r.updated_at, r.download_enabled
  from public.mod_rooms r
  where r.room_code = v_room_code;
end;
$func$;

create or replace function public.login_mod_room(
  p_room_code text,
  p_password text
)
returns table (
  room_code text,
  room_name text,
  manifest jsonb,
  manifest_version integer,
  updated_at timestamptz,
  download_enabled boolean
)
language plpgsql
security definer
set search_path = public
as $func$
declare
  v_room_code text := public.normalize_room_code(p_room_code);
begin
  return query
  select r.room_code, r.room_name, r.manifest, r.manifest_version, r.updated_at, r.download_enabled
  from public.mod_rooms r
  where r.room_code = v_room_code
    and r.password_hash = extensions.crypt(p_password, r.password_hash);

  if not found then
    raise exception 'Invalid room code or password.';
  end if;
end;
$func$;

create or replace function public.get_room_manifest(
  p_room_code text
)
returns table (
  room_code text,
  room_name text,
  manifest jsonb,
  manifest_version integer,
  updated_at timestamptz,
  download_enabled boolean
)
language sql
security definer
set search_path = public
as $func$
  select
    r.room_code,
    r.room_name,
    r.manifest,
    r.manifest_version,
    r.updated_at,
    r.download_enabled
  from public.mod_rooms r
  where r.room_code = public.normalize_room_code(p_room_code);
$func$;

create or replace function public.admin_save_mod_room_manifest(
  p_room_code text,
  p_room_name text,
  p_manifest jsonb,
  p_download_enabled boolean default true
)
returns table (
  room_code text,
  room_name text,
  manifest jsonb,
  manifest_version integer,
  updated_at timestamptz,
  download_enabled boolean
)
language plpgsql
security definer
set search_path = public
as $func$
declare
  v_room_code text := public.normalize_room_code(p_room_code);
  v_room_name text := trim(coalesce(p_room_name, ''));
  v_existing public.mod_rooms%rowtype;
  v_manifest jsonb;
begin
  select *
  into v_existing
  from public.mod_rooms r
  where r.room_code = v_room_code;

  if not found then
    raise exception 'Room not found.';
  end if;

  v_manifest := public.validate_manifest_shape(p_manifest, coalesce(v_existing.max_file_count, 32));

  update public.mod_rooms r
  set room_name = case when v_room_name = '' then r.room_name else left(v_room_name, 120) end,
      manifest = v_manifest,
      manifest_version = coalesce(r.manifest_version, 1) + 1,
      download_enabled = coalesce(p_download_enabled, true)
  where r.id = v_existing.id;

  return query
  select r.room_code, r.room_name, r.manifest, r.manifest_version, r.updated_at, r.download_enabled
  from public.mod_rooms r
  where r.id = v_existing.id;
end;
$func$;

revoke all on function public.create_mod_room(text, text, text) from public;
revoke all on function public.login_mod_room(text, text) from public;
revoke all on function public.get_room_manifest(text) from public;
revoke all on function public.admin_save_mod_room_manifest(text, text, jsonb, boolean) from public;

grant execute on function public.create_mod_room(text, text, text) to service_role;
grant execute on function public.login_mod_room(text, text) to service_role;
grant execute on function public.get_room_manifest(text) to service_role;
grant execute on function public.admin_save_mod_room_manifest(text, text, jsonb, boolean) to service_role;

notify pgrst, 'reload schema';
