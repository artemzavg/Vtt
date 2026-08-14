FROM otel/opentelemetry-collector-contrib:0.127.0

COPY deploy/observability/otel-collector.yaml \
  /etc/otelcol-contrib/config.yaml

