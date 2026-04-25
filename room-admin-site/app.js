import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const storageKeys = {
  url: "victor.supabase.url",
  anonKey: "victor.supabase.anonKey",
  bucket: "victor.supabase.bucket"
};

const state = {
  client: null,
  roomCode: "",
  roomPassword: "",
  roomName: "",
  manifest: createEmptyManifest()
};

const elements = {
  setupForm: document.querySelector("#setup-form"),
  createRoomForm: document.querySelector("#create-room-form"),
  loginForm: document.querySelector("#login-form"),
  dashboardPanel: document.querySelector("#dashboard-panel"),
  statusBanner: document.querySelector("#status-banner"),
  dashboardTitle: document.querySelector("#dashboard-title"),
  dashboardSubtitle: document.querySelector("#dashboard-subtitle"),
  copyRoomCode: document.querySelector("#copy-room-code"),
  uploadDlls: document.querySelector("#upload-dlls"),
  saveRoom: document.querySelector("#save-room"),
  modList: document.querySelector("#mod-list"),
  roomName: document.querySelector("#pack-name"),
  packVersion: document.querySelector("#pack-version"),
  gameVersion: document.querySelector("#game-version"),
  dllFiles: document.querySelector("#dll-files"),
  supabaseUrl: document.querySelector("#supabase-url"),
  supabaseAnonKey: document.querySelector("#supabase-anon-key"),
  supabaseBucket: document.querySelector("#supabase-bucket"),
  createRoomName: document.querySelector("#create-room-name"),
  createRoomCode: document.querySelector("#create-room-code"),
  createRoomPassword: document.querySelector("#create-room-password"),
  loginRoomCode: document.querySelector("#login-room-code"),
  loginRoomPassword: document.querySelector("#login-room-password")
};

bootstrap();

function bootstrap() {
  loadSetupFromStorage();
  bindEvents();
  rebuildClient();
  renderManifest();
}

function bindEvents() {
  elements.setupForm.addEventListener("submit", handleSaveSetup);
  elements.createRoomForm.addEventListener("submit", handleCreateRoom);
  elements.loginForm.addEventListener("submit", handleLoginRoom);
  elements.copyRoomCode.addEventListener("click", handleCopyRoomCode);
  elements.uploadDlls.addEventListener("click", handleUploadDlls);
  elements.saveRoom.addEventListener("click", handleSaveRoom);
}

function loadSetupFromStorage() {
  elements.supabaseUrl.value = localStorage.getItem(storageKeys.url) ?? "";
  elements.supabaseAnonKey.value = localStorage.getItem(storageKeys.anonKey) ?? "";
  elements.supabaseBucket.value = localStorage.getItem(storageKeys.bucket) ?? "victor-mods";
}

function rebuildClient() {
  const url = elements.supabaseUrl.value.trim();
  const anonKey = elements.supabaseAnonKey.value.trim();

  if (!url || !anonKey) {
    state.client = null;
    return;
  }

  state.client = createClient(url, anonKey);
}

function handleSaveSetup(event) {
  event.preventDefault();
  localStorage.setItem(storageKeys.url, elements.supabaseUrl.value.trim());
  localStorage.setItem(storageKeys.anonKey, elements.supabaseAnonKey.value.trim());
  localStorage.setItem(storageKeys.bucket, elements.supabaseBucket.value.trim());
  rebuildClient();
  showStatus("Supabase setup saved locally.", "success");
}

async function handleCreateRoom(event) {
  event.preventDefault();
  const client = requireClient();
  if (!client) {
    return;
  }

  const roomCode = normalizeRoomCode(elements.createRoomCode.value);
  const roomName = elements.createRoomName.value.trim();
  const password = elements.createRoomPassword.value;

  const { data, error } = await client.rpc("create_mod_room", {
    p_room_code: roomCode,
    p_room_name: roomName,
    p_password: password
  });

  if (error) {
    showStatus(error.message, "error");
    return;
  }

  hydrateRoomSession(data, roomCode, password);
  elements.createRoomForm.reset();
  showStatus(`Room ${roomCode} created.`, "success");
}

async function handleLoginRoom(event) {
  event.preventDefault();
  const client = requireClient();
  if (!client) {
    return;
  }

  const roomCode = normalizeRoomCode(elements.loginRoomCode.value);
  const password = elements.loginRoomPassword.value;

  const { data, error } = await client.rpc("login_mod_room", {
    p_room_code: roomCode,
    p_password: password
  });

  if (error) {
    showStatus(error.message, "error");
    return;
  }

  hydrateRoomSession(data, roomCode, password);
  elements.loginForm.reset();
  showStatus(`Room ${roomCode} loaded.`, "success");
}

function hydrateRoomSession(data, roomCode, password) {
  const room = Array.isArray(data) ? data[0] : data;
  state.roomCode = roomCode;
  state.roomPassword = password;
  state.roomName = room?.room_name ?? "";
  state.manifest = normalizeManifest(room?.manifest, room?.room_name);
  syncDashboardFields();
  renderDashboard();
}

function syncDashboardFields() {
  elements.roomName.value = state.manifest.name || state.roomName || "";
  elements.packVersion.value = state.manifest.version || "1.0.0";
  elements.gameVersion.value = state.manifest.gameVersion || "";
}

function renderDashboard() {
  elements.dashboardPanel.hidden = false;
  elements.dashboardTitle.textContent = state.roomName || state.roomCode;
  elements.dashboardSubtitle.textContent = `Room code ${state.roomCode} is ready to manage. Share only the room code with players.`;
  renderManifest();
}

