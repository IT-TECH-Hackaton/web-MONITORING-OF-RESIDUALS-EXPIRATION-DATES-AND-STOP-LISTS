FROM node:22-alpine AS build
WORKDIR /repo
COPY package.json ./
COPY packages ./packages
COPY apps/web ./apps/web
RUN npm install
ARG VITE_API_URL=
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build -w @repo/web

FROM nginx:1.27-alpine
COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/apps/web/dist /usr/share/nginx/html
