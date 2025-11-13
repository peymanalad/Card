CREATE USER otel_collector IDENTIFIED BY "ChangeMe!";

-- Minimal privileges required to read performance metrics.
GRANT CREATE SESSION TO otel_collector;
GRANT SELECT_CATALOG_ROLE TO otel_collector;
GRANT SELECT ON V_$METRIC TO otel_collector;
GRANT SELECT ON V_$SYSTEM_EVENT TO otel_collector;
GRANT SELECT ON V_$SYSSTAT TO otel_collector;
GRANT SELECT ON V_$SESSION TO otel_collector;
GRANT SELECT ON V_$PROCESS TO otel_collector;

-- Optional: limit the user's default tablespace and quota if needed.
ALTER USER otel_collector DEFAULT TABLESPACE USERS QUOTA 0 ON USERS;
