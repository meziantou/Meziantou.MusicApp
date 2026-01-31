# syntax=docker/dockerfile:1@sha256:b6afd42430b15f2d2a4c5a02b919e98a525b785b1aaff16747d2f623364e39b6

FROM node:24-alpine@sha256:cd6fb7efa6490f039f3471a189214d5f548c11df1ff9e5b181aa49e22c14383e AS build

ARG VITE_COMMIT_HASH=unknown

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN VITE_COMMIT_HASH=${VITE_COMMIT_HASH} npm run build

FROM nginx:1.29-alpine@sha256:4870c12cd2ca986de501a804b4f506ad3875a0b1874940ba0a2c7f763f1855b2

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
