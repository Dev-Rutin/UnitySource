# Gun Shop Simulator

Unity 6 기반 PC 싱글플레이 물리 시뮬레이터 프로젝트입니다. 플레이어는 양손을 직접 조작해 총기를 조립하고, 완성품을 상점에 진열하고 판매합니다.

## 개발 환경

- Unity: `6000.3.9f1`
- Render Pipeline: Universal Render Pipeline `17.3.0`
- Input: Unity Input System `1.18.0`
- Target: Windows PC, keyboard and mouse

## 소스 구조

- `Assets/GunShop/Runtime`: 게임 런타임 코드 (`Rutin.GunShop`)
- `Assets/GunShop/Tests/EditMode`: 순수 로직 및 에디터 테스트
- `Assets/GunShop/Tests/PlayMode`: 물리 및 런타임 통합 테스트
- `Assets/Scenes`: 플레이 가능한 씬

`Library`, `Temp`, `Logs`, `UserSettings`, IDE 프로젝트 파일은 생성 산출물이므로 Git에서 추적하지 않습니다.

## Git workflow

1. 기능별 GitHub 이슈를 생성합니다.
2. 최신 `develop`에서 기능 브랜치를 만듭니다.
3. Unity EditMode/PlayMode 검증 후 `develop` 대상 PR을 생성합니다.
4. 사용자 리뷰를 반영하고 사용자가 직접 Squash merge합니다.
5. 병합 확인 후 다음 기능을 시작합니다.

기존 `URP_3D_PrototypeWithAI` 프레임워크 프로젝트는 이 프로젝트의 변경 범위에 포함하지 않습니다.
