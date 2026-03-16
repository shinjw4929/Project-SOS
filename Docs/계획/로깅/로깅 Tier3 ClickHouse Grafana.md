# 로깅 Tier 3: ClickHouse + Grafana

## 목적

집계/추이 분석용 시계열 데이터. 빌드 간 위반 비율 비교, 실시간 대시보드, 알림.

---

## ClickHouse 테이블 스키마

```sql
CREATE TABLE violation_logs (
    timestamp DateTime64(3, 'UTC'),
    build_version LowCardinality(String),
    session_id String,
    server_id LowCardinality(String),
    record_type LowCardinality(String),
    violation_type LowCardinality(String),
    severity LowCardinality(String),
    entity_type LowCardinality(String),
    team_id Int8,
    message String
) ENGINE = MergeTree()
PARTITION BY toYYYYMMDD(timestamp)
ORDER BY (build_version, violation_type, timestamp)
TTL timestamp + INTERVAL 90 DAY DELETE;
```

### Materialized View (빌드별 위반 유형 집계)

```sql
CREATE MATERIALIZED VIEW violation_rate_by_build_mv TO violation_rate_by_build AS
SELECT
    build_version,
    violation_type,
    toStartOfHour(timestamp) AS hour,
    count() AS violation_count
FROM violation_logs
WHERE record_type = 'violation'
GROUP BY build_version, violation_type, hour;
```

---

## 데이터 수집: Vector 동시 전송

Vector의 네이티브 fan-out으로 MongoDB와 ClickHouse에 동시 전송. 별도 ETL 불필요.

```
Unity → File → Vector ──→ MongoDB (Document, 포렌식 조회)
                     └──→ ClickHouse (플랫, 집계/추이)
```

### Vector 설정 (참고)

```toml
[sources.game_logs]
type = "file"
include = ["${PERSISTENT_DATA_PATH}/Logs/game_*.log"]

[transforms.parse_log]
type = "remap"
inputs = ["game_logs"]
source = '''
. = parse_regex!(.message, r'^(?P<timestamp>\S+) (?P<level>\S+) \[(?P<world>[SC]):(?P<category>\w+)\] (?P<msg>.*)')
'''

[transforms.flatten_for_clickhouse]
type = "remap"
inputs = ["parse_log"]
source = '''
.record_type = .record_type ?? "log"
.violation_type = .violation_type ?? ""
.severity = .level
.entity_type = .entity_type ?? ""
.team_id = to_int(.team_id) ?? 0
'''

[sinks.clickhouse]
type = "clickhouse"
inputs = ["flatten_for_clickhouse"]
endpoint = "${CLICKHOUSE_URL}"
database = "project_sos"
table = "violation_logs"

[sinks.mongodb]
type = "mongodb"
inputs = ["parse_log"]
connection_string = "${MONGODB_URI}"
database = "project_sos"
collection = "game_logs"
```

---

## Grafana 대시보드

| 대시보드 | 주요 패널 |
|---------|----------|
| Violation Overview | 위반 유형별 발생 건수 (시계열), 빌드별 위반 비율 비교 |
| Build Comparison | 빌드 A vs B 위반 유형별 증감, 새 빌드 배포 후 위반 추이 |
| Entity Analysis | 엔티티 타입별 위반 분포, 팀별 위반 건수 |
| Error Dashboard | 코드 에러 발생률, 에러 메시지별 빈도 |

---

## 알림 규칙

| 조건 | 심각도 | 설명 |
|------|--------|------|
| 새 빌드 배포 후 1시간 내 위반 건수 > 이전 빌드 평균 200% | Warning | 빌드 회귀 감지 |
| WallPenetration 5분간 10건 초과 | Warning | 물리 정합성 이상 |
| record_type=error 5분간 50건 초과 | Critical | 코드 레벨 에러 급증 |

### 알림 쿼리 예시

```sql
-- WallPenetration 5분 집계
SELECT count() AS cnt
FROM violation_logs
WHERE violation_type = 'WallPenetration'
  AND timestamp >= now() - INTERVAL 5 MINUTE;

-- 빌드별 위반 비율 비교
SELECT
    build_version,
    count() AS violations,
    countIf(violation_type = 'WallPenetration') AS wall_pen
FROM violation_logs
WHERE timestamp >= now() - INTERVAL 1 HOUR
GROUP BY build_version;
```
