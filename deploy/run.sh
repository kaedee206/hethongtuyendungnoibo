#!/usr/bin/env bash
# ==============================================================================
# KHỞI CHẠY ATS NOVERATECH - MÔI TRƯỜNG PRODUCTION VPS (ALL-IN-ONE)
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
echo "  KHỞI CHẠY ATS NOVERATECH - MÔI TRƯỜNG PRODUCTION VPS"
echo "======================================================================"
echo " - Thư mục hoạt động     : $SCRIPT_DIR"
echo " - Dịch vụ               : PostgreSQL + Authelia SSO + Ats.Web + Nginx SSL"
echo "======================================================================"

docker compose up -d --build "$@"

echo ""
echo "✓ Khởi chạy Production VPS hoàn tất!"
echo "  Kiểm tra trạng thái : docker compose ps"
echo "  Xem logs tổng hợp   : docker compose logs -f"
echo "  Dừng hệ thống       : docker compose down"
