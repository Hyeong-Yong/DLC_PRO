# 파장계(HighFinesse WS/6) 연동 — 2026-09-30

DLC_PRO 안에서 HighFinesse **Wavelength Meter WS/6 VisIR (7294)** 을 함께 사용합니다. 메인 창에서는 레이저를 제어합니다. 별도 창인 **WLM LongTerm graph** 에서는 파장 또는 주파수 추세를 동시에 봅니다.

## 통신 방식 (매뉴얼 4.1 External access)

- 통신은 WLM 설치 시 `Windows\System32`에 들어가는 **wlmData.dll**을 통해 이루어집니다. 이 DLL이 실행 중인 WLM 서버 프로그램(`Wavelength Meter WS/6 VisIR`)과 프로세스 간 통신을 합니다. 네트워크나 COM 포트는 쓰지 않습니다.
- 그래서 **WLM 프로그램을 같은 PC에서 켜 둔 상태**로 DLC_PRO를 실행합니다. WLM 프로그램이 꺼져 있으면 **WLM 프로그램 실행** 버튼으로 켤 수 있습니다(ControlWLM).
- DLL은 서버와 버전이 같아야 합니다. 그래서 앱 폴더에 DLL을 복사하지 않고, 설치된 DLL을 그대로 씁니다. `Projects\64\wlmData.dll`도 복사하지 않습니다.
- 측정값은 **CallbackProcEx 콜백**(`cmiWavelength1`, 측정 시각 ms 포함)으로 받습니다. 콜백 설치가 안 되는 환경에서는 자동으로 폴링으로 바꿉니다. 이때 `Instantiate(cInstReturnMode, 1)`를 설정해 같은 값을 중복해서 받지 않게 합니다.
- 64비트 형식은 C 헤더(`wlmData.h`) 기준입니다. `Instantiate`와 `ControlWLM`의 포인터 인자는 `IntPtr`입니다. 동봉된 C# 헤더의 `long P1`/`long App` 선언은 쓰지 않습니다. `bool`은 1바이트입니다.
- DLL 호출은 전용 작업 스레드 하나에서 순서대로 처리합니다. 그래서 UI가 멈추지 않습니다. 콜백은 값을 큐에 넣기만 하고 바로 돌아옵니다. 이는 매뉴얼 CallbackProc 권장사항입니다.

## 화면

### 사이드 메뉴 → Wavemeter

WLM 주 화면의 조작부를 옮긴 페이지입니다. 레이저 연결이나 선택과 관계없이 사용할 수 있습니다.

| WLM 화면 | DLC_PRO | DLL 함수 |
|---|---|---|
| Result unit (vac/air nm, THz, 1/cm, eV) | Result unit | Get/SetResultMode |
| Range 330–1000 / 1000–1750 nm | Range | SetRange(cRangeModelByOrder) 후 0/1 |
| Pulse Continuous / Pulsed | Pulse | Get/SetPulseMode |
| Precision Fine / Wide | Precision | Get/SetWideMode |
| Fast | Fast | Get/SetFastMode |
| Expo.1 [ms], Automatic | Exposure | Get/SetExposureNum, Get/SetExposureModeNum |
| Interval [ms] | Interval | Get/SetInterval, Get/SetIntervalMode |
| Autocalibration, AutoCal each N 단위 | Autocalibration | Get/SetAutoCalMode, Get/SetAutoCalSetting |
| Average Cnt, Floating/Succeeding, Pattern | Average | Get/SetAveragingSettingNum |
| Show signal (간섭 패턴) | Show signal | SetPattern, GetPatternDataNum |
| Start | Start / Stop | Operation(cCtrlStartMeasurement / cCtrlStopAll) |
| T, p, Link, paused | 결과 카드 상단 | GetTemperature, GetPressure, GetLinkState, GetOperationState |

- **Start**를 누르면 측정을 시작하고 **WLM LongTerm graph 창**을 엽니다. 연결 전이면 먼저 연결합니다. 이 동작은 "Start 시 LongTerm graph 창 열기"로 끌 수 있습니다.
- 설정을 바꾸면 WLM 프로그램에도 바로 반영됩니다. WLM이 거부하면(범위 밖 값 등) 빨간 글씨로 사유를 표시하고 실제 값으로 되돌립니다.
- WLM 프로그램에서 직접 바꾼 설정도 0.5초 이내에 반영됩니다. 콜백 알림이 오면 즉시 반영됩니다.
- 측정 오류(Underexposed, Overexposed, No signal 등)는 결과 아래와 상단 바에 표시합니다.
- 상단 상태 바에 **WLM 현재 값**을 표시하므로 어느 페이지에서나 볼 수 있습니다. 클릭하면 Wavemeter 페이지로 이동합니다.
- **연결 해제나 앱 종료는 WLM 측정 상태를 바꾸지 않습니다.** WLM 프로그램은 따로 계속 사용할 수 있습니다.
- Range 이름은 `%AppData%\DLC_PRO\wavemeter.ini`의 `RangeNames=330 - 1000 nm;1000 - 1750 nm`에서 바꿀 수 있습니다.

### WLM LongTerm graph (별도 창)

사이드바나 메인 창을 나눈 형태가 아니라 **독립된 최상위 창**입니다. 소유 창이 없으므로 메인 창 옆이나 다른 모니터에 두고 따로 최소화할 수 있습니다. "항상 위"를 켜면 메인 창 위에 계속 표시됩니다. 창 위치와 크기는 저장합니다. 메인 창을 닫으면 함께 닫힙니다.

