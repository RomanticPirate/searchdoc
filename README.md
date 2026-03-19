# 문서 탐색기

실행 파일 위치:

- `DocumentExplorerApp\bin\Release\net8.0-windows\publish\DocumentExplorerApp.exe`

설명:

- 검은 콘솔 창 없이 실행되는 Windows Forms 기반 문서 본문 검색기였어.
- 마지막으로 선택한 검색 폴더를 `document_search_settings.json`으로 저장해.
- 오른쪽 미리보기에서 검색어를 검은 배경, 흰 글자로 하이라이트해.

지원 형식:

- `docx`, `xlsx`, `pptx`, `hwpx`: 앱이 직접 읽어.
- `doc`, `xls`, `ppt`: Microsoft Office가 설치되어 있으면 읽어.
- `hwp`: 한글(HWP)이 설치되어 있으면 읽어.

소스 위치:

- `DocumentExplorerApp`
