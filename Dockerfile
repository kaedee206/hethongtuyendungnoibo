# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=C.UTF-8 \
    LANG=C.UTF-8

# Tận dụng BuildKit cache mount cho apt để tăng tốc độ cài đặt gói hệ thống
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    mkdir -p /app/wwwroot/uploads/media /app/wwwroot/uploads/cvs /app/wwwroot/uploads/avatars /app/wwwroot/uploads/resumes /app/wwwroot/templates && \
    chown -R app:app /app/wwwroot/uploads /app/wwwroot/templates

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS restore
WORKDIR /src

# Sao chép các tệp cấu hình và định nghĩa project trước để tối ưu hóa Docker layer caching
COPY Directory.Build.props ./
COPY src/Ats.Web/Ats.Web.csproj src/Ats.Web/

# Tận dụng BuildKit cache mount cho NuGet package cache trên máy host
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Ats.Web/Ats.Web.csproj --nologo

FROM restore AS publish
WORKDIR /src

# Sao chép mã nguồn ứng dụng
COPY src/Ats.Web/ src/Ats.Web/

# Biên dịch và publish với NuGet cache mount và các tham số tối ưu hóa hiệu năng
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Ats.Web/Ats.Web.csproj \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:TieredCompilation=true \
    /p:PublishReadyToRun=false \
    --nologo

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .

USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://127.0.0.1:8080/ || exit 1

ENTRYPOINT ["dotnet", "Ats.Web.dll"]
