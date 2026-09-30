# DLC_PRO

C# / Avalonia 기반 TOPTICA DLC pro 제어 프로그램 (.NET 10).

## 실행

```powershell
dotnet run --project DLC_PRO.csproj
```

시작 화면은 하드웨어 상태 대시보드입니다. TCP/IP 주소를 입력하거나 **IP 장비 검색**을 이용해 연결합니다. USB는 COM 포트를 선택합니다. 연결 후 감지된 레이저 행을 클릭하면 해당 레이저의 제어 탭이 열립니다. 현재는 한 컨트롤러의 레이저 1·2를 지원합니다.

상단 **AMP OFF / ALL OFF**는 감지된 모든 레이저에 적용됩니다. Laser 페이지 안의 개별 OFF는 선택한 레이저에 적용됩니다. 레이저별 안전 설정과 주파수 변환 설정은 별도 저장하고 통신 설정은 공유합니다.

**장비 보호값은 읽기 전용입니다.** Maximum Current, 공장값, 교정 계수는 변경할 수 없습니다. Console은 단일 조회 명령만 허용하며, Maintenance/Service 권한 상승은 차단됩니다. Settings의 **앱 전류 상한**은 장비 공장값을 바꾸지 않는 소프트웨어 제한입니다. 자세한 범위는 [읽기 전용 보호](Docs/보호값_읽기전용.md)를 참고하세요.

## 검증

```powershell
dotnet build DLC_PRO.csproj -c Release
dotnet run --project Tests/DLC_PRO.Tests.csproj
```

테스트는 로컬 TCP/UDP 시뮬레이터와 Avalonia Headless를 사용합니다. 실제 장비에는 접속하지 않습니다. UI 스크린샷은 `Tests/artifacts/`에 생성됩니다. Windows의 한글 글꼴(맑은 고딕)을 기준으로 표시를 검증했습니다.

## 작업 기록

- [기존 작업 내용](Docs/DLC_PRO_작업내용.html)
- [기존 안전성 검토](Docs/검토_수정_2026-09-29.md)
- [연결 경고·다중 레이저·그래프 및 추가 버그 수정](Docs/업데이트_2026-09-30.md)
