import { NextResponse } from "next/server";

export function middleware(request) {
  const target = (process.env.API_PROXY_TARGET || "").replace(/\/$/, "");
  const { pathname, search } = request.nextUrl;

  if (target && (pathname.startsWith("/api/") || pathname.startsWith("/connect/") || pathname.startsWith("/.well-known/"))) {
    const destination = new URL(`${pathname}${search}`, target);
    return NextResponse.rewrite(destination);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/api/:path*", "/connect/:path*", "/.well-known/:path*"],
};
