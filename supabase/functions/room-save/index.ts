import {
  createAdminClient,
  createCorsPreflightResponse,
  createJsonResponse,
  isRoomSessionError,
  requireRoomSessionToken
} from "../_shared/shared.ts";

function normalizeManifest(body: any) {
  const manifest = body?.manifest ?? {};
  return {
    id: String(manifest.id ?? ""),
    name: String(manifest.name ?? ""),
    version: String(manifest.version ?? "1.0.0"),
    gameVersion: String(manifest.gameVersion ?? ""),
    loader: "BepInEx",
    dependencies: Array.isArray(manifest.dependencies) ? manifest.dependencies.map(String) : [],
    conflicts: Array.isArray(manifest.conflicts) ? manifest.conflicts.map(String) : [],
    files: Array.isArray(manifest.files)
      ? manifest.files.map((file: any) => ({
          path: String(file.path ?? ""),
          storagePath: String(file.storagePath ?? ""),
          sha256: String(file.sha256 ?? "").toLowerCase(),
          sizeBytes: Number(file.sizeBytes ?? 0)
        }))
      : []
  };
}

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return createCorsPreflightResponse(request);
  }

  try {
    const session = await requireRoomSessionToken(request);
    const body = await request.json();
    const roomName = String(body?.roomName ?? "").trim();
    const downloadEnabled = body?.downloadEnabled !== false;
    const manifest = normalizeManifest(body);
    const admin = createAdminClient();

    const { data, error } = await admin.rpc("admin_save_mod_room_manifest", {
      p_room_code: session.roomCode,
      p_room_name: roomName,
      p_manifest: manifest,
      p_download_enabled: downloadEnabled
    });

    if (error) {
      return createJsonResponse(request, 400, { error: error.message });
    }

    const room = Array.isArray(data) ? data[0] : data;
    return createJsonResponse(request, 200, { room });
  } catch (error) {
    return createJsonResponse(
      request,
      isRoomSessionError(error) ? 401 : 500,
      { error: error instanceof Error ? error.message : "Unexpected error." });
  }
});
