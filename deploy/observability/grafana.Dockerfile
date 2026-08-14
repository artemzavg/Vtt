FROM grafana/grafana:12.0.1

COPY deploy/observability/grafana/provisioning \
  /etc/grafana/provisioning