function renderManifest() {
  const files = state.manifest.files ?? [];
  if (!files.length) {
    elements.modList.className = "mod-list empty-state";
    elements.modList.textContent = "No DLLs uploaded yet.";
    return;
  }

  elements.modList.className = "mod-list";
  elements.modList.innerHTML = files.map((file, index) => `
    <div class="mod-row">
      <div class="mod-meta">
        <div class="mod-name">${escapeHtml(file.path.split("/").pop() || file.path)}</div>
        <div class="mod-url">${escapeHtml(file.url)}</div>
        <div class="mono">sha256: ${escapeHtml(file.sha256 || "not set")}</div>
      </div>
      <div class="mod-actions">
        <button class="solid-button danger" type="button" data-remove-index="${index}">Remove</button>
      </div>
    </div>
  `).join("");

  elements.modList.querySelectorAll("[data-remove-index]").forEach(button => {
    button.addEventListener("click", () => {
      const index = Number(button.getAttribute("data-remove-index"));
      state.manifest.files.splice(index, 1);
      renderManifest();
    });
  });
}

async function handleUploadDlls() {
  const client = requireClient();
  if (!client || !requireRoomSession()) {
    return;
  }

  const files = Array.from(elements.dllFiles.files ?? []);
  if (!files.length) {
    showStatus("Choose at least one DLL first.", "error");
    return;
  }

  const bucket = elements.supabaseBucket.value.trim();
  if (!bucket) {
    showStatus("Add a storage bucket name first.", "error");
    return;
  }

  for (const file of files) {
    if (!file.name.toLowerCase().endsWith(".dll")) {
      showStatus(`Skipped ${file.name} because only DLL files are allowed.`, "error");
      continue;
    }

    const fileBuffer = await file.arrayBuffer();
    const sha256 = await hashArrayBuffer(fileBuffer);
    const objectPath = `${state.roomCode}/${Date.now()}-${sanitizeFileName(file.name)}`;

    const { error: uploadError } = await client.storage
      .from(bucket)
      .upload(objectPath, file, {
        cacheControl: "3600",
        upsert: true,
        contentType: "application/octet-stream"
      });

    if (uploadError) {
      showStatus(uploadError.message, "error");
      return;
    }

    const { data: publicUrlData } = client.storage.from(bucket).getPublicUrl(objectPath);
    upsertManifestFile({
      path: `BepInEx/plugins/${file.name}`,
      url: publicUrlData.publicUrl,
      sha256
    });
  }

  elements.dllFiles.value = "";
  renderManifest();
  showStatus("DLLs uploaded and added to the room manifest. Save the room to publish the changes.", "success");
}

async function handleSaveRoom() {
  const client = requireClient();
  if (!client || !requireRoomSession()) {
    return;
  }

  state.manifest.name = elements.roomName.value.trim() || state.roomName || state.roomCode;
  state.manifest.version = elements.packVersion.value.trim() || "1.0.0";
  state.manifest.gameVersion = elements.gameVersion.value.trim();

  const { data, error } = await client.rpc("save_mod_room_manifest", {
    p_room_code: state.roomCode,
    p_password: state.roomPassword,
    p_room_name: state.manifest.name,
    p_manifest: state.manifest
  });

  if (error) {
    showStatus(error.message, "error");
    return;
  }

  hydrateRoomSession(data, state.roomCode, state.roomPassword);
  showStatus("Room manifest saved.", "success");
}

async function handleCopyRoomCode() {
  if (!state.roomCode) {
    showStatus("Open a room first.", "error");
    return;
  }

  await navigator.clipboard.writeText(state.roomCode);
  showStatus(`Copied ${state.roomCode}.`, "success");
}

function upsertManifestFile(nextFile) {
  const existingIndex = state.manifest.files.findIndex(file => file.path === nextFile.path);
  if (existingIndex >= 0) {
    state.manifest.files[existingIndex] = nextFile;
    return;
  }

  state.manifest.files.push(nextFile);
}

function createEmptyManifest() {
  return {
    id: "",
    name: "",
    version: "1.0.0",
    gameVersion: "",
    loader: "BepInEx",
    dependencies: [],
    conflicts: [],
    files: []
  };
}

function normalizeManifest(manifest, fallbackName = "") {
  return {
    ...createEmptyManifest(),
    ...manifest,
    name: manifest?.name || fallbackName || "",
    files: Array.isArray(manifest?.files) ? [...manifest.files] : []
  };
}

function normalizeRoomCode(roomCode) {
  return roomCode.trim().toUpperCase();
}

function sanitizeFileName(fileName) {
  return fileName.replace(/[^a-zA-Z0-9._-]/g, "_");
}

async function hashArrayBuffer(buffer) {
  const hashBuffer = await crypto.subtle.digest("SHA-256", buffer);
  return Array.from(new Uint8Array(hashBuffer))
    .map(value => value.toString(16).padStart(2, "0"))
    .join("");
}

function requireClient() {
  if (state.client) {
    return state.client;
  }

  showStatus("Save your Supabase URL and anon key first.", "error");
  return null;
}

function requireRoomSession() {
  if (state.roomCode && state.roomPassword) {
    return true;
  }

  showStatus("Create or open a room first.", "error");
  return false;
}

function showStatus(message, type = "success") {
  elements.statusBanner.textContent = message;
  elements.statusBanner.className = `status-banner ${type}`;
  window.clearTimeout(showStatus.timerId);
  showStatus.timerId = window.setTimeout(() => {
    elements.statusBanner.className = "status-banner hidden";
  }, 4200);
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}
