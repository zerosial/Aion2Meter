# A2Capture (`a2cap`)

아이온2 트래픽을 `.pcapng` 파일로 떨어뜨리고, 세션 메타데이터를 JSON 매니페스트로 함께 기록하는 명령행 캡처 도구입니다. **A2Meter의 회귀 테스트와 파서 디버깅을 위한 캡처 수단**입니다.

- **출력 형식:** 콘솔 EXE
- **타깃:** `net8.0` / `x64`
- **어셈블리 이름:** `a2cap`
- **의존성:** SharpPcap 6.3.1, PacketDotNet 1.4.8, Npcap (런타임)

---

## 1. 사용 시나리오

A2Meter 파서를 수정한 뒤 *실제 게임을 다시 돌리지 않고* 검증하기 위한 워크플로의 1단계입니다.

```
[게임 한 번 캡처]                  [파서 수정/검증을 N회 반복]
 a2cap  ─→  세션디렉토리/  ──→  a2inspect (이벤트 카운트 확인)
                              ─→  A2Meter --replay (시각적 회귀)
```

---

## 2. 명령어

```powershell
a2cap --list                      # 사용 가능한 어댑터 나열 후 종료
a2cap                              # 기본값으로 캡처 (Ctrl-C로 종료)
a2cap [options]                    # 옵션 지정 캡처
```

### 옵션

| 옵션 | 기본값 | 설명 |
|---|---|---|
| `--adapter <인덱스\|이름\|친숙명>` | 첫 번째 IPv4 활성 어댑터 | 캡처할 NIC 지정 |
| `--port <N>` | `13328` | 캡처할 TCP 포트. **아이온2 서버 시드 포트**. `0`이면 모든 TCP. |
| `--port-range LO-HI` | `1024-65535` | `--port 0`일 때만 사용. 포트 범위. |
| `--filter "<bpf>"` | (자동) | 사용자 지정 BPF 필터. 위 두 옵션 무시. |
| `--out <디렉토리>` | `./captures` | 출력 디렉토리. 그 아래 세션 ID 폴더 자동 생성. |
| `--rotate-mb <int>` | `200` | 파일 크기가 N MB 초과 시 다음 파일로 회전. |
| `--rotate-min <int>` | `30` | 파일 작성 후 N분 경과 시 다음 파일로 회전. |
| `--quiet` | (off) | 라이브 카운터 출력 억제. |
| `--help` / `-h` | — | 도움말 표시. |

### 예시

```powershell
# 기본 캡처 (./captures/<세션ID>/ 에 저장)
a2cap

# 특정 NIC + 100MB마다 회전
a2cap --adapter "Ethernet" --rotate-mb 100

# 포트가 바뀐 빌드를 디버깅: 모든 TCP 캡처
a2cap --port 0 --port-range 1024-65535

# 어댑터 목록 확인
a2cap --list
```

---

## 3. 출력 구조

```
captures/
└── 20260503-153022/                ← 세션 ID = 시작 시각
    ├── manifest.json               ← 메타데이터 (실시간 갱신)
    ├── capture-153022.pcapng       ← 첫 번째 회전
    ├── capture-153555.pcapng       ← 두 번째 회전
    └── capture-160055.pcapng
```

### manifest.json 스키마

```json
{
  "sessionId": "20260503-153022",
  "startedAt": "2026-05-03T06:30:22Z",
  "endedAt":   "2026-05-03T07:10:55Z",
  "adapter": {
    "name":         "\\Device\\NPF_{...}",
    "description":  "Realtek Gaming GbE Family Controller",
    "friendlyName": "이더넷",
    "mac":          "AA:BB:CC:DD:EE:FF"
  },
  "filter":     "tcp port 13328",
  "port":       13328,
  "portRange":  null,
  "rotateMb":   200,
  "rotateMin":  30,
  "host": {
    "machine":        "DESKTOP-XXX",
    "os":             "Microsoft Windows NT 10.0.26100.0",
    "libpcapVersion": "6.3.1.0"
  },
  "packetsTotal": 1234567,
  "bytesTotal":   987654321,
  "files": [
    "capture-153022.pcapng",
    "capture-153555.pcapng",
    "capture-160055.pcapng"
  ]
}
```

매니페스트는 다음 시점에 디스크에 다시 쓰입니다.

- 캡처 시작 직후 (헤더만)
- 매 회전 직후 (파일 추가)
- Ctrl-C 정상 종료 시 (`endedAt`, `packetsTotal`, `bytesTotal` 채워짐)

비정상 종료(전원 차단 등) 시 마지막 회전 시점까지의 매니페스트는 보존되지만 `endedAt`은 비어 있습니다.

---

## 4. 동작 원리

```
SharpPcap.LibPcapLiveDevice
        │
        │  PacketArrival (background thread)
        ▼
  CaptureSession.OnPacket
        │
        ├─ Interlocked.Increment _totalPackets
        ├─ CaptureFileWriterDevice.Write  ──→  현재 회전 pcapng
        └─ if (overSize || overTime) RotateLocked()
                    │
                    └─ 이전 라이터 Close, 새 파일 Open, manifest.json 갱신
```

- 모든 디스크 쓰기는 `_writerLock`으로 직렬화되어 회전 도중 데이터 손실이 없습니다.
- 1초마다 `Tick`이 동작 카운터를 갱신하고 (드롭된 패킷 포함), `--quiet`가 아닌 이상 콘솔에 라이브 표시합니다.
- `Console.CancelKeyPress`로 Ctrl-C를 가로채 깨끗하게 종료합니다.

---

## 5. 아이온2 포트에 대해

기본 포트 `13328`은 원본 A2Power 스니퍼가 *fast-path*로 처리하는 포트입니다 — 한 번의 패킷 매칭만으로 서버 포트로 자동 확정됩니다. 다른 포트는 매직 페이로드 다중 매칭이 필요합니다.

게임 빌드가 변경되어 포트가 바뀌면 다음 절차로 진단합니다.

```powershell
# 1) 모든 TCP 캡처
a2cap --port 0

# 2) a2inspect로 후보 포트 식별
a2inspect ./captures/<세션ID>
# → "Magic-payload (likely server) ports:" 섹션 확인

# 3) 식별된 포트로 좁혀서 다시 캡처
a2cap --port <발견된포트>
```

---

## 6. Npcap 설치 확인

스타트업에서 `EnsurePcapAvailable` 호출이 실패하면 다음 메시지로 안내합니다.

```
[a2cap] Npcap is not installed (or not loadable).
        Install Npcap from https://npcap.com — tick
        "Install Npcap in WinPcap API-compatible Mode".
```

설치 후에도 어댑터가 비어 있다면 **관리자 권한**으로 다시 실행하세요.

---

## 7. 빌드

```powershell
dotnet build A2Capture.csproj -c Release
# → bin/Release/net8.0/a2cap.exe
```

A2Meter 메인 앱과 달리 단일 파일 게시 설정이 없습니다.
배포가 필요하면 `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` 옵션을 명령행에 직접 붙이세요.
