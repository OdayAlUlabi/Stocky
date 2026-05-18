# Multi-stage build for the Vite/React static frontend.
# Served by nginx on 8080 (ACA ingress targetPort matches).

FROM node:22-alpine AS build
ARG VITE_API_BASE_URL=""
ARG VITE_GOOGLE_CLIENT_ID=""
ARG VITE_ENTRA_TENANT_ID=""
ARG VITE_ENTRA_SPA_CLIENT_ID=""
ARG VITE_ENTRA_API_SCOPE=""
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV VITE_GOOGLE_CLIENT_ID=$VITE_GOOGLE_CLIENT_ID
ENV VITE_ENTRA_TENANT_ID=$VITE_ENTRA_TENANT_ID
ENV VITE_ENTRA_SPA_CLIENT_ID=$VITE_ENTRA_SPA_CLIENT_ID
ENV VITE_ENTRA_API_SCOPE=$VITE_ENTRA_API_SCOPE
WORKDIR /src
COPY src/Web/package.json src/Web/package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY src/Web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime
# nginx config must listen on 8080 and serve SPA fallback.
COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
# Replace the base image's main config with one that uses /tmp for all
# runtime paths so nginx can run as the unprivileged 'nginx' user.
COPY src/Web/nginx.main.conf /etc/nginx/nginx.conf
RUN chown -R nginx:nginx /usr/share/nginx /var/cache/nginx /var/log/nginx
USER nginx
COPY --from=build --chown=nginx:nginx /src/dist /usr/share/nginx/html
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
