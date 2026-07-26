import { cookies } from "next/headers";

function internalApiBaseUrl() {
  return process.env.API_INTERNAL_BASE_URL || process.env.INTERNAL_API_BASE_URL || "http://api:8080";
}

export async function getServerCurrentUser() {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map(({ name, value }) => `${name}=${value}`)
    .join("; ");

  if (!cookieHeader) return null;

  try {
    const response = await fetch(`${internalApiBaseUrl()}/api/v1/auth/me`, {
      headers: { cookie: cookieHeader },
      cache: "no-store",
    });
    return response.ok ? response.json() : null;
  } catch {
    return null;
  }
}
