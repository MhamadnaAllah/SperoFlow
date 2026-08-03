FROM node:22.15-alpine AS dependencies
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
# The lock file is generated on Windows and omits Linux-only optional deps
# (sharp/@img/*, @emnapi/*) that Next.js image optimization needs on Alpine.
# npm install resolves the correct platform binaries for this build OS.
RUN npm install --no-audit --no-fund

FROM node:22.15-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=dependencies /app/node_modules ./node_modules
COPY frontend/ ./
RUN npm run build

FROM build AS production-dependencies
RUN npm prune --omit=dev

FROM node:22.15-alpine AS runtime
RUN addgroup -S -g 10001 app && adduser -S -u 10001 -G app app
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
COPY --from=build --chown=app:app /app/package.json ./package.json
COPY --from=build --chown=app:app /app/package-lock.json ./package-lock.json
COPY --from=production-dependencies --chown=app:app /app/node_modules ./node_modules
COPY --from=build --chown=app:app /app/.next ./.next
COPY --from=build --chown=app:app /app/public ./public
USER app
EXPOSE 3000
CMD ["npm", "run", "start"]