# UnitySource

Unity 6 기반 3D 게임 프레임워크 개발 저장소입니다.

## 프로젝트

- Unity: `6000.3.9f1`
- Render Pipeline: Universal Render Pipeline `17.3.0`
- Unity 프로젝트 경로: `URP_3D_PrototypeWithAI`
- 통합 기준 브랜치: `develop`

## Git workflow

1. GitHub 이슈 생성
2. `develop`에서 `feat/<issue-number>-<feature-name>` 브랜치 생성
3. 구현 및 Unity Test Runner 검증
4. `develop` 대상 PR 생성
5. 리뷰 반영 및 테스트 재실행
6. 머지 완료 후 다음 이슈 진행

프레임워크 구조와 성능 기준은
[`Assets/GameFramework/README.md`](URP_3D_PrototypeWithAI/Assets/GameFramework/README.md)를 참고하세요.
