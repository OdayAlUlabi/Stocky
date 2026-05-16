# Multi-stage build for the Vite/React static frontend.
# Served by nginx on 8080 (ACA ingress targetPort matches).

FROM node:22-alpine AS build
WORKDIR /src
COPY src/Web/package.json src/Web/package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY src/Web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime
# nginx config must listen on 8080 and serve SPA fallback.
COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
# Drop privileges via the bundled nginx user; rewrite the main config to not
# require root for the master process.
RUN sed -i 's/user  nginx;/# user nginx;/' /etc/nginx/nginx.conf \
    && sed -i 's|/var/run/nginx.pid|/tmp/nginx.pid|' /etc/nginx/nginx.conf \
    && sed -i 's|listen       80;|listen       8080;|' /etc/nginx/nginx.conf \
    && chown -R nginx:nginx /usr/share/nginx /var/cache/nginx /var/log/nginx \
    && touch /tmp/nginx.pid && chown nginx:nginx /tmp/nginx.pid
USER nginx
COPY --from=build --chown=nginx:nginx /src/dist /usr/share/nginx/html
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
