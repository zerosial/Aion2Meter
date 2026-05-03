# A2Meter (메인 오버레이 앱)

아이온2 DPS 미터의 본체 애플리케이션입니다. 프레임 없는 오버레이 창에 실시간 DPS 바를 그립니다.

- **출력 형식:** `WinExe` (콘솔 미부착)
- **타깃:** `net8.0-windows` / `x64`
- **렌더러:** Direct2D (Vortice.Direct2D1) — 헤더 영역만 WinForms
- **단일 파일 게시:** `PublishSingleFile=true`, `SelfContained=true`
- **어셈블리 이름:** `A2Meter`

---

## 1. 디렉토리 구조

```
src/A2Meter/
├── Program.cs                ─ 진입점. 단일 인스턴스 뮤텍스, CLI 파싱, --demo
├── app.manifest              ─ DPI Awareness (PerMonitorV2), 관리자 권한 요청 없음
├── A2Meter.csproj            ─ 프로젝트 파일 (단일 파일 게시 설정 포함)
│
├── Assets/Icons/             ─ 직업 아이콘 png (런타임에 JobIconAtlas로 로드)
├── Data/
│   ├── game_db.json          ─ 스킬/몬스터/버프 코드 → 이름 매핑 (1.2MB)
│   └── known_skills_catalog.json ─ 알려진 스킬 카탈로그
│
├── Core/                     ─ 인프라 (설정, 핫키, 트레이, Win32)
│   ├── AppSettings.cs        ─ JSON 영속화 + 디바운스 저장 + 원자적 교체
│   ├── HotkeyManager.cs      ─ RegisterHotKey 기반 글로벌 단축키
│   ├── ShortcutSettings.cs   ─ 단축키 문자열 모델
│   ├── TrayManager.cs        ─ NotifyIcon + 컨텍스트 메뉴
│   ├── Win32Native.cs        ─ WS_EX_LAYERED / WS_EX_TRANSPARENT 등
│   └── WindowState.cs        ─ 창 좌표/크기 영속화 모델
│
├── Direct2D/                 ─ 고빈도 렌더링 레이어
│   ├── D2DContext.cs         ─ Direct3D11 디바이스 + DXGI 스왑체인 + D2D RT
│   ├── D2DFontProvider.cs    ─ DirectWrite 폰트 캐시
│   ├── DpsCanvas.cs          ─ 실제 바/숫자/직업 아이콘 그리기 (~10Hz)
│   └── JobIconAtlas.cs       ─ Assets/Icons → ID2D1Bitmap 변환
│
├── Dps/                      ─ DPS 파이프라인
│   ├── IPacketSource.cs      ─ 라이브/리플레이 공통 인터페이스
│   ├── PacketSniffer.cs      ─ Npcap 다중 NIC 스캔 → 첫 게임 패킷 락온
│   ├── PcapReplaySource.cs   ─ a2cap 세션 재생 (realtime/fast/N배속)
│   ├── DpsMeter.cs           ─ 액터/타깃별 누적기, 보스 단위 스냅샷
│   ├── DpsPipeline.cs        ─ 소스↔미터↔캔버스 글루, 세션 종료 감지
│   ├── CombatHistory.cs      ─ 종료된 세션 보관소
│   ├── PartyTracker.cs       ─ 파티원 식별 / CombatPower 누적
│   ├── Models.cs             ─ TcpSegment, CombatHitArgs, MobTarget 등
│   ├── JobMapping.cs         ─ 게임 jobCode ↔ UI archetype 매핑
│   ├── ProtocolPipeline.cs   ─ 패킷 → 이벤트 파싱 어댑터
│   └── Protocol/             ─ 원본 A2Power 프로토콜 코드 포팅 (A2Inspect와 공유)
│       ├── TcpReassembler.cs    ─ 시퀀스 정렬 / 누락 탐지
│       ├── StreamProcessor.cs   ─ varint 프레임 + LZ4 컨테이너 디코딩
│       ├── PacketDispatcher.cs  ─ 태그 매칭 + 이벤트 발행
│       ├── PacketProcessor.cs   ─ Damage/UserInfo/MobSpawn 파서
│       ├── PartyStreamParser.cs ─ 파티 리스트/업데이트/요청/수락 파싱
│       ├── Lz4Decoder.cs        ─ 자체 LZ4 블록 디코더
│       ├── SkillDatabase.cs     ─ game_db.json 로더
│       ├── ServerMap.cs         ─ 서버 ID → 이름
│       ├── ChannelState.cs      ─ 채널 별 디코딩 컨텍스트
│       ├── FlowKey.cs           ─ (src,sp,dst,dp) 키
│       ├── ProtocolUtils.cs     ─ varint, magic-payload 휴리스틱
│       └── NativePacketEngine.cs ─ PacketEngine.dll P/Invoke (선택적 폴백)
│
├── Forms/                    ─ WinForms UI
│   ├── OverlayForm.cs        ─ 프레임리스 토픽모스트 셸
│   ├── OverlayHeaderPanel.cs ─ 잠금/이력/투명도/닫기 버튼
│   ├── DpsDetailForm.cs      ─ 행 클릭 시 스킬 상세 패널
│   ├── CombatHistoryForm.cs  ─ 종료된 세션 목록/회상
│   ├── SettingsForm.cs       ─ 네이티브 설정 패널 (WebView2 대체)
│   └── SecondaryWindows.cs   ─ 보조 창 매니저 (싱글톤 보장)
│
└── Native/PacketEngine.dll   ─ 원본 네이티브 파서 (옵션)
```

