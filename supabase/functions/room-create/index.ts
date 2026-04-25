import {
  createAdminClient,
  createCorsPreflightResponse,
  createJsonResponse,
  createRoomSessionToken,
  sanitizeRoomCode
} from "../_shared/shared.ts";

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return createCorsPreflightResponse(request);
  }

  try {
    const body = await request.json();
    const roomCode = sanitizeRoomCode(body?.roomCode ?? "");
    const roomName = String(body?.roomName ?? "").trim();
    const password = String(body?.password ?? "");
    const admin = createAdminClient();

    const { data, error } = await admin.rpc("create_mod_room", {
      p_room_code: roomCode,
      p_room_name: roomName,
      p_password: password
    });

    if (error) {
      return createJsonResponse(request, 400, { error: error.message });
    }

    const room = Array.isArray(data) ? data[0] : data;
    const sessionToken = await createRoomSessionToken(roomCode);
    return createJsonResponse(request, 200, { room, sessionToken });
  } catch (error) {
    return createJsonResponse(request, 500, { error: error instanceof Error ? error.message : "Unexpected error." });
  }
});
