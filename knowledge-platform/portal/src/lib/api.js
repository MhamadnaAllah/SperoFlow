const OWNER_API = "/api/v1/knowledge";
const ADMIN_API = "/api/v1/admin/knowledge";
const MAX_FILE_SIZE = 100 * 1024 * 1024;

let csrfToken;

export class PortalApiError extends Error {
  constructor(message, status = 0) {
    super(message);
    this.name = "PortalApiError";
    this.status = status;
  }
}

async function readPayload(response) {
  if (!(response.headers.get("content-type") || "").includes("application/json")) {
    return null;
  }

  return response.json().catch(() => null);
}

async function csrf() {
  if (csrfToken) {
    return csrfToken;
  }

  const response = await fetch("/auth/csrf", { credentials: "same-origin" });
  const payload = await readPayload(response);
  if (!response.ok || !payload?.token) {
    throw new PortalApiError(payload?.title || "Unable to prepare a protected request.", response.status);
  }

  csrfToken = payload.token;
  return csrfToken;
}

async function request(path, options = {}) {
  const method = options.method || "GET";
  const headers = new Headers(options.headers || {});
  if (!["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase())) {
    headers.set("X-CSRF-TOKEN", await csrf());
  }

  if (options.body !== undefined && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, {
    ...options,
    headers,
    body: options.body === undefined || typeof options.body === "string" ? options.body : JSON.stringify(options.body),
    credentials: "same-origin",
  });
  const payload = await readPayload(response);
  if (!response.ok) {
    const errors = payload?.errors ? Object.values(payload.errors).flat().join(" ") : "";
    throw new PortalApiError(errors || payload?.detail || payload?.title || "Request failed (" + response.status + ").", response.status);
  }

  return payload;
}

function fileExtension(file) {
  const position = file.name.lastIndexOf(".");
  return position < 0 ? "" : file.name.slice(position).toLowerCase();
}

function contentTypeFor(file) {
  const byExtension = {
    ".csv": "text/csv",
    ".json": "application/json",
    ".md": "text/markdown",
    ".txt": "text/plain",
    ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ".pdf": "application/pdf",
  };
  return byExtension[fileExtension(file)] || file.type.split(";", 1)[0] || "application/octet-stream";
}

export function validateSourceFile(file) {
  if (!file) return "Choose a source file.";
  if (![".csv", ".json", ".md", ".txt", ".docx", ".pdf"].includes(fileExtension(file))) {
    return "Use CSV, JSON, Markdown, TXT, DOCX, or PDF.";
  }
  if (file.size < 1 || file.size > MAX_FILE_SIZE) {
    return "Sources must be between 1 byte and 100 MB.";
  }
  return null;
}

async function sha256(file) {
  if (!globalThis.crypto?.subtle) {
    throw new PortalApiError("This browser cannot verify the source checksum.");
  }
  const digest = await globalThis.crypto.subtle.digest("SHA-256", await file.arrayBuffer());
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}

export const portalApi = {
  session: () => request("/auth/me"),
  listOwned: () => request(OWNER_API + "/datasets"),
  listAdmin: () => request(ADMIN_API + "/datasets"),
  createDataset: (input) => request(OWNER_API + "/datasets", { method: "POST", body: input }),
  updateDataset: (id, input) => request(OWNER_API + "/datasets/" + id, { method: "PUT", body: input }),
  listSources: (id) => request(OWNER_API + "/datasets/" + id + "/sources"),
  listJobs: (id) => request(OWNER_API + "/datasets/" + id + "/jobs"),
  submitForReview: (id, concurrencyToken) => request(OWNER_API + "/datasets/" + id + "/submit-review", { method: "POST", body: { concurrencyToken } }),
  returnToPrivate: (id, concurrencyToken) => request(OWNER_API + "/datasets/" + id + "/return-to-private", { method: "POST", body: { concurrencyToken } }),
  retryJob: (id) => request(OWNER_API + "/jobs/" + id + "/retry", { method: "POST", body: {} }),
  listReleases: (id) => request(ADMIN_API + "/datasets/" + id + "/releases"),
  publish: (id, releaseId, concurrencyToken) => request(ADMIN_API + "/datasets/" + id + "/publish", { method: "POST", body: { releaseId, concurrencyToken } }),
  archive: (id, concurrencyToken) => request(ADMIN_API + "/datasets/" + id + "/archive", { method: "POST", body: { concurrencyToken } }),
  restore: (id, concurrencyToken) => request(ADMIN_API + "/datasets/" + id + "/restore", { method: "POST", body: { concurrencyToken } }),
  assignOwner: (id, ownerSubject, concurrencyToken) => request(ADMIN_API + "/datasets/" + id + "/owner", { method: "POST", body: { ownerSubject, concurrencyToken } }),

  async uploadSource(datasetId, file) {
    const error = validateSourceFile(file);
    if (error) throw new PortalApiError(error);

    const upload = await request(OWNER_API + "/datasets/" + datasetId + "/uploads", {
      method: "POST",
      body: {
        fileName: file.name,
        contentType: contentTypeFor(file),
        sizeBytes: file.size,
        sha256: await sha256(file),
      },
    });
    const transfer = await fetch(upload.uploadUrl, {
      method: "PUT",
      headers: upload.requiredHeaders || {},
      body: file,
      credentials: "omit",
    });
    if (!transfer.ok) {
      throw new PortalApiError("Object storage rejected the source (" + transfer.status + ").", transfer.status);
    }

    return request(OWNER_API + "/datasets/" + datasetId + "/sources/" + upload.source.id + "/finalize", {
      method: "POST",
      body: {},
    });
  },

  logout: () => request("/auth/logout", { method: "POST", body: {} }),
};
