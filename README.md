# 문서 탐색기

Windows Forms 기반 로컬 문서 검색 프로그램입니다.

## 현재 배포 방식

- 로컬 배포본은 `실행파일/SearchDoc.exe` 단일 파일입니다.
- git 저장소에는 배포 산출물을 올리지 않고, 소스와 자산만 관리합니다.

## 주요 기능

- 파일명 검색 / 문서 내용 검색
- `docx`, `xlsx`, `pptx`, `pdf`, `txt`, `csv`, `json`, `xml` 등 다양한 형식 지원
- 검색 결과 목록과 문서 미리보기
- 검색어 하이라이트 및 이전/다음 이동
- 최초 1회 색인 및 변경 파일 재색인

## 주요 소스 위치

- 앱 프로젝트: `DocumentExplorerApp`
- 메인 UI: `DocumentExplorerApp/MainForm.cs`
- 문서 검색 로직: `DocumentExplorerApp/DocumentSearcher.cs`
- 미리보기 생성: `DocumentExplorerApp/PreviewDocumentBuilder.cs`
- 색인/색인 팝업: `DocumentExplorerApp/DocumentIndexing.cs`

## 참고

- 실행 파일 속성 메타데이터가 포함되어 있습니다.
- 최종 배포 전에는 `dotnet publish`로 `실행파일_refresh`를 만든 뒤 `실행파일/SearchDoc.exe`로 교체하는 흐름을 사용했습니다.
