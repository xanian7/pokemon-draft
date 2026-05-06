# ── Stage 1: Build Vue SPA ────────────────────────────────────────────────────
FROM node:22-alpine AS frontend

WORKDIR /app/ClientApp
COPY pokemon-draft/ClientApp/package*.json ./
RUN npm ci

COPY pokemon-draft/ClientApp ./
RUN npm run build-only

# ── Stage 2: Build .NET API ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend

WORKDIR /app
COPY pokemon-draft/*.csproj ./
RUN dotnet restore

COPY pokemon-draft/ ./
# Copy compiled frontend into the location the app expects at runtime
COPY --from=frontend /app/ClientApp/dist ./ClientApp/dist

RUN dotnet publish -c Release -o /publish --no-restore

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=backend /publish ./

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "pokemon-draft-api.dll"]
