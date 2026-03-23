# handoff

프로젝트: 문서 탐색기

## 현재 상태

- Windows Forms 기반 문서 검색 앱
- 메인 배포본은 로컬 `실행파일/SearchDoc.exe`
- 저장소에는 배포 산출물 제외, 소스/자산만 관리

## 최근 반영 내용

- 검색 결과 / 미리보기 UI 정리
- 검색 결과 상태 아이콘 개선
- 인포 아이콘 및 즉시 툴팁 추가
- DOCX 필드 코드(`PAGEREF` 등) 제외 처리
- 미리보기 포커싱/하이라이트 이동 수정
- 엑셀/표 미리보기 가독성 개선
- 색인 팝업 취소 기능 추가
- 실행 파일 메타데이터 정리

## 핵심 파일

- `DocumentExplorerApp/MainForm.cs`
- `DocumentExplorerApp/DocumentSearcher.cs`
- `DocumentExplorerApp/PreviewDocumentBuilder.cs`
- `DocumentExplorerApp/DocumentIndexing.cs`
- `DocumentExplorerApp/SearchTargetToggle.cs`

## 배포 메모

- 현재는 `SearchDoc.exe` 단일 파일 배포
- 저장소에는 `실행파일/`, `실행파일_refresh/`, `실행파일_single/` 미포함

## 다음 작업 후보

- 남은 nullable warning 정리
- README 보강
- 최종 QA 체크리스트 정리
