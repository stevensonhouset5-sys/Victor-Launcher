insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'victor-mods',
  'victor-mods',
  false,
  26214400,
  array['application/octet-stream', 'application/x-msdownload']
)
on conflict (id) do update
set public = excluded.public,
    file_size_limit = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

drop policy if exists "public read victor mods" on storage.objects;
drop policy if exists "public upload victor mods" on storage.objects;
drop policy if exists "public update victor mods" on storage.objects;
drop policy if exists "public delete victor mods" on storage.objects;

drop policy if exists "deny direct storage reads" on storage.objects;
create policy "deny direct storage reads"
on storage.objects
for select
to public
using (bucket_id = 'victor-mods' and false);

drop policy if exists "deny direct storage writes" on storage.objects;
create policy "deny direct storage writes"
on storage.objects
for all
to public
using (bucket_id = 'victor-mods' and false)
with check (bucket_id = 'victor-mods' and false);
