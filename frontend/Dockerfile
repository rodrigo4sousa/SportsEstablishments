# syntax=docker/dockerfile:1

FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# public/ is optional in Next.js and not in the repository yet; the runtime stage copies it.
RUN mkdir -p public
# Rewrites are resolved at build time, so the internal API URL is a build argument. It has no default here:
# docker-compose.yml owns it (as build argument, and as environment for server code at run time).
ARG API_INTERNAL_URL
RUN test -n "$API_INTERNAL_URL" || (echo "Build argument API_INTERNAL_URL is required (docker-compose.yml sets it)." >&2; exit 1)
# HSTS is a build setting too (the headers are written into the build). It stays off unless the site is served over HTTPS:
# pass --build-arg ENABLE_HSTS=true (docker-compose.yml leaves it off).
ARG ENABLE_HSTS=false
ENV API_INTERNAL_URL=$API_INTERNAL_URL \
    ENABLE_HSTS=$ENABLE_HSTS \
    NEXT_TELEMETRY_DISABLED=1
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
# API_INTERNAL_URL (read by server code at run time) comes from the container environment, not from the image.
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /app/public ./public
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
USER node
EXPOSE 3000
CMD ["node", "server.js"]
