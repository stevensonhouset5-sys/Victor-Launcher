# Victor Launcher Room Admin Site

This folder contains a static admin page for managing room-based mod packs in Supabase.

## What it does

- create a room with `room code + password`
- log back into a room later with the same code and password
- upload DLLs to Supabase Storage
- build a manifest automatically
- save that manifest so Victor Launcher can fetch it by room code

## Files

- `index.html`: the page markup
- `app.css`: visual styling
- `app.js`: Supabase calls, uploads, and manifest editing logic

## How to host it

Any static host is fine:

- Supabase hosting
- Netlify
- Vercel
- GitHub Pages

If you test locally, use a small static server instead of opening the file directly in Finder so the module import works.

## Supabase setup

1. Run the SQL in [room_schema.sql](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/room_schema.sql)
2. Create a public storage bucket, for example `victor-mods`
3. Host this folder
4. Open the page and paste:
   - your Supabase project URL
   - your anon key
   - your bucket name

## Current tradeoff

This first version uploads directly from the browser to Supabase Storage. That keeps the setup simple, but it also means your bucket policies must allow uploads from the anon client.

That is okay for a private prototype with friends. If you want this to be safer for wider use, the next step is moving uploads behind a password-checked Edge Function or another small backend.
