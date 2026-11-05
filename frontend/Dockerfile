# syntax=docker/dockerfile:1

# ── build: Angular production bundle ──────────────────────────────
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

# ── runtime: nginx serves the SPA and proxies /api and /hubs ──────
FROM nginx:1.30-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/inbox/browser /usr/share/nginx/html
EXPOSE 80
