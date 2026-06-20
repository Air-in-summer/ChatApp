# syntax=docker/dockerfile:1

# Build React/Vite frontend bằng đúng major Node đang dùng trong development.
FROM node:22-alpine AS frontend-build
WORKDIR /src/client

COPY multiroomchatwebapp.client/package.json multiroomchatwebapp.client/package-lock.json ./
RUN npm ci

COPY multiroomchatwebapp.client/ ./
RUN npm run build \
    && test -f /src/client/dist/index.html

# Restore và publish ASP.NET Core bằng Linux .NET SDK.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY MultiRoomChatWebApp.Server/MultiRoomChatWebApp.Server.csproj MultiRoomChatWebApp.Server/
RUN dotnet restore MultiRoomChatWebApp.Server/MultiRoomChatWebApp.Server.csproj \
    /p:SkipSpaProjectReference=true

COPY MultiRoomChatWebApp.Server/ MultiRoomChatWebApp.Server/
RUN rm -rf MultiRoomChatWebApp.Server/Publish

RUN dotnet publish MultiRoomChatWebApp.Server/MultiRoomChatWebApp.Server.csproj \
    --configuration ${BUILD_CONFIGURATION} \
    --output /app/publish \
    --no-restore \
    /p:SkipSpaProjectReference=true \
    /p:UseAppHost=false

# Frontend production được phục vụ cùng origin từ ASP.NET Core.
RUN rm -rf /app/publish/wwwroot
COPY --from=frontend-build /src/client/dist /app/publish/wwwroot

# Production nhận toàn bộ config qua environment variables.
# Build phải dừng ngay nếu config Development hoặc secret file lọt vào publish.
RUN rm -f /app/publish/appsettings*.json \
    && forbidden_files="$(find /app/publish -type f \
        \( -name 'appsettings*.json' \
        -o -name '.env' \
        -o -name '.env.*' \) \
        -print)" \
    && if [ -n "${forbidden_files}" ]; then \
        printf 'Forbidden files found in publish output:\n%s\n' "${forbidden_files}"; \
        exit 1; \
    fi
RUN test -f /app/publish/wwwroot/index.html \
    && test -f /app/publish/MultiRoomChatWebApp.Server.dll

# Runtime image chỉ chứa ASP.NET runtime, app publish và curl cho healthcheck.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
ENV ASPNETCORE_URLS=http://+:8080
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/Logs \
    && chown -R ${APP_UID}:${APP_UID} /app

COPY --from=backend-publish --chown=${APP_UID}:${APP_UID} /app/publish ./

USER ${APP_UID}
EXPOSE 8080

ENTRYPOINT ["dotnet", "MultiRoomChatWebApp.Server.dll"]
