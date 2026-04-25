import {
  createAdminClient,
  createCorsPreflightResponse,
  createJsonResponse,
  isRoomSessionError,
  requireRoomSessionToken,
  sanitizeFileName,
  sha256Hex
} from "../_shared/shared.ts";

const maxBytes = 25 * 1024 * 1024;

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return createCorsPreflightResponse(request);
  }

  try {
    const session = await requireRoomSessionToken(request);
    const formData = await request.formData();
    const file = formData.get("file");

    if (!(file instanceof File)) {
      return createJsonResponse(request, 400, { error: "Attach one DLL file using the file field." });
    }

    const safeName = sanitizeFileName(file.name);
    if (!safeName.toLowerCase().endsWith(".dll")) {
      return createJsonResponse(request, 400, { error: "Only DLL uploads are allowed." });
    }

    const bytes = new Uint8Array(await file.arrayBuffer());
    if (bytes.length === 0 || bytes.length > maxBytes) {
      return createJsonResponse(request, 400, { error: "DLL size must be between 1 byte and 25 MB." });
    }

    const objectPath = `${session.roomCode}/${crypto.randomUUID()}-${safeName}`;
    const sha256 = await sha256Hex(bytes);
    const admin = createAdminClient();
    const bucket = Deno.env.get("MOD_STORAGE_BUCKET") ?? "victor-mods";

    const { error } = await admin.storage.from(bucket).upload(objectPath, bytes, {
      upsert: false,
      contentType: "application/octet-stream",
      cacheControl: "3600"
    });

    if (error) {
      return createJsonResponse(request, 400, { error: error.message });
    }

    return createJsonResponse(request, 200, {
      file: {
        fileName: safeName,
        storagePath: objectPath,
        path: `BepInEx/plugins/${safeName}`,
        sha256,
        sizeBytes: bytes.length
      }
    });
  } catch (error) {
    return createJsonResponse(
      request,
      isRoomSessionError(error) ? 401 : 500,
      { error: error instanceof Error ? error.message : "Unexpected error." });
  }
});
