/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  /**
   * When API_PROXY_TARGET is set (local next dev / Playwright mock), browser
   * same-origin /api/* calls are rewritten to the API. Production Compose uses
   * Caddy for /api and leaves this unset.
   */
  async rewrites() {
    const target = (process.env.API_PROXY_TARGET || "").replace(/\/$/, "");
    if (!target) {
      return [];
    }
    return [
      {
        source: "/api/:path*",
        destination: `${target}/api/:path*`,
      },
      {
        source: "/connect/:path*",
        destination: `${target}/connect/:path*`,
      },
      {
        source: "/.well-known/:path*",
        destination: `${target}/.well-known/:path*`,
      },
    ];
  },
};

export default nextConfig;
