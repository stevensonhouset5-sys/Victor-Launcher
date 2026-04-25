## Victor Launcher Secure Rollout

Run these in order:

1. Execute [room_schema.sql](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/room_schema.sql) in the Supabase SQL Editor.
2. Execute [storage_policies.sql](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/storage_policies.sql) in the Supabase SQL Editor.
3. Create or reuse a private storage bucket named `victor-mods`.
4. Deploy these Edge Functions:
   - `room-create`
   - `room-login`
   - `room-upload`
   - `room-save`
   - `room-delete-file`
   - `room-manifest`

### Required Function Secrets

Set these in Supabase Edge Function secrets:

- `SUPABASE_URL`
- `SUPABASE_SERVICE_ROLE_KEY`
- `ROOM_SESSION_SECRET`
- `MOD_STORAGE_BUCKET`
- `MANIFEST_SIGNING_PRIVATE_KEY_PEM`
- `MANIFEST_SIGNING_KEY_ID`
- `MANIFEST_URL_TTL_SECONDS`

Recommended values:

- `MOD_STORAGE_BUCKET=victor-mods`
- `MANIFEST_SIGNING_KEY_ID=victor-prod-1`
- `MANIFEST_URL_TTL_SECONDS=900`

### Signing Key Pair

The current plugin build ships with this development public key baked in:

```pem
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAzR9LkNcG6pfwzvgKsImv
oXwme875XnIRsZxdJp0L5X92UMY1dn/ylHfwr4s/ddePbwvXsUGLupHYGqFDUE4P
XkpBDrERfeBi+A3W1foZIcyiqsAyTm6ni1JIoGj4dVolqkSW4E5oA5FQ8+YaRKcH
yid6X6RHny4ivFXXrQr3SIU8JYbdFtmt3AC3AJ0w4+qCWWv3COc3IspYQSbm/x5f
S7YIv4cOhzuRQ/n1lxehRENJuakW/xWc2LciH+0gW4me95byj7zsA+BUEvGBWClt
mvxiKvpARyj5DmLbBaaTm66Z22MOa3h0XBCJARXln++2VK6oNImxrOuFfNC3Ilmc
uwIDAQAB
-----END PUBLIC KEY-----
```

If you want the current DLL to work immediately, the private key in your function secret must match that public key.

For a real public launch, generate your own key pair and update:

1. `MANIFEST_SIGNING_PRIVATE_KEY_PEM` in Supabase secrets
2. `ManifestSigningPublicKeyPem` in the Victor Launcher plugin config

### Example Deploy Commands

```bash
supabase functions deploy room-create
supabase functions deploy room-login
supabase functions deploy room-upload
supabase functions deploy room-save
supabase functions deploy room-delete-file
supabase functions deploy room-manifest
```
