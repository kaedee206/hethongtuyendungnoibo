#!/usr/bin/env bash
# ==============================================================================
# KHỞI CHẠY ATS NOVERATECH - MÔI TRƯỜNG DEVELOPMENT (TEST / DEMO)
# ==============================================================================
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if [[ ! -f .env ]]; then
    if [[ -f .env.example ]]; then
        echo "Tệp .env chưa tồn tại! Đang tạo từ .env.example..."
        cp .env.example .env
    fi
fi

echo "======================================================================"
echo "  KHỞI CHẠY ATS NOVERATECH - MÔI TRƯỜNG DEVELOPMENT (DEV)"
echo "======================================================================"
echo " - Thư mục hoạt động     : $SCRIPT_DIR"
echo " - ASPNETCORE_ENVIRONMENT: Development"
echo " - Web Nginx Port        : http://localhost:80"
echo " - Direct App Debug Port : http://localhost:8080"
echo "======================================================================"

docker compose up -d --build "$@"

echo ""
echo "✓ Khởi chạy Development thành công!"
echo "  Xem logs Web:   docker compose logs -f web"
echo "  Xem logs Nginx: docker compose logs -f nginx"
echo "  Dừng hệ thống:  docker compose down"
