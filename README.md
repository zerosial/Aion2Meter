# A2Meter

**아이온2(Aion 2) DPS 미터 오버레이**

A2Meter는 아이온2 게임 클라이언트가 서버와 주고받는 TCP 패킷을 NIC 단에서 스니핑하여 실시간으로 DPS(초당 데미지)를 계산해 화면 상단에 오버레이로 표시하는 도구입니다. 게임 프로세스에 일절 인젝션하지 않으며, 오로지 네트워크 패킷만 관찰합니다.

본 솔루션은 원본 A2Viewer/A2Power 프로젝트의 핵심 프로토콜 스택을 C#으로 포팅하고, 오버레이 UI를 WebView2 기반에서 **순수 네이티브(WinForms + Direct2D)** 로 재작성한 결과물입니다.

---

## 빠른 시작 & 사용법

### 1. 모드별 실행 가이드
GitHub Releases에서 제공하는 압축 파일 안에는 두 가지 실행 파일이 포함되어 있습니다. 목적에 맞게 사용하세요.

* **`A2Meter.exe` (일반 모드)**
  * **대상**: 일반 플레이어
  * **기능**: 현재 `game_db.json`에 등록된 보스와 던전에 대해서만 실시간 DPS 측정 및 통계를 제공합니다.
  * **사용법**: `A2Meter.exe`를 더블클릭하여 실행 후 아이온2를 플레이하면 자동으로 패킷을 감지합니다.

* **`A2Meter_Admin.exe` (관리자 모드)**
  * **대상**: 미터기 데이터 기여자 및 관리자
  * **특화 기능**: 일반 모드의 모든 기능 + **신규 보스 강제 기록 시스템 (Force Record)**
  * **사용법**:
    1. `A2Meter_Admin.exe`를 실행합니다. (일반 유저에게는 보이지 않는 상단 **`[REC]`** 버튼이 나타납니다)
    2. 미터기에 아직 등록되지 않은 신규 인던 보스 조우 시, `Ctrl + Alt + R` 단축키를 누르거나 `[REC]` 버튼을 클릭하여 **`[REC(On)]`** 상태로 전환합니다.
    3. 몬스터를 타격하면 엔진이 해당 대상을 즉시 보스로 강제 승격시켜 전투 세션을 기록합니다.
    4. 동시에 `Data/game_db.json`에 `[강제등록] 몹코드_XXXX` 형태로 자동 주입하여, 추후 버전 업데이트 시 쉽게 DB를 갱신할 수 있도록 해줍니다.

### 2. 사전 요구사항

