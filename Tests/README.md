# 회귀 테스트

저장소 루트에서 `dotnet run --project Tests/DLC_PRO.Tests.csproj`를 실행합니다. UI 검증만 실행하려면 뒤에 `-- --ui`를 붙입니다. 종료 코드 0은 전체 성공입니다.

- IntegrationTests / Program: 통신, 명령 순서, 전류 램프 취소, 안전 인터록, 데이터 파싱, 세션 교체 및 레코더 다운로드 회귀 검증.
- DualLaserRig / UiTests: 독립된 두 레이저를 모사하는 루프백 TCP 장비와 UDP 검색 응답을 사용. Avalonia 화면을 실제 Skia 렌더러로 생성하고 경고창, 재연결, 대상별 명령, 전체 OFF, 글꼴, Wide Scan 상태 변경을 검증.
- 앱 설정은 임시 디렉터리로 분리합니다. 실제 사용자 설정과 실제 장비는 사용하지 않습니다.
- 화면 이미지는 `Tests/artifacts/`에 생성됩니다. 이 디렉터리와 빌드 결과는 Git 추적 대상에서 제외합니다.
- 한글 글꼴 테스트는 Windows의 맑은 고딕을 기준으로 합니다.
