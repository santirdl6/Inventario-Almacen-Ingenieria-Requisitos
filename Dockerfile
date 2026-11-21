FROM node:24-bookworm-slim AS frontend-build
WORKDIR /source/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /source
COPY backend/src/Inventario.Api/Inventario.Api.csproj backend/src/Inventario.Api/
RUN dotnet restore backend/src/Inventario.Api/Inventario.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/src/Inventario.Api/Inventario.Api.csproj \
    --configuration Release --no-restore --output /publish /p:UseAppHost=false

FROM backend-build AS backend-tests
WORKDIR /source/backend
RUN dotnet restore Inventario.slnx
ENTRYPOINT ["dotnet", "test", "Inventario.slnx", "--configuration", "Release", "--no-restore", "--nologo"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && install -d -o "$APP_UID" -g "$APP_UID" /app/keys
WORKDIR /app
COPY --from=backend-build /publish/ ./
COPY --from=frontend-build /source/frontend/dist/inventario/browser/ ./wwwroot/
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeyDirectory=/app/keys
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/api/v1/health || exit 1
ENTRYPOINT ["dotnet", "Inventario.Api.dll"]
