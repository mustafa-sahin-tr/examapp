# syntax=docker/dockerfile:1

FROM node:20 AS build
WORKDIR /app

ARG APP_DIR
RUN test -n "$APP_DIR"

# Issue #488 (Y1): bagimliliklar YALNIZ package-lock.json'dan, `npm ci` ile kurulur. Bilerek `|| npm install` fallback'i
# YOK: lock kaymasi sessizce kilitsiz kuruluma donerse registry'deki yeni (zararli) yama surumleri ve degismis tarball'lar
# (ornegin cdn.sheetjs.com xlsx) butunluk (integrity) kontrolu olmadan imaja girer. Lock yoksa/uyumsuzsa build bilerek duser.
# Once yalniz manifest+lock kopyalanir (kaynak degisince npm ci katmani cache'ten gelir).
COPY ${APP_DIR}/package.json ${APP_DIR}/package-lock.json* ./
RUN test -f package-lock.json || { echo "ERROR: ${APP_DIR}/package-lock.json yok; kilitsiz kurulum yapilmaz (#488 Y1). 'npm install --package-lock-only' ile uretip commit'leyin." >&2; exit 1; }
RUN npm ci --no-audit --no-fund

COPY ${APP_DIR}/ ./

# Angular output path varies by config; default dist/<project>
RUN npm run build

# Normalize Angular build output into a known folder.
# Angular 17+ outputs to dist/<project>/browser.
# With outputMode=static (SSR prerender), the root file is index.csr.html, not index.html.
RUN set -eu; \
    mkdir -p /app/out; \
    browser_index="$(find dist -type f \( -path '*/browser/index.html' -o -path '*/browser/index.csr.html' \) -print -quit 2>/dev/null || true)"; \
    if [ -n "$browser_index" ]; then \
    output_dir="$(dirname "$browser_index")"; \
    else \
    index="$(find dist -type f -name index.html -print -quit)"; \
    test -n "$index"; \
    output_dir="$(dirname "$index")"; \
    fi; \
    cp -a "$output_dir/." /app/out/

FROM nginx:1.27-alpine AS runtime

# Serve on port 4200 to match existing Ocelot routes.
# Issue #333: şablon — nginx imajının giriş betiği /etc/nginx/templates/*.template dosyalarını envsubst ile (yalnız tanımlı
# ortam değişkenleri) /etc/nginx/conf.d/'ye yazar. CSP_JITSI_ORIGIN: ui için görüşme sunucusu origin'i (ör.
# https://meet.hedefokul.com), auth-ui için boş.
# Not: /etc/nginx/conf.d yazılabilir olmalı (readOnlyRootFilesystem/non-root sertleştirmesinde emptyDir bağlayın);
# aksi halde envsubst adımı atlanır ve stok default.conf silindiği için server bloğu olmadan açılır.
ENV CSP_JITSI_ORIGIN=""
RUN rm -f /etc/nginx/conf.d/default.conf
COPY deploy/nginx/angular-spa.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=755 deploy/nginx/15-validate-csp-env.sh /docker-entrypoint.d/15-validate-csp-env.sh

# Copy normalized build output (overwrites default nginx index.html)
COPY --from=build /app/out/ /usr/share/nginx/html/

# Angular SSR/static builds may emit index.csr.html instead of index.html.
# Normalize to index.html so nginx SPA fallback serves the app shell.
RUN if [ -f /usr/share/nginx/html/index.csr.html ]; then \
    cp /usr/share/nginx/html/index.csr.html /usr/share/nginx/html/index.html; \
    fi

EXPOSE 4200
