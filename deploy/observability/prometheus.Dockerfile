FROM prom/prometheus:v3.4.1

COPY deploy/observability/prometheus.yaml \
  /etc/prometheus/prometheus.yaml