---

## 2. 실행 모드

`Program.Main`은 다음 네 가지 모드 중 하나로 분기합니다 ([Program.cs:157-174](Program.cs#L157-L174)).

| 인자 | 동작 |
|---|---|
| (없음) | 라이브 캡처. Npcap 필요. 단일 인스턴스 뮤텍스 잠금. |
| `--replay <dir>` | 지정한 세션 디렉토리의 pcap 파일들을 실시간 페이스로 재생. |
| `--replay <dir> --speed N` | N배속 재생 (1.0=실시간, 4.0=4배 빠르게). |
| `--replay <dir> --fast` | 타이밍 무시하고 최대 속도로 드레인. 단위 테스트용. |
| `--demo` | 더미 데이터로 캔버스 미리보기 (네트워크/Npcap 불필요). |

리플레이 모드에서는 단일 인스턴스 뮤텍스를 건너뛰므로 라이브 인스턴스와 함께 띄울 수 있습니다.

---

## 3. 오버레이 창의 특수 윈도우 스타일

[OverlayForm.cs:93-104](Forms/OverlayForm.cs#L93-L104)에서 다음 확장 스타일을 적용합니다.

| 플래그 | 효과 |
|---|---|
| `WS_EX_LAYERED` | 알파 합성 (`SetLayeredWindowAttributes`로 투명도 조절) |
| `WS_EX_TOOLWINDOW` | Alt+Tab 목록에서 숨김 |
| `WS_EX_NOACTIVATE` | 클릭해도 포커스 강탈하지 않음 (게임 입력 방해 방지) |
| `WS_EX_TOPMOST` | 항상 최상위 |
| `WS_EX_TRANSPARENT` | **잠금(Lock) 모드**에서만 동적 ON — 클릭이 게임 창으로 통과 |

또한 `ShowWithoutActivation = true`, `ShowInTaskbar = false`로 설정되어 게임 플레이를 방해하지 않도록 설계되었습니다.

---

## 4. 패킷 캡처 전략 (다중 NIC 자동 락온)

[PacketSniffer.cs](Dps/PacketSniffer.cs)는 사용자가 어댑터를 선택할 필요가 없도록 설계되었습니다.

1. 모든 IPv4 어댑터를 동시에 promiscuous 모드로 오픈 (loopback 제외)
2. 각각에 BPF 필터 `tcp port 13328` 적용
3. **첫 번째로 페이로드를 가진 TCP 세그먼트가 도착한 어댑터**를 락온
4. 나머지 어댑터는 즉시 닫음
5. 첫 세그먼트도 손실 없이 파이프라인에 전달

이 방식으로 멀티 NIC 환경(VPN, 가상 어댑터, 무선/유선 동시 연결)에서도 사용자가 직접 NIC를 고를 필요가 없습니다.

---

## 5. DPS 세션 의미론

원본 A2Power의 *"하나의 보스 풀 = 하나의 세션"* 의미론을 [DpsPipeline.cs](Dps/DpsPipeline.cs)에서 그대로 보존합니다.

- **세션 시작:** 새 보스 `MobSpawn` (`isBoss=1`, `MaxHp>0`) 수신
- **세션 종료:** 마지막 데미지 이후 `SessionIdleSeconds = 5.0`초 경과
- **종료 후 처리:**
  - 미터를 *즉시 리셋하지 않고* 마지막 스냅샷을 남겨둠 → 사용자가 클릭해서 행 상세 검토 가능
  - 다음 전투 시작 시점에 비로소 리셋 (`OnCombatHit` 내부 분기)
  - 종료된 세션은 `CombatHistory`에 영구 보관 (이력 창에서 회상 가능)

보스 단위로 보고 싶을 때는 `DpsMeter.BuildTargetSnapshot(targetId)`,
파티 전체 합산은 `DpsMeter.BuildCurrentSnapshot()`을 사용합니다.

---

## 6. 설정 영속화

[AppSettings.cs](Core/AppSettings.cs)는 다음 원칙으로 동작합니다.

- **위치:** `%APPDATA%\A2Meter\app_settings.json`, `window_state.json`
- **디바운스 저장:** `SaveDebounced()`는 400ms 윈도우로 연속 호출을 합치므로 슬라이더 드래그 시에도 디스크 부담이 없음
- **원자적 쓰기:** `tmp` 파일에 먼저 쓰고 → `File.Replace`로 교체 → 실패 시 `.bak` 폴백
- **싱글톤:** `AppSettings.Instance`로 어디서든 접근

---

## 7. 단축키 (기본값)

[ShortcutSettings.cs](Core/ShortcutSettings.cs) 기본값으로 다음 단축키가 등록됩니다.
변경은 설정 패널 또는 `app_settings.json`의 `shortcuts` 섹션을 직접 편집하세요.

| 단축키 | 동작 |
|---|---|
| Toggle | 오버레이 표시/숨기기 |
| Refresh | 현재 미터 강제 리셋 |
| Compact | 콤팩트 모드 토글 (TODO) |
| SwitchTab | 탭 전환 (현재 단일 탭) |

전역 핫키는 `RegisterHotKey` Win32 API로 등록되며, 다른 프로세스가 이미 점유한 조합은 무시됩니다.

---

## 8. 빌드/게시

```powershell
# 디버그 빌드
dotnet build A2Meter.csproj -c Debug

# 릴리스 단일 파일 게시
dotnet publish A2Meter.csproj -c Release -r win-x64

# 결과:
#   bin/Release/net8.0-windows/win-x64/publish/A2Meter.exe   (~70MB, 압축됨)
```

`Assets/`, `Data/`, `Native/PacketEngine.dll`은 모두 `<Content>` 항목으로 publish 디렉토리에 자동 복사됩니다 ([A2Meter.csproj:34-45](A2Meter.csproj#L34-L45)).

---

## 9. 트러블슈팅

| 증상 | 원인 / 조치 |
|---|---|
| 실행 직후 종료 | 단일 인스턴스 뮤텍스 충돌. 작업 관리자에서 기존 `A2Meter.exe` 종료. |
| 어떤 패킷도 잡히지 않음 | Npcap 미설치. [npcap.com](https://npcap.com)에서 설치, *WinPcap API-compatible Mode* 체크. |
| 어댑터를 못 찾음 | 관리자 권한 부족. UAC 승격 후 재실행. |
| 게임이 다른 포트로 통신 | `PacketSniffer` 생성 시 `filter` 인자를 `"tcp port 12345"` 등으로 변경. |
| DPS가 표시되지만 본인 닉네임이 `#1234` | UserInfo 패킷 누락. `--replay`로 같은 세션을 a2inspect에 통과시켜 검증. |
| 클릭이 게임에 안 닿음 | 헤더의 자물쇠 버튼으로 잠금(Lock) 모드 활성화 → 클릭 통과. |
