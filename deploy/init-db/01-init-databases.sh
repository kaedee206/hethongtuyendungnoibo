#!/bin/bash
set -e

# Tự động khởi tạo database cho cả ATS và Authelia SSO trên cùng 1 PostgreSQL
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE DATABASE authelia_db;
    GRANT ALL PRIVILEGES ON DATABASE authelia_db TO $POSTGRES_USER;
EOSQL
