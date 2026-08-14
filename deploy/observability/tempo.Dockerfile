FROM grafana/tempo:2.8.0

COPY deploy/observability/tempo.yaml \
  /etc/tempo/config.yaml