HighFinesse LongTerm 프로그램(매뉴얼 3.4)의 기능을 따릅니다.

- **Horiz. axis**: Number / Time, N per page(시간 축은 초). **Fixed**를 켜면 오른쪽 끝에 닿을 때 반 페이지씩 이동하고, 끄면 최근 구간이 계속 흐릅니다.
- **Statistic**: Floating(최근 N개) 또는 리셋 이후 전체. Mean, Std. dev., Minimum, Maximum, Max−Min을 표시하고 σ와 P-V는 MHz로도 표시합니다. **Reset now**로 통계를 다시 시작합니다.
- **Vert. axis Fixed**: Min/Max를 고정합니다. 처음 켤 때는 현재 보이는 범위로 채웁니다.
- 단위: WLM 표시 단위를 따르거나 따로 선택할 수 있습니다. **Δf [MHz]** 는 기준 주파수 대비 차이를 표시합니다(기준 = 첫 측정값 또는 "Δ 기준 = 평균").
- 기록 정지/시작, 지우기, **저장**(탭 구분 `.lta.txt`: 시각, 경과 s, WLM 타임스탬프, 번호, 진공 파장, 주파수, 표시 단위). LongTerm의 .lta처럼 오류 값(예: −3 underexposed)도 그대로 기록합니다.
- 그래프는 배정밀도로 그립니다. float로는 852.3595xx nm의 유효숫자가 부족하므로 `PlotSeries.XD/YD`를 추가했습니다. 드래그/휠 확대, 더블클릭 자동 스케일, PNG/CSV 저장은 기존 그래프와 같습니다.
- 창을 닫아도 기록은 계속됩니다. 최대 1,000,000점까지 기록하고, 넘으면 오래된 점부터 버립니다. 다시 열면 이어서 표시합니다.

## 코드 구조 (MVVM)

| 계층 | 파일 |
|---|---|
| DLL 추상화 | `Core/Wlm/IWlmBackend.cs`, `WlmNativeBackend.cs`(P/Invoke), `WlmSimulatedBackend.cs`, `WlmConst.cs`, `WlmUnits.cs` |
| 서비스 | `Services/WavemeterService.cs`(작업 스레드, 콜백, 상태, 설정 쓰기), `Services/WindowService.cs` + `Interfaces/IWindowService.cs`(별도 창) |
| 모델 | `Models/WavemeterModels.cs`(WlmSample, WlmStatus, WavemeterSettings → wavemeter.ini) |
| ViewModel | `ViewModels/Pages/WavemeterPageViewModel.cs`, `ViewModels/Wavemeter/LongTermViewModel.cs` |
| View | `Views/Pages/WavemeterPageView.axaml`, `Views/LongTermWindow.axaml` |
| 기존 파일 변경 | ApplicationPageNames(+Wavemeter), Bootstrapper(DI 등록), MainViewModel/MainView(메뉴, 상단 WLM 값), App(ShutdownMode, `--wlm-sim`), PlotModel/PlotView(배정밀도, 고정 범위) |

ViewModel은 창 객체를 만들지 않고 `IWindowService.ShowLongTerm()`만 호출합니다. 파일 선택 창은 LongTerm 창을 기준으로 엽니다.

## 실행 옵션

- `--wlm-sim`: 실제 DLL 대신 시뮬레이터를 사용합니다(852.3595 nm 부근, 화면 확인용). 실제 WLM에는 접속하지 않습니다.
- `--page Wavemeter`: 시작 페이지를 지정합니다.
- 페이지의 "앱 시작 시 자동 연결"을 켜면 실행할 때 WLM에 자동으로 연결합니다.

## 검증

- `Tests/WavemeterTests.cs`: `dotnet run --project Tests/DLC_PRO.Tests.csproj -- --ui` 실행 시 UI 테스트 끝에서 수행합니다. 항상 시뮬레이터 백엔드만 사용하므로 테스트 PC에서 실제 WLM 프로그램이 실행 중이어도 설정을 바꾸지 않습니다. 확인 항목은 다음과 같습니다.
  - Start를 누르면 연결, 콜백 설치, 측정 시작, LongTerm 창(소유 창 없음)이 열립니다.
  - 파장, 주파수, 상단 바 표시.
  - 단위, 노출, 범위 쓰기와 거부 시 복원.
  - Underexposed 표시.
  - Δf 표시.
  - TSV 오류 코드 보존.
  - Stop을 눌러도 창이 유지됩니다.
  - 연결을 해제해도 WLM 측정은 유지됩니다.
- 클라우드 검증에서는 기존 회귀 테스트 전체와 WLM 21개, 별도 WLM 기능 검증 52개가 통과했습니다. Windows 전용 맑은 고딕 확인은 제외했습니다.
- **실제 WS/6 장비와 wlmData.dll로는 아직 확인하지 않았습니다.** 처음 실행할 때 다음을 확인해 주세요.
  1. Wavemeter 페이지에서 연결 → 메시지가 `WS/6-7294 · wlmData.dll (콜백)`인지.
  2. Start → WLM 프로그램도 측정 상태로 바뀌는지, LongTerm 창 값이 WLM 화면 값과 같은지.
  3. Range 표시가 WLM 화면의 선택과 같은지(순서 기준 0 = 330–1000 nm).
  4. Show signal 패턴이 WLM 화면의 간섭 패턴과 비슷한지.
