# syntax=docker/dockerfile:1

FROM node:20 AS build
WORKDIR /app

ARG APP_DIR
RUN test -n "$APP_DIR"

# Copy app and install deps (lockfile optional across apps)
COPY ${APP_DIR}/package.json ./
COPY ${APP_DIR}/ ./

RUN if [ -f package-lock.json ]; then npm ci --no-audit --no-fund || npm install --no-audit --no-fund; else npm install --no-audit --no-fund; fi

# Angular output path varies by config; default dist/<project>
RUN npm run build

# Normalize Angular build output into a known folder.
# Angular 17+ often outputs to dist/<project>/browser; older configs use dist/<project>.
RUN set -eu; \
    mkdir -p /app/out; \
    browser_index="$(find dist -type f -path '*/browser/index.html' -print -quit 2>/dev/null || true)"; \
    if [ -n "$browser_index" ]; then \
    output_dir="$(dirname "$browser_index")"; \
    else \
    index="$(find dist -type f -name index.html -print -quit)"; \
    test -n "$index"; \
    output_dir="$(dirname "$index")"; \
    fi; \
    cp -a "$output_dir/." /app/out/

FROM nginx:1.27-alpine AS runtime

# Serve on port 4200 to match existing Ocelot routes
COPY deploy/nginx/angular-spa.conf /etc/nginx/conf.d/default.conf

# Copy normalized build output (overwrites default nginx index.html)
COPY --from=build /app/out/ /usr/share/nginx/html/

EXPOSE 4200
