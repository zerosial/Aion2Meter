# A2Inspect (`a2inspect`)

a2cap으로 캡처한 세션을 **A2Meter와 동일한 프로토콜 스택**에 통과시켜, 파서가 올바르게 동작하는지 진단하는 명령행 도구입니다. UI 의존성이 없어 헤드리스 환경(CI 등)에서도 실행 가능합니다.

- **출력 형식:** 콘솔 EXE
- **타깃:** `net8.0` / `x64`
- **어셈블리 이름:** `a2inspect`
- **의존성:** SharpPcap, PacketDotNet (pcap 읽기 전용)

---

## 1. 코드 공유 구조

A2Inspect는 **자체 프로토콜 코드를 두지 않습니다.** 대신 `.csproj`에서 A2Meter 프로젝트의 프로토콜 소스를 직접 컴파일에 포함합니다 ([A2Inspect.csproj:21-32](A2Inspect.csproj#L21-L32)).

```xml
<Compile Include="..\A2Meter\Dps\Protocol\ProtocolUtils.cs"   Link="Protocol\ProtocolUtils.cs"  />
<Compile Include="..\A2Meter\Dps\Protocol\Lz4Decoder.cs"      Link="Protocol\Lz4Decoder.cs"     />
<Compile Include="..\A2Meter\Dps\Protocol\TcpReassembler.cs"  Link="Protocol\TcpReassembler.cs" />
<Compile Include="..\A2Meter\Dps\Protocol\StreamProcessor.cs" Link="Protocol\StreamProcessor.cs" />
<Compile Include="..\A2Meter\Dps\Protocol\SkillDatabase.cs"   Link="Protocol\SkillDatabase.cs"  />
<Compile Include="..\A2Meter\Dps\Protocol\PacketDispatcher.cs" Link="Protocol\PacketDispatcher.cs" />
<Compile Include="..\A2Meter\Dps\Protocol\PartyStreamParser.cs" Link="Protocol\PartyStreamParser.cs" />
<Compile Include="..\A2Meter\Dps\Protocol\ServerMap.cs"       Link="Protocol\ServerMap.cs" />
<Compile Include="..\A2Meter\Dps\JobMapping.cs"               Link="Dps\JobMapping.cs" />
<Compile Include="..\A2Meter\Dps\Models.cs"                   Link="Dps\Models.cs" />
```

**이 설계의 의도:** A2Meter에서 파서를 수정하면 A2Inspect가 다음 빌드부터 즉시 같은 코드를 검증하므로, 코드 드리프트가 원천적으로 발생하지 않습니다.

`Data/game_db.json` 또한 동일한 파일을 출력 디렉토리에 복사 링크로 가져옵니다.

---

## 2. 사용법

```powershell
a2inspect <세션디렉토리>
```

세션 디렉토리는 a2cap이 만든 폴더 (`captures/20260503-153022/` 같은) 입니다.
`manifest.json`이 있으면 거기 명시된 파일 순서대로, 없으면 디렉토리의 모든 `*.pcap*` 파일을 글롭으로 처리합니다.

### 예시

```powershell
a2inspect ./captures/20260503-153022
```

---

## 3. 4-pass 분석 파이프라인

`Program.cs`는 같은 pcap 파일들을 4번 순회하며 각 패스에서 다른 통계를 뽑습니다.

### Pass 1 — 포트 히스토그램 + 매직 페이로드 매칭

목적: **서버 포트가 무엇인지** 확정.

```
Top 10 TCP flows by packet count:
     src ->    dst   packets       bytes
   13328 -> 50432    102345    98765432
   50432 -> 13328     34567     1234567
   ...

Magic-payload (likely server) ports:
  port  13328: 1234 payloads matched
```

매직 페이로드는 [`ProtocolUtils.LooksLikeGameMagicPayload`](../A2Meter/Dps/Protocol/ProtocolUtils.cs)로 휴리스틱 검사하며, 가장 많이 매칭된 포트를 *서버 포트*로 채택합니다. 매칭이 없으면 fallback으로 13328을 시도합니다.

### Pass 2 — 파서 이벤트 카운트

`StreamProcessor → PacketDispatcher` 체인을 실제로 가동하고, 각 이벤트가 몇 번 발화했는지 셉니다.

```
Parser results:
  Damage      : 4231
  UserInfo    : 28
  MobSpawn    : 156
  BossHp      : 1523
  Buff        : 891
  CombatPower : 12 (by entity), 3 (by name)
  EntityRem   : 67

Party parser results:
  PartyList   : 5
  PartyUpdate : 47
  ...
```

수가 **0이면 파서가 깨졌거나** 캡처가 비정상입니다.

### Pass 3 — 보스 단위 DPS 타임라인

[DpsPipeline.cs](../A2Meter/Dps/DpsPipeline.cs)와 동일한 *"1풀 = 1세션"* 의미론으로 5초마다 누적 DPS를 출력합니다. 패킷 클럭(타임스탬프) 기반이므로 결과는 결정적(deterministic)입니다.

```
DPS timeline (packet-clock, boss-scoped):
  ── boss spawn: M_Boss_Glasvain (id=99999 hp=26,330,000) ──
  t=   5.0s  ★Glasvain  HP=22,800,000/26,330,000 (86.6%)  total=  3,530,000
      수호성      job= 4    1,200,000   240,000/s   34.0%
      살성       job= 3      980,000   196,000/s   27.8%
      ...
  ── final M_Boss_Glasvain (id=99999) at t=87.3s ──
  ...
```

이 출력을 인게임에서 본 DPS와 비교하면 파서 결과의 정확도를 정량적으로 검증할 수 있습니다.

### Pass 4 — 모든 데미지 수신자 히스토그램

```
Top 15 damage receivers (any target):
  rx=  98,765,432  hits= 4231  entityId= 99999  mobCode= 12345  M_Boss_Glasvain
  rx=  45,678,901  hits= 1502  entityId= 88888  mobCode= 12300  M_Add_Glasvain
  ...
```

**용도:** "Pass 3에서 보스가 안 잡혔다"는 증상을 디버깅할 때, 보스가 실제로는 다른 `entityId`로 파싱된 것은 아닌지 확인합니다.

---

## 4. 진단 메시지 해석

마지막에 `StreamProcessor diag`가 출력됩니다.

```
StreamProcessor diag: calls=1234 dispatch=4321 lz4=89 lz4fail=0
                      sync=2 invalid=1 incomplete=5 bufLen=0
```

| 필드 | 의미 | 비정상 신호 |
|---|---|---|
| `calls` | `ProcessData` 호출 횟수 | 0 → TCP 재조립 실패 |
| `dispatch` | 메시지 디스패치 횟수 | 0 → 프레임 디코딩 실패 |
| `lz4` | LZ4 디컴프레션 성공 횟수 | — |
| `lz4fail` | LZ4 디컴프레션 실패 | >0 → 압축 컨테이너 포맷 변경 의심 |
| `sync` | 싱크 마커로 복구한 횟수 | 다수 → 캡처 손실 또는 프로토콜 변경 |
| `invalid` | 길이 헤더가 비정상 | 다수 → 위와 동일 |
| `incomplete` | 버퍼 부족으로 다음 호출 대기 | 정상 (낮은 수치) |
| `bufLen` | 종료 시점 잔여 버퍼 | 매우 큼 → 마지막 메시지 미완성 |

---

## 5. 일반적 활용 패턴

### 신규 서버 빌드 회귀 검증

```powershell
# 새 빌드에서 캡처
a2cap

# 파서 결과 확인 (Damage 이벤트가 0이면 프로토콜 변경)
a2inspect ./captures/<세션ID>
```

### 파서 코드 수정 후 영향 확인

```powershell
# A2Meter/Dps/Protocol/PacketDispatcher.cs 수정 후
dotnet build src/A2Inspect

# 기존 캡처들에 회귀 실행
foreach ($d in Get-ChildItem ./captures -Directory) {
  Write-Host "=== $($d.Name) ==="
  ./bin/Release/net8.0/a2inspect.exe $d.FullName
}
```

### CI에서 자동 회귀

`a2inspect`는 종료 코드 0/1만 반환하지만, 파싱한 텍스트 카운트가 기준치 이하면 실패하도록 PowerShell/bash 래퍼를 작성해 CI 파이프라인에 끼워 넣을 수 있습니다.

---

## 6. 빌드

```powershell
dotnet build A2Inspect.csproj -c Release
# → bin/Release/net8.0/a2inspect.exe
# + 동일 디렉토리에 game_db.json (자동 복사)
```

빌드 시점에 A2Meter 프로젝트의 프로토콜 파일들이 함께 컴파일되므로, A2Meter 빌드가 깨져 있으면 A2Inspect도 빌드에 실패합니다.
