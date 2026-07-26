import { NextResponse } from "next/server";

const SESSION_COOKIE = "__Host-speroflow-knowledge";

export function middleware(request) {
  if (request.cookies.has(SESSION_COOKIE)) {
    return NextResponse.next();
  }

  const login = new URL("/auth/login", request.url);
  login.searchParams.set("returnUrl", request.nextUrl.pathname + request.nextUrl.search);
  return NextResponse.redirect(login);
}

export const config = {
  matcher: ["/((?!_next/|favicon.ico).*)"],
};