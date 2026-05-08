# ── Stage 1: Build Vue SPA ────────────────────────────────────────────────────
FROM node:22-alpine AS frontend

WORKDIR /app/ClientApp
COPY pokemon-draft/ClientApp/package*.json ./
RUN npm ci

COPY pokemon-draft/ClientApp ./

ARG VITE_GIT_SHA=dev
ENV VITE_GIT_SHA=$VITE_GIT_SHA
RUN npm run build-only

# ── Stage 2: Build .NET API ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend

WORKDIR /app
COPY pokemon-draft/*.csproj ./
RUN dotnet restore

COPY pokemon-draft/ ./

RUN dotnet publish -c Release -o /publish --no-restore

# Copy compiled frontend into the publish output so it's available at runtime
COPY --from=frontend /app/ClientApp/dist /publish/ClientApp/dist

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=backend /publish ./

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "pokemon-draft-api.dll"]
