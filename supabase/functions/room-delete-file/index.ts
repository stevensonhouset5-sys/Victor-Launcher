import {
  createAdminClient,
  createCorsPreflightResponse,
  createJsonResponse,
  isRoomSessionError,
  requireRoomSessionToken
} from "../_shared/shared.ts";

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return createCorsPreflightResponse(request);
  }

  try {
    const session = await requireRoomSessionToken(request);
    const body = await request.json();
    const storagePath = String(body?.storagePath ?? "");
    if (!storagePath) {
      return createJsonResponse(request, 400, { error: "storagePath is required." });
    }

    if (!storagePath.startsWith(`${session.roomCode}/`)) {
      return createJsonResponse(request, 403, { error: "That file is outside this room." });
    }

    const admin = createAdminClient();
    const bucket = Deno.env.get("MOD_STORAGE_BUCKET") ?? "victor-mods";
    const { error } = await admin.storage.from(bucket).remove([storagePath]);
    if (error) {
      return createJsonResponse(request, 400, { error: error.message });
    }

    return createJsonResponse(request, 200, { deleted: true, storagePath });
  } catch (error) {
    return createJsonResponse(
      request,
      isRoomSessionError(error) ? 401 : 500,
      { error: error instanceof Error ? error.message : "Unexpected error." });
  }
});
