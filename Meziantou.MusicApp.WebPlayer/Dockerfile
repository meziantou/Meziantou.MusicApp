# syntax=docker/dockerfile:1@sha256:2780b5c3bab67f1f76c781860de469442999ed1a0d7992a5efdf2cffc0e3d769

FROM node:24-alpine@sha256:d1b3b4da11eefd5941e7f0b9cf17783fc99d9c6fc34884a665f40a06dbdfc94f AS build

ARG VITE_COMMIT_HASH=unknown

WORKDIR /app

COPY package.json package-lock.json .npmrc ./
RUN npm ci

COPY . .
RUN VITE_COMMIT_HASH=${VITE_COMMIT_HASH} npm run build

FROM nginx:1.30-alpine@sha256:0272e4604ed93c1792f03695a033a6e8546840f86e0de20a884bb17d2c924883

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
