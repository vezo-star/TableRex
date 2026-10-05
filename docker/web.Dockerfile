# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f docker/web.Dockerfile -t bookingplatform/web .
# /api and /hubs are expected to be routed to the Api by the reverse proxy in front of this container.

FROM node:24-alpine AS build
WORKDIR /app

COPY Frontend/BookingPlatform.Web/package.json Frontend/BookingPlatform.Web/package-lock.json ./
# postinstall (quasar prepare) needs the full project; quasar build runs it anyway.
RUN npm ci --ignore-scripts

COPY Frontend/BookingPlatform.Web/ ./
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
RUN npm run build

FROM nginx:alpine AS final
# Run as the unprivileged nginx user on a non-privileged port.
RUN sed -i -e '/^user /d' -e 's#^pid .*#pid /tmp/nginx.pid;#' /etc/nginx/nginx.conf \
    && chown -R nginx:nginx /var/cache/nginx
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/spa /usr/share/nginx/html
USER nginx
EXPOSE 8080
