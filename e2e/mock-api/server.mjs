/**
 * Minimal same-origin API stand-in for Playwright mocked E2E.
 * Implements CSRF cookie pair, login session cookie, me, roles, proposals.
 */
import http from "node:http";
import { randomUUID } from "node:crypto";
import { parse as parseUrl } from "node:url";

const port = Number(process.env.PORT || process.env.E2E_MOCK_API_PORT || 18080);

const state = {
  csrf: null,
  session: null,
  proposals: [
    {
      id: "11111111-1111-1111-1111-111111111111",
      kind: "createTask",
      state: "pending",
      source: "balance",
      title: "Schedule a 30-minute focus block",
      description: "Protect deep work for your highest-priority project this week.",
      payload: {
        title: "Focus block",
        description: "Deep work",
        lifeArea: "work",
        quadrant: "Q2",
        estimatedMinutes: 30,
        roleId: null,
      },
      appliedEntityId: null,
      concurrencyToken: "proposal-token-1",
      createdAt: new Date().toISOString(),
      resolvedAt: null,
    },
  ],
  roles: [
    {
      id: "22222222-2222-2222-2222-222222222222",
      name: "Mental",
      category: "internal",
      defaultLifeArea: "personal",
      color: "#0053dc",
      icon: "psychology",
      sortOrder: 0,
      isArchived: false,
      isSystemRole: true,
      concurrencyToken: "role-token-1",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
  ],
};

function send(res, status, body, extraHeaders = {}) {
  const payload = body === undefined || body === null ? "" : typeof body === "string" ? body : JSON.stringify(body);
  const headers = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Credentials": "true",
    "Access-Control-Allow-Headers": "content-type, x-csrf-token, cookie",
    "Access-Control-Allow-Methods": "GET,POST,PUT,DELETE,OPTIONS",
    ...extraHeaders,
  };
  if (payload && typeof body !== "string") {
    headers["Content-Type"] = "application/json; charset=utf-8";
  }
  res.writeHead(status, headers);
  res.end(payload);
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8");
      if (!raw) return resolve(null);
      try {
        resolve(JSON.parse(raw));
      } catch (err) {
        reject(err);
      }
    });
    req.on("error", reject);
  });
}

function parseCookies(header) {
  const out = {};
  if (!header) return out;
  for (const part of header.split(";")) {
    const [k, ...rest] = part.trim().split("=");
    if (!k) continue;
    out[k] = decodeURIComponent(rest.join("=") || "");
  }
  return out;
}

function requireCsrf(req, res) {
  const cookies = parseCookies(req.headers.cookie);
  const header = req.headers["x-csrf-token"];
  const cookieToken = cookies["__Host-speroflow-xsrf"] || cookies["speroflow-xsrf"];
  if (!header || !cookieToken || header !== cookieToken || header !== state.csrf) {
    send(res, 400, {
      title: "Invalid CSRF token.",
      type: "https://speroflow.dev/problems/invalid-csrf-token",
      status: 400,
    });
    return false;
  }
  return true;
}

function requireAuth(req, res) {
  const cookies = parseCookies(req.headers.cookie);
  if (!state.session || cookies["speroflow-e2e-session"] !== state.session) {
    send(res, 401, { title: "Unauthorized.", status: 401 });
    return false;
  }
  return true;
}

