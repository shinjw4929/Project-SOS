# 로깅 Tier 2: MongoDB 포렌식 분석 저장소

## 목적

개별 레코드 조회 + 원인 추적을 위한 Document DB.
"이 엔티티 왜 벽 뚫었나" → 위반 건 열고 CommandTrail 읽기.

---

## 저장 대상 3가지 카테고리

| 카테고리 | 설명 | 예시 |
|---------|------|------|
| **CommandTrail** | 엔티티별 RPC/명령 시퀀스 | MoveRequest→AttackRequest→GatherRequest |
| **게임 정합성 오류** | 런타임 위반 감지 | WallPenetration, NegativeHP 등 |
| **코드 레벨 에러** | 런타임 예외, 비정상 상태 | NullRef, 잘못된 Entity 참조 |

---

## Document 스키마

```json
{
  "_id": "ObjectId",
  "timestamp": "ISODate",
  "session_id": "abc-123",
  "build_version": "0.1.0",
  "server_id": "prod-1",

  "record_type": "violation | error | command_trail",
  "violation_type": "WallPenetration",
  "severity": "info | warning | error",

  "entity": {
    "ghost_id": 42,
    "entity_type": "Worker",
    "team_id": 1,
    "position": { "x": 10.5, "y": 0, "z": 20.3 },
    "hp": { "current": 100, "max": 100 },
    "action_state": "Moving",
    "aggro_target_ghost_id": null
  },

  "command_trail": [
    { "tick": 1200, "command": "MoveRequest", "data": { "target_x": 15, "target_z": 25 } },
    { "tick": 1180, "command": "AttackRequest", "data": { "target_ghost_id": 38 } }
  ],

  "context": {
    "wave_phase": 1,
    "elapsed_time": 62.4,
    "total_entities": 150,
    "server_tick": 3600
  },

  "message": "Entity penetrated wall at grid position (3,5)"
}
```

---

## 인덱스 설계

```javascript
// 필수 인덱스
db.game_logs.createIndex({ "record_type": 1, "timestamp": -1 })
db.game_logs.createIndex({ "violation_type": 1, "timestamp": -1 })
db.game_logs.createIndex({ "session_id": 1, "timestamp": -1 })
db.game_logs.createIndex({ "entity.ghost_id": 1, "timestamp": -1 })

// TTL 인덱스 (90일 자동 삭제)
db.game_logs.createIndex({ "timestamp": 1 }, { expireAfterSeconds: 7776000 })
```

---

## 수집 파이프라인

```
Unity Build → com.unity.logging → 로컬 파일 (Tier 1)
                                       ↓
                                 Vector Agent (tail + parse)
                                       ↓
                                 MongoDB (Document sink)
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

[sinks.mongodb]
type = "mongodb"
inputs = ["parse_log"]
connection_string = "${MONGODB_URI}"
database = "project_sos"
collection = "game_logs"
```

---

## 쿼리 패턴

```javascript
// 특정 엔티티의 CommandTrail 조회
db.game_logs.find({
  "record_type": "command_trail",
  "entity.ghost_id": 42,
  "session_id": "abc-123"
}).sort({ "timestamp": -1 }).limit(50)

// WallPenetration 위반 조회
db.game_logs.find({
  "record_type": "violation",
  "violation_type": "WallPenetration",
  "timestamp": { "$gte": ISODate("2026-03-12T00:00:00Z") }
}).sort({ "timestamp": -1 })

// 빌드별 에러 집계
db.game_logs.aggregate([
  { "$match": { "record_type": "error" } },
  { "$group": { "_id": "$build_version", "count": { "$sum": 1 } } },
  { "$sort": { "count": -1 } }
])
```