- **Windows 10/11 x64**
- **[Npcap](https://npcap.com)** (설치 시 *"Install Npcap in WinPcap API-compatible Mode"* 체크 권장)
- .NET 런타임은 단일 실행 파일 내에 포함되어 있으므로 별도 설치가 필요 없습니다.

### 3. 오프라인 및 고급 실행 옵션

```powershell
A2Meter.exe                              # 라이브 캡처 모드 (기본 실행)
A2Meter.exe --replay <세션디렉토리>      # 오프라인 재생 (실시간 페이스)
A2Meter.exe --replay <디렉토리> --speed 4  # 4배속 재생
A2Meter.exe --replay <디렉토리> --fast    # 최대 속도 재생
A2Meter.exe --demo                        # 더미 데이터 미리보기
```

오버레이는 기본적으로 **단일 인스턴스 뮤텍스**로 보호되며 (`A2Meter.SingleInstance.Mutex`),
`--replay` 모드는 이 제한이 풀려 라이브 인스턴스와 함께 띄울 수 있습니다.

---

## 솔루션 구성

본 저장소는 다음 세 개의 .NET 8 프로젝트로 구성됩니다.

| 프로젝트 | 종류 | 역할 |
|---|---|---|
| [src/A2Meter/](src/A2Meter/) | WinExe (`A2Meter.exe`) | 실시간 DPS 오버레이 본체. Direct2D로 렌더링, Npcap으로 캡처. |
| [src/A2Capture/](src/A2Capture/) | Console (`a2cap.exe`) | 회귀 테스트용 패킷 캡처 도구. `.pcapng` + 매니페스트로 세션 저장. |
| [src/A2Inspect/](src/A2Inspect/) | Console (`a2inspect.exe`) | 캡처된 세션을 오프라인에서 파서에 통과시켜 진단/검증. |

세 프로젝트 모두 `x64` 플랫폼, `net8.0` 타깃입니다 (메인 앱만 `net8.0-windows`).
프로토콜 코드는 [src/A2Meter/Dps/Protocol/](src/A2Meter/Dps/Protocol/)에 한 벌만 존재하며, A2Inspect는 `<Compile Include="..\A2Meter\..." Link="..." />` 방식으로 동일한 소스를 빌드 시점에 공유합니다.

---

## 데이터 흐름

```
 [아이온2 클라이언트]
        │
        │  TCP (서버 ↔ 클라이언트, 기본 :13328)
        ▼
 ┌─────────────────────────────────────────────────────┐
 │            Npcap / WinPcap (커널 레벨)             │
 └─────────────────────────────────────────────────────┘
        │
        │  raw segments
        ▼
 ┌──────────────┐                ┌─────────────────┐
 │ PacketSniffer │  또는  ⇢  ⇢  │ PcapReplaySource │   (--replay 모드)
 │  (다중 NIC    │                │  (a2cap 세션 재생)│
 │   자동 락온)  │                └─────────────────┘
 └──────────────┘
        │
        ▼
   TcpReassembler  →  StreamProcessor  →  PacketDispatcher
   (시퀀스 정렬)      (varint 프레임/LZ4)    (태그 기반 디스패치)
        │
        │  Damage / MobSpawn / UserInfo / Buff / BossHp ...
        ▼
   DpsMeter  ──  PartyTracker  ──  CombatHistory
        │
        ▼
   DpsPipeline (10Hz 푸시)
        │
        ▼
   DpsCanvas (Direct2D)  +  OverlayHeaderPanel (WinForms)
```

---

## 배포 및 자동화 (Release & Automation)

본 저장소에는 빌드 및 GitHub Releases 업로드를 단순화하기 위한 두 가지 자동화 파이프라인이 구성되어 있습니다.

### 1) GitHub Actions 클라우드 자동 배포 (추천)
버전 이름이 명시된 **Git 태그를 원격 저장소에 푸시**하기만 하면, GitHub 클라우드 러너가 자동으로 빌드, ZIP 압축 및 GitHub Release 배포를 수행합니다.

#### ⚠️ 필수 사전 설정 (.env 보안 주입)
메인 어플리케이션은 컴파일 타임에 프로젝트 루트의 `.env` 환경 설정 파일을 임베디드 리소스로 주입합니다. `.env` 파일은 보안상 저장소에 커밋되지 않으므로, Actions가 이를 참조할 수 있도록 **최초 1회** 등록해주셔야 합니다.
1. 포크한 GitHub 저장소의 **`Settings`** ➡️ **`Secrets and variables`** ➡️ **`Actions`** 메뉴로 이동합니다.
2. **`New repository secret`**을 클릭합니다.
3. Name에 `ENV_FILE`을 입력하고, Value에 로컬의 `.env` 내용 전체를 복사해 붙여넣고 저장합니다.

#### 🚀 배포 실행 명령어
버전을 정의하고, `v`로 시작하는 버전 태그(예: `v1.0.3-beta`, `v1.0.3-beta+server` 등)를 생성해 푸시합니다:
```bash
# 1. 로컬에 릴리즈할 새 버전에 대한 로컬 태그 생성
git tag v1.0.3-beta+server

# 2. 생성한 태그를 본인의 원격 저장소(origin)로 푸시
git push origin v1.0.3-beta+server
```
> **팁**: 태그가 원격에 정상 푸시되면, GitHub 저장소의 **Actions** 탭에서 실시간 빌드 과정을 확인할 수 있으며, 완료 시 **Releases** 페이지에 자동으로 빌드 본 ZIP 파일이 업로드됩니다.

---

### 2) 로컬 PowerShell 원클릭 배포 스크립트
로컬에서 즉시 빌드하여 GitHub에 초안(Draft) Release로 배포하고 싶을 때 사용합니다.
1. [GitHub CLI (gh)](https://cli.github.com/)가 로컬에 설치되어 있고 로그인(`gh auth login`)된 상태여야 합니다.
2. 프로젝트 루트 경로에서 아래 스크립트를 원클릭으로 실행합니다:
   ```powershell
   .\publish.ps1
   ```
   *(이 스크립트는 로컬의 실시간 `.env` 파일을 탑재해 빌드한 후, 자동으로 `publish/` 폴더 내에 ZIP 파일 압축 및 GitHub 초안 Release 생성을 완료해 줍니다.)*

---

## 주요 기능

| 기능 | 위치 |
|---|---|
| 다중 NIC 자동 락온(첫 게임 패킷 수신 어댑터 선택) | [PacketSniffer.cs](src/A2Meter/Dps/PacketSniffer.cs) |
| LZ4 디컴프레션 + varint 프레임 디코딩 | [Protocol/StreamProcessor.cs](src/A2Meter/Dps/Protocol/StreamProcessor.cs) |
| 태그 기반 패킷 디스패치 (Damage/MobSpawn/UserInfo 등) | [Protocol/PacketDispatcher.cs](src/A2Meter/Dps/Protocol/PacketDispatcher.cs) |
| 보스 단위 세션 분리 (원본 A2Power의 "1풀 = 1세션" 의미론) | [DpsPipeline.cs](src/A2Meter/Dps/DpsPipeline.cs) |
| 파티 멤버 추적 / 본인 식별 | [PartyTracker.cs](src/A2Meter/Dps/PartyTracker.cs), [Protocol/PartyStreamParser.cs](src/A2Meter/Dps/Protocol/PartyStreamParser.cs) |
| 글로벌 핫키 (RegisterHotKey) | [Core/HotkeyManager.cs](src/A2Meter/Core/HotkeyManager.cs) |
| 시스템 트레이 메뉴 | [Core/TrayManager.cs](src/A2Meter/Core/TrayManager.cs) |
| 클릭 통과(Lock) / 토픽모스트 / 레이어드 윈도우 | [Forms/OverlayForm.cs](src/A2Meter/Forms/OverlayForm.cs) |
| 전투 이력 보존/조회 | [Dps/CombatHistory.cs](src/A2Meter/Dps/CombatHistory.cs), [Forms/CombatHistoryForm.cs](src/A2Meter/Forms/CombatHistoryForm.cs) |
| 네이티브 PacketEngine.dll 폴백 (선택사항) | [Protocol/NativePacketEngine.cs](src/A2Meter/Dps/Protocol/NativePacketEngine.cs) |

---

## 회귀 테스트 워크플로

라이브 게임 없이 파서 변경을 검증하려면 다음 흐름을 사용합니다.

```powershell
# 1) 인게임에서 한 번만 캡처
a2cap.exe                                 # Ctrl-C로 중단
# → ./captures/<세션ID>/capture-*.pcapng + manifest.json

# 2) 캡처 진단 (포트/플로우/이벤트 카운트 검증)
a2inspect.exe ./captures/<세션ID>

# 3) 동일 세션을 오버레이에서 재생
A2Meter.exe --replay ./captures/<세션ID> --fast
```

자세한 사용법은 각 프로젝트의 README를 참조하세요.

- [src/A2Meter/README.md](src/A2Meter/README.md)
- [src/A2Capture/README.md](src/A2Capture/README.md)
- [src/A2Inspect/README.md](src/A2Inspect/README.md)

---

## 설정 파일 위치

`%APPDATA%\A2Meter\` 아래에 다음 파일들이 자동 생성됩니다.

| 파일 | 내용 |
|---|---|
| `app_settings.json` | 투명도, 테마, 단축키, 패널 크기, 동의 버전 등 |
| `window_state.json` | 오버레이 창의 좌표/크기 (해상도/모니터 변경 시 자동 보정) |
| `*.bak` | 원자적 쓰기 실패 시 복구용 백업본 |

쓰기는 [AppSettings.SaveDebounced](src/A2Meter/Core/AppSettings.cs)로 약 400ms 디바운스되며,
크래시 안전을 위해 `tmp → File.Replace → bak` 순서로 원자적 교체됩니다.

---

## 라이선스 / 책임

본 도구는 게임 클라이언트나 서버에 어떠한 변조도 가하지 않으며,
오로지 사용자 PC의 NIC를 통과하는 패킷만을 관찰합니다.
사용으로 인한 약관 위반/계정 제재 등 모든 책임은 사용자에게 있습니다.