const server = http.createServer(async (req, res) => {
  const method = (req.method || "GET").toUpperCase();
  const { pathname, query } = parseUrl(req.url || "/", true);

  if (method === "OPTIONS") {
    return send(res, 204);
  }

  if (pathname === "/health/live") {
    return send(res, 200, { status: "healthy", service: "speroflow-e2e-mock-api" });
  }

  try {
    if (pathname === "/api/v1/auth/csrf" && method === "GET") {
      state.csrf = `csrf-${randomUUID()}`;
      // Browsers on http://127.0.0.1 cannot set __Host- cookies without HTTPS Secure.
      // Use a plain cookie name for the mock so Playwright can complete the flow.
      const setCookie = `speroflow-xsrf=${encodeURIComponent(state.csrf)}; Path=/; HttpOnly; SameSite=Strict`;
      return send(res, 200, { token: state.csrf }, { "Set-Cookie": setCookie });
    }

    if (pathname === "/api/v1/auth/login" && method === "POST") {
      if (!requireCsrf(req, res)) return;
      const body = await readBody(req);
      if (!body?.email || !body?.password) {
        return send(res, 400, { title: "Email and password are required.", status: 400 });
      }
      if (body.password === "wrong-password") {
        return send(res, 401, { title: "Invalid email or password.", status: 401 });
      }
      state.session = `sess-${randomUUID()}`;
      state.user = {
        id: "33333333-3333-3333-3333-333333333333",
        email: String(body.email).toLowerCase(),
        displayName: "E2E User",
        emailConfirmed: true,
        roles: ["User"],
      };
      return send(res, 204, null, {
        "Set-Cookie": `speroflow-e2e-session=${encodeURIComponent(state.session)}; Path=/; HttpOnly; SameSite=Lax`,
      });
    }

    if (pathname === "/api/v1/auth/logout" && method === "POST") {
      if (!requireCsrf(req, res)) return;
      state.session = null;
      return send(res, 204, null, {
        "Set-Cookie": "speroflow-e2e-session=; Path=/; Max-Age=0",
      });
    }

    if (pathname === "/api/v1/auth/me" && method === "GET") {
      if (!requireAuth(req, res)) return;
      return send(res, 200, state.user);
    }

    if (pathname === "/api/v1/roles/bootstrap" && method === "POST") {
      if (!requireCsrf(req, res)) return;
      if (!requireAuth(req, res)) return;
      return send(res, 200, state.roles);
    }

    if (pathname === "/api/v1/roles" && method === "GET") {
      if (!requireAuth(req, res)) return;
      return send(res, 200, state.roles);
    }

    if (pathname === "/api/v1/ai/roles/pending" && method === "GET") {
      if (!requireAuth(req, res)) return;
      return send(res, 200, []);
    }

    if (pathname === "/api/v1/ai/proposals" && method === "GET") {
      if (!requireAuth(req, res)) return;
      let list = state.proposals;
      if (query?.state) {
        list = list.filter((p) => p.state === query.state);
      }
      return send(res, 200, list);
    }

    const approveMatch = pathname.match(/^\/api\/v1\/ai\/proposals\/([^/]+)\/approve$/);
    if (approveMatch && method === "POST") {
      if (!requireCsrf(req, res)) return;
      if (!requireAuth(req, res)) return;
      const body = await readBody(req);
      const id = approveMatch[1];
      const proposal = state.proposals.find((p) => p.id === id);
      if (!proposal) {
        return send(res, 404, { title: "Proposal not found.", status: 404 });
      }
      if (body?.concurrencyToken !== proposal.concurrencyToken) {
        return send(res, 409, { title: "Conflict", detail: "Proposal changed elsewhere.", status: 409 });
      }
      proposal.state = "approved";
      proposal.resolvedAt = new Date().toISOString();
      proposal.concurrencyToken = "proposal-token-approved";
      return send(res, 200, proposal);
    }

    const cancelMatch = pathname.match(/^\/api\/v1\/ai\/proposals\/([^/]+)\/cancel$/);
    if (cancelMatch && method === "POST") {
      if (!requireCsrf(req, res)) return;
      if (!requireAuth(req, res)) return;
      const body = await readBody(req);
      const id = cancelMatch[1];
      const proposal = state.proposals.find((p) => p.id === id);
      if (!proposal) {
        return send(res, 404, { title: "Proposal not found.", status: 404 });
      }
      if (body?.concurrencyToken !== proposal.concurrencyToken) {
        return send(res, 409, { title: "Conflict", detail: "Proposal changed elsewhere.", status: 409 });
      }
      proposal.state = "cancelled";
      proposal.resolvedAt = new Date().toISOString();
      return send(res, 200, proposal);
    }

    // Calendar and other dashboard shells may soft-fail; return empty arrays.
    if (pathname.startsWith("/api/v1/") && method === "GET" && requireAuth(req, res)) {
      return send(res, 200, []);
    }

    return send(res, 404, { title: "Not found", path: pathname, status: 404 });
  } catch (err) {
    console.error(err);
    return send(res, 500, { title: "Mock API error", detail: String(err), status: 500 });
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`SperoFlow E2E mock API on http://127.0.0.1:${port}`);
});
