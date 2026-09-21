# Grafana Dashboard – EGWN Interface EDA

Dieses Dashboard visualisiert die Prometheus-Metriken, die von der EGWN
Interface EDA App (`/metrics` Endpoint) exportiert werden.

## Import

1. In Grafana: **Dashboards → New → Import**.
2. Datei `egwn-interface-eda-dashboard.json` hochladen (oder Inhalt einfügen).
3. Beim Prompt `DS_PROMETHEUS` die vorhandene Prometheus-Datasource auswählen.
4. **Import**.

## Voraussetzungen

- Prometheus scraped den `/metrics`-Endpoint der App (siehe `Program.cs` –
  `app.MapMetrics()`).
- Beispiel `prometheus.yml` Scrape Job:

  ```yaml
  scrape_configs:
    - job_name: egwn-interface-eda
      metrics_path: /metrics
      static_configs:
        - targets: ['egwn-interface-eda:8080']
  ```

## Enthaltene Sections

- **Overview** – Erfolgsraten Meter/KPI, aktive Meter/Communities.
- **Meter Readings** – Rate nach Status, Dauer-Quantile, Tabelle mit letztem
  Erfolg/Versuch/Punkten pro Meter, Top-Failures der letzten Stunde.
- **KPI Readings** – Rate nach Status, Dauer-Quantile, Tabelle je Community.
- **Backfills** – laufender Backfill, Fortschritt, Items completed/failed/
  remaining, letzter Run.
- **Quartz Jobs** – nächster/letzter Fire-Zeitpunkt je Job.
- **HTTP** – Request-Rate nach Statuscode und Latenz-Quantile
  (`UseHttpMetrics`).

## Variablen

- `datasource` – Prometheus-Datasource.
- `community_id`, `meter_id`, `job` – Multi-Select-Filter.
