# syntax=docker/dockerfile:1

FROM node:20 AS build
WORKDIR /app

ARG APP_DIR
RUN test -n "$APP_DIR"

# Copy app and install deps (lockfile optional across apps)
COPY ${APP_DIR}/package.json ./
COPY ${APP_DIR}/ ./

RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi

# Angular output path varies by config; default dist/<project>
RUN npm run build

FROM nginx:1.27-alpine AS runtime

# Serve on port 4200 to match existing Ocelot routes
COPY deploy/nginx/angular-spa.conf /etc/nginx/conf.d/default.conf

# Copy build output (best-effort: copy whole dist)
COPY --from=build /app/dist/ /usr/share/nginx/html/

EXPOSE 4200
