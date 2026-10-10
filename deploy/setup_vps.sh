#!/usr/bin/env bash
# ==============================================================================
# SCRIPT THIẾT LẬP TỰ ĐỘNG VPS 2GB RAM CHO HỆ THỐNG ATS + SSO NOVERATECH
# Hỗ trợ: Ubuntu 22.04 LTS, Ubuntu 24.04 LTS, Debian 12
# ==============================================================================

set -euo pipefail

echo "=========================================================="
echo "   NOVERATECH ENTERPRISE - VPS SETUP & HARDENING SCRIPT   "
echo "=========================================================="

# 1. BƯỚC 1: TẠO 4GB SWAP (RAM ẢO TRÊN 30GB SSD)
echo "[1/5] Kiểm tra và cấu hình bộ nhớ RAM ảo (Swap 4GB)..."
if ! grep -q '/swapfile' /etc/fstab; then
    sudo fallocate -l 4G /swapfile || sudo dd if=/dev/zero of=/swapfile bs=1M count=4096
    sudo chmod 600 /swapfile
    sudo mkswap /swapfile
    sudo swapon /swapfile
    echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
    sudo sysctl vm.swappiness=10
    echo 'vm.swappiness=10' | sudo tee -a /etc/sysctl.conf
    echo " -> Đã tạo 4GB Swap thành công!"
else
    echo " -> Swapfile đã tồn tại, bỏ qua bước tạo."
fi

# 2. BƯỚC 2: CẤU HÌNH TƯỜNG LỬA UFW (CHỈ MỞ 22, 80, 443)
echo "[2/5] Cấu hình tường lửa UFW bảo vệ VPS..."
sudo apt-get update -y
sudo apt-get install -y ufw curl git certbot

sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp comment 'SSH Port'
sudo ufw allow 80/tcp comment 'HTTP Web'
sudo ufw allow 443/tcp comment 'HTTPS Web'
sudo ufw --force enable
echo " -> Tường lửa UFW đã kích hoạt: Chỉ cho phép port 22, 80, 443!"

# 3. BƯỚC 3: CÀI ĐẶT DOCKER VÀ DOCKER COMPOSE
echo "[3/5] Kiểm tra và cài đặt Docker & Docker Compose..."
if ! command -v docker &> /dev/null; then
    curl -fsSL https://get.docker.com -o get-docker.sh
    sudo sh get-docker.sh
    sudo usermod -aG docker $USER || true
    rm -f get-docker.sh
    echo " -> Đã cài đặt Docker thành công!"
else
    echo " -> Docker đã sẵn sàng."
fi

# Cấu hình BuildKit mặc định cho Docker Engine
sudo mkdir -p /etc/docker
if [ ! -f /etc/docker/daemon.json ]; then
    echo '{"features": {"buildkit": true}}' | sudo tee /etc/docker/daemon.json
    sudo systemctl restart docker 2>/dev/null || true
fi
export DOCKER_BUILDKIT=1
export COMPOSE_DOCKER_CLI_BUILD=1

# 4. BƯỚC 4: XIN CHỨNG CHỈ SSL MIỄN PHÍ VỚI CERTBOT LET'S ENCRYPT
echo "[4/5] Hướng dẫn cấp phát chứng chỉ SSL Let's Encrypt cho 2 Subdomain..."
echo "  - Subdomain 1: tuyendung.noveratech.digital"
echo "  - Subdomain 2: sso.noveratech.digital"
echo ""
echo "LƯU Ý: Trước khi chạy lệnh xin SSL, bạn PHẢI trỏ cả 2 bản ghi A của domain về IP của VPS này!"
read -p "Bạn đã trỏ DNS 2 domain về IP VPS này chưa? (y/N): " dns_confirmed
if [[ "$dns_confirmed" =~ ^[Yy]$ ]]; then
    sudo systemctl stop nginx 2>/dev/null || true
    echo "Đang cấp phát SSL cho tuyendung.noveratech.digital và sso.noveratech.digital..."
    sudo certbot certonly --standalone \
        -d tuyendung.noveratech.digital \
        -d sso.noveratech.digital \
        --agree-tos --register-unsafely-without-email --non-interactive || true
fi

# 5. BƯỚC 5: KHỞI ĐỘNG DOCKER COMPOSE
echo "[5/5] Khởi động cụm dịch vụ ATS + SSO + Postgres + Nginx..."
mkdir -p /var/www/certbot
sudo chmod +x init-db/*.sh 2>/dev/null || true

echo "Chạy lệnh sau để khởi chạy hệ thống:"
echo "  cd ~/hethongtuyendungnoibo/deploy"
echo "  docker compose up -d --build"
echo ""
echo "=========================================================="
echo "   THIẾT LẬP HOÀN TẤT! HỆ THỐNG ĐÃ SẴN SÀNG VẬN HÀNH      "
echo "=========================================================="
