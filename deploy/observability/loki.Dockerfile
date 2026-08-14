FROM grafana/loki:3.5.1

COPY deploy/observability/loki.yaml \
  /etc/loki/config.yaml

