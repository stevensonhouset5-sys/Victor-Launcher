# Victor Launcher Room Admin Site

This folder contains a static admin page for managing room-based mod packs in Supabase.

## What it does

- create a room with `room code + password`
- log back into a room later with the same code and password
- upload DLLs to Supabase Storage
- build a manifest automatically
- save that manifest so Victor Launcher can fetch it by room code

## Files

- `app.css`: visual styling for the room manager
- `app.js`: room-manager client logic

This public repo intentionally does not need to include a live, hardwired `index.html` that points at your personal Supabase project. Keep the deployed HTML private or generate it per environment before publishing.

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
4. Build or deploy your room-manager HTML with the correct project-specific values for that environment

## Current tradeoff

This project now expects the safer flow where uploads and room actions go through password-checked Edge Functions instead of a raw browser-to-bucket prototype.
