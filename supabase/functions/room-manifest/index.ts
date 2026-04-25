import {
  canonicalizeEnvelope,
  createAdminClient,
  createCorsPreflightResponse,
  createJsonResponse,
  sanitizeRoomCode,
  signManifestEnvelope
} from "../_shared/shared.ts";

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return createCorsPreflightResponse(request);
  }

  try {
    const url = new URL(request.url);
    const roomCode = sanitizeRoomCode(url.searchParams.get("roomCode") ?? "");
    if (!roomCode) {
      return createJsonResponse(request, 400, { error: "roomCode is required." });
    }

    const admin = createAdminClient();
    const { data, error } = await admin.rpc("get_room_manifest", {
      p_room_code: roomCode
    });

    if (error) {
      return createJsonResponse(request, 404, { error: error.message });
    }

    const room = Array.isArray(data) ? data[0] : data;
    if (!room?.download_enabled) {
      return createJsonResponse(request, 404, { error: "This room is not publishing a mod pack right now." });
    }

    const manifest = room?.manifest;
    const files = Array.isArray(manifest?.files) ? manifest.files : [];
    const bucket = Deno.env.get("MOD_STORAGE_BUCKET") ?? "victor-mods";
    const signedUrlTtlSeconds = Number(Deno.env.get("MANIFEST_URL_TTL_SECONDS") ?? "900");
    const issuedAt = new Date().toISOString();
    const expiresAt = new Date(Date.now() + signedUrlTtlSeconds * 1000).toISOString();

    const mappedFiles = [];
    for (const file of files) {
      const storagePath = String(file?.storagePath ?? "");
      if (!storagePath) {
        continue;
      }

      const { data: signedUrlData, error: signedUrlError } = await admin.storage
        .from(bucket)
        .createSignedUrl(storagePath, signedUrlTtlSeconds);

      if (signedUrlError || !signedUrlData?.signedUrl) {
        return createJsonResponse(request, 500, { error: `Could not sign ${storagePath} for download.` });
      }

      mappedFiles.push({
        path: String(file?.path ?? ""),
        url: signedUrlData.signedUrl,
        sha256: String(file?.sha256 ?? "").toLowerCase(),
        sizeBytes: Number(file?.sizeBytes ?? 0)
      });
    }

    const envelope = {
      roomCode,
      roomName: String(room?.room_name ?? ""),
      manifestVersion: Number(room?.manifest_version ?? 1),
      issuedAtUtc: issuedAt,
      expiresAtUtc: expiresAt,
      manifest: {
        id: String(manifest?.id ?? ""),
        name: String(manifest?.name ?? ""),
        version: String(manifest?.version ?? "1.0.0"),
        gameVersion: String(manifest?.gameVersion ?? ""),
        loader: "BepInEx",
        dependencies: Array.isArray(manifest?.dependencies) ? manifest.dependencies.map(String) : [],
        conflicts: Array.isArray(manifest?.conflicts) ? manifest.conflicts.map(String) : [],
        files: mappedFiles
      }
    };

    const { payload, signature } = await signManifestEnvelope(envelope);

    return createJsonResponse(request, 200, {
      signingKeyId: Deno.env.get("MANIFEST_SIGNING_KEY_ID") ?? "victor-dev-1",
      canonicalPayload: payload,
      signature,
      envelope: JSON.parse(canonicalizeEnvelope(envelope))
    });
  } catch (error) {
    return createJsonResponse(request, 500, { error: error instanceof Error ? error.message : "Unexpected error." });
  }
});
