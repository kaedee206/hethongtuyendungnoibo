FROM nginx:alpine

# Loại bỏ cấu hình mặc định
RUN rm -f /etc/nginx/conf.d/default.conf

# Sao chép cấu hình tối ưu của hệ thống ATS
COPY nginx.conf /etc/nginx/nginx.conf
COPY conf.d/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD wget -qO- http://127.0.0.1/nginx-health || exit 1

CMD ["nginx", "-g", "daemon off;"]
