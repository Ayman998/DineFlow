# The guest and merchant apps, served by Caddy with automatic HTTPS. Caddy also forwards the API's
# paths to the api container, so each app calls the API on its own origin (no CORS involved).
# Build from the repository root:
#   docker build -f deploy/web.Dockerfile -t dineflow-web .

FROM node:22-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx ng build guest && npx ng build merchant

FROM caddy:2-alpine
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist/guest/browser /srv/guest
COPY --from=build /web/dist/merchant/browser /srv/merchant
