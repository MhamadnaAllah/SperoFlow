import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.join(__dirname, "..", "..");
const frontendDir = path.join(rootDir, "frontend");

const mockPort = Number(process.env.E2E_MOCK_API_PORT || 18080);
const webPort = Number(process.env.E2E_WEB_PORT || 13000);

console.log(`[E2E Runner] Starting Mock API on :${mockPort} and Next.js Start on :${webPort}...`);

const mockProc = spawn("node", [path.join(__dirname, "server.mjs")], {
  stdio: "inherit",
  env: {
    ...process.env,
    PORT: String(mockPort),
    E2E_MOCK_API_PORT: String(mockPort),
  },
});

const nextBin = path.join(frontendDir, "node_modules", "next", "dist", "bin", "next");

const nextProc = spawn("node", [nextBin, "start", "-p", String(webPort), "-H", "127.0.0.1"], {
  cwd: frontendDir,
  stdio: "inherit",
  env: {
    ...process.env,
    API_PROXY_TARGET: `http://127.0.0.1:${mockPort}`,
    API_INTERNAL_BASE_URL: `http://127.0.0.1:${mockPort}`,
    INTERNAL_API_BASE_URL: `http://127.0.0.1:${mockPort}`,
    NEXT_TELEMETRY_DISABLED: "1",
  },
});

function cleanup() {
  console.log("[E2E Runner] Cleaning up processes...");
  try { mockProc.kill(); } catch {}
  try { nextProc.kill(); } catch {}
}

process.on("SIGINT", () => { cleanup(); process.exit(0); });
process.on("SIGTERM", () => { cleanup(); process.exit(0); });
