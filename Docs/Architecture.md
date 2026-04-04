# Project-SOS 아키텍처

## Project Overview

Project-SOS is a multiplayer RTS game built with Unity 6 (6000.0.64f1) using Unity's Data-Oriented Tech Stack (DOTS):
- **Entity Component System (ECS)** via Unity Entities 1.4.3
- Unity Physics 1.4.3 (standard Unity Rigidbody & Colliders auto-convert via Baking System)
- **Netcode for Entities** 1.10.0 for multiplayer synchronization
- **Client-Server Architecture** with authoritative server

## Build & Development

- **Unity Version**: 6000.0.64f1 | **Solution**: `Project-SOS.sln` (6 assemblies)
- **Editor Settings**: Enter Play Mode Settings > Do not reload Domain or Scene
- **Player Settings**: Run in Background (checked)
- **접속 방식**: 룸 서버 경유 수동 연결 (AutoConnect 비활성화, `RoomClient` + `NetcodeConnectionUtil` 사용)

## Assembly Structure

```
Assets/Scripts/
├── Shared/          # Components, RPCs, systems used by both client & server
├── Client/          # Input handling, UI, visualization systems
├── Server/          # Server authority, game logic enforcement
└── Authoring/       # GameObject → Entity conversion (baking)
```

**상세 파일/폴더 구조**: [Systems/코드베이스 구조.md](Systems/코드베이스%20구조.md)

---

## 상세 문서

| 문서 | 내용 |
|------|------|
| [system-flow.md](Architecture/system-flow.md) | 시스템 실행 순서 (입력→공간분할→명령→타겟팅→이동→전투→정리→렌더링), 핵심 의존성 |
| [key-patterns.md](Architecture/key-patterns.md) | DamageEvent 버퍼, Authoring Composition, User State Machine, Work Range, VAT Animation |
| [network.md](Architecture/network.md) | 접속 흐름, Room Server Token Validation, RPC 목록 |
| [game-rules.md](Architecture/game-rules.md) | Prefabs/Scenes, Wave System, Collider 규칙, StructureFootprint, GameSettings 패턴 |
