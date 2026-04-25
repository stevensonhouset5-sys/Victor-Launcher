import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const encoder = new TextEncoder();
const decoder = new TextDecoder();

function buildCorsHeaders(request: Request) {
  const requestedOrigin = request.headers.get("Origin");
  const requestedHeaders = request.headers.get("Access-Control-Request-Headers");
  const requestedMethod = request.headers.get("Access-Control-Request-Method");

  return {
    "Access-Control-Allow-Origin": requestedOrigin && requestedOrigin !== "null" ? requestedOrigin : "*",
    "Access-Control-Allow-Headers": requestedHeaders || "authorization, x-client-info, apikey, content-type, x-room-session",
    "Access-Control-Allow-Methods": requestedMethod || "GET, POST, OPTIONS",
    "Access-Control-Max-Age": "86400",
    "Vary": "Origin, Access-Control-Request-Headers, Access-Control-Request-Method"
  };
}

export function createCorsPreflightResponse(request: Request) {
  return new Response("ok", {
    headers: buildCorsHeaders(request)
  });
}

export function createAdminClient() {
  const url = Deno.env.get("SUPABASE_URL");
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");

  if (!url || !serviceRoleKey) {
    throw new Error("SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY must be configured.");
  }

  return createClient(url, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false }
  });
}

export function createJsonResponse(request: Request, status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      ...buildCorsHeaders(request),
      "Content-Type": "application/json"
    }
  });
}

export function sanitizeRoomCode(roomCode: string) {
  return (roomCode ?? "").trim().toUpperCase();
}

export function sanitizeFileName(fileName: string) {
  return (fileName ?? "").replace(/[^a-zA-Z0-9._-]/g, "_");
}

function base64UrlEncode(input: Uint8Array) {
  let binary = "";
  for (const value of input) {
    binary += String.fromCharCode(value);
  }

  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function base64UrlDecode(input: string) {
  const normalized = input.replaceAll("-", "+").replaceAll("_", "/");
  const padding = normalized.length % 4 === 0 ? "" : "=".repeat(4 - (normalized.length % 4));
  const binary = atob(normalized + padding);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

async function importHmacKey(secret: string) {
  return crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign", "verify"]
  );
}

export async function createRoomSessionToken(roomCode: string) {
  const secret = Deno.env.get("ROOM_SESSION_SECRET");
  if (!secret) {
    throw new Error("ROOM_SESSION_SECRET must be configured.");
  }

  const payload = {
    roomCode,
    exp: Math.floor(Date.now() / 1000) + 60 * 60 * 12
  };

  const payloadJson = JSON.stringify(payload);
  const payloadEncoded = base64UrlEncode(encoder.encode(payloadJson));
  const key = await importHmacKey(secret);
  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode(payloadEncoded));

  return `${payloadEncoded}.${base64UrlEncode(new Uint8Array(signature))}`;
}

export async function requireRoomSessionToken(request: Request) {
  const secret = Deno.env.get("ROOM_SESSION_SECRET");
  if (!secret) {
    throw new Error("ROOM_SESSION_SECRET must be configured.");
  }

  const customHeaderToken = request.headers.get("x-room-session")?.trim() ?? "";
  const authHeader = request.headers.get("Authorization") ?? "";
  const bearerToken = authHeader.startsWith("Bearer ") ? authHeader.slice("Bearer ".length).trim() : "";
  const token = customHeaderToken || bearerToken;
  if (!token) {
    throw new Error("Missing room session token.");
  }

  const [payloadEncoded, signatureEncoded] = token.split(".");
  if (!payloadEncoded || !signatureEncoded) {
    throw new Error("Invalid room session token.");
  }

  const key = await importHmacKey(secret);
  const isValid = await crypto.subtle.verify(
    "HMAC",
    key,
    base64UrlDecode(signatureEncoded),
    encoder.encode(payloadEncoded)
  );

  if (!isValid) {
    throw new Error("Invalid room session token.");
  }

  const payloadJson = decoder.decode(base64UrlDecode(payloadEncoded));
  const payload = JSON.parse(payloadJson);
  if (!payload?.roomCode || !payload?.exp || payload.exp < Math.floor(Date.now() / 1000)) {
    throw new Error("Room session token expired.");
  }

  return {
    token,
    roomCode: sanitizeRoomCode(payload.roomCode)
  };
}

export function isRoomSessionError(error: unknown) {
  if (!(error instanceof Error)) {
    return false;
  }

  return error.message.includes("room session token") ||
    error.message.includes("Missing room session token") ||
    error.message.includes("Invalid room session token") ||
    error.message.includes("Room session token expired");
}

export async function sha256Hex(bytes: Uint8Array) {
  const hash = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(hash))
    .map((value) => value.toString(16).padStart(2, "0"))
    .join("");
}

function decodePem(pem: string) {
  const base64 = pem
    .replace(/-----BEGIN [^-]+-----/g, "")
    .replace(/-----END [^-]+-----/g, "")
    .replace(/\s+/g, "");

  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

async function importManifestPrivateKey() {
  const pem = Deno.env.get("MANIFEST_SIGNING_PRIVATE_KEY_PEM");
  if (!pem) {
    throw new Error("MANIFEST_SIGNING_PRIVATE_KEY_PEM must be configured.");
  }

  return crypto.subtle.importKey(
    "pkcs8",
    decodePem(pem),
    {
      name: "RSASSA-PKCS1-v1_5",
      hash: "SHA-256"
    },
    false,
    ["sign"]
  );
}

export function canonicalizeEnvelope(envelope: {
  roomCode: string;
  roomName: string;
  manifestVersion: number;
  issuedAtUtc: string;
  expiresAtUtc: string;
  manifest: {
    id: string;
    name: string;
    version: string;
    gameVersion: string;
    loader: string;
    dependencies: string[];
    conflicts: string[];
    files: Array<{
      path: string;
      url: string;
      sha256: string;
      sizeBytes: number;
    }>;
  };
}) {
  return JSON.stringify({
    roomCode: envelope.roomCode,
    roomName: envelope.roomName,
    manifestVersion: envelope.manifestVersion,
    issuedAtUtc: envelope.issuedAtUtc,
    expiresAtUtc: envelope.expiresAtUtc,
    manifest: {
      id: envelope.manifest.id,
      name: envelope.manifest.name,
      version: envelope.manifest.version,
      gameVersion: envelope.manifest.gameVersion,
      loader: envelope.manifest.loader,
      dependencies: [...(envelope.manifest.dependencies ?? [])],
      conflicts: [...(envelope.manifest.conflicts ?? [])],
      files: (envelope.manifest.files ?? []).map((file) => ({
        path: file.path,
        url: file.url,
        sha256: file.sha256,
        sizeBytes: file.sizeBytes
      }))
    }
  });
}

export async function signManifestEnvelope(envelope: Parameters<typeof canonicalizeEnvelope>[0]) {
  const key = await importManifestPrivateKey();
  const payload = canonicalizeEnvelope(envelope);
  const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, encoder.encode(payload));
  return {
    payload,
    signature: base64UrlEncode(new Uint8Array(signature))
  };
}
