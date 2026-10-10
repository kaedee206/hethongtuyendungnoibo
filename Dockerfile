FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=C.UTF-8 \
    LANG=C.UTF-8

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* && \
    mkdir -p /app/wwwroot/uploads/media /app/wwwroot/uploads/cvs /app/wwwroot/uploads/avatars /app/wwwroot/uploads/resumes /app/wwwroot/templates && \
    chown -R app:app /app/wwwroot/uploads /app/wwwroot/templates

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS restore
WORKDIR /src

COPY src/Ats.Web/Ats.Web.csproj src/Ats.Web/

RUN dotnet restore src/Ats.Web/Ats.Web.csproj

FROM restore AS publish
WORKDIR /src

COPY src/Ats.Web/ src/Ats.Web/

RUN dotnet publish src/Ats.Web/Ats.Web.csproj \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .

USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://127.0.0.1:8080/ || exit 1

ENTRYPOINT ["dotnet", "Ats.Web.dll"]
