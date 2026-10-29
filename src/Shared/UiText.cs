using System.Globalization;
namespace CatchLessons;
public static class UiText
{
    public static string Language {get;set;}="en";
    private static readonly Dictionary<string,string> English = new(StringComparer.Ordinal)
    {
        ["설정"]="Settings",["설정 닫기"]="Close settings",["CatchLessons 설정"]="CatchLessons settings",["언어와 입력을 맞추고, 자막에 집중하세요."]="Choose your languages and input, then focus on captions.",
        ["일반"]="General",["앱 버전"]="Version",["업데이트 저장소가 설정되지 않았습니다."]="Update repository is not configured.",["업데이트 확인"]="Check for updates",["업데이트 다운로드"]="Download update",["업데이트 확인 중…"]="Checking for updates…",["새 버전이 있지만 설치 파일을 찾지 못했습니다."]="A newer version is available, but no installer was found.",["새 버전 사용 가능: "]="New version available: ",["최신 버전입니다."]="You are up to date.",["업데이트 확인 실패: "]="Update check failed: ",
        ["연결"]="Connection",["언어"]="Languages",["앱 언어"]="App language",["녹음·저장"]="Recording & storage",["요약"]="Summary",["화면"]="Appearance",["단축키"]="Shortcuts",["닫기"]="Close",["확인"]="OK",
        ["녹음마다 새로운 대화를 만들고, 그 안에서 번역과 요약을 이어갑니다. 선택하면 실제 응답으로 연결을 확인합니다."]="Each recording starts a new conversation for translation and summaries. Selecting an agent checks its connection.",
        ["Agent를 선택하면 연결을 확인합니다."]="Select an agent to check its connection.",["Agent 연결 다시 확인"]="Test agent connection",["실제 Agent 응답 확인 중…"]="Checking agent response…",["Agent 연결 확인 중…"]="Checking agent connections…",["Agent 연결 실패"]="Agent connection failed",["연결 확인 취소"]="Connection check canceled",["연결됨"]="connected",
        ["연결 확인 필요"]="Connection required",["연결 실패"]="connection failed",["설정에서 해당 Agent의 CLI 경로와 로그인을 확인하세요."]="Open Connection settings and check the agent CLI path and sign in.",["연결 설정"]="Connection settings",
        ["모델 · 다음 녹음부터 적용"]="Model · applies to the next recording",["Effort · 낮을수록 속도 우선"]="Effort · lower is faster",["Fast 모드"]="Fast mode",
        ["음성 인식 엔진"]="Speech recognition engine",["로컬 Whisper (무료)"]="Local Whisper (free)",["로컬 음성 모델"]="Local speech model",["CPU만 사용 (GPU 문제 발생 시)"]="Use CPU only",["가속과 품질"]="Acceleration and quality",
        ["첫 시작에 모델을 내려받아 PC에 보관합니다. 음성 인식과 언어 감지에는 API 키나 사용 요금이 없습니다."]="The model is downloaded once and stored on this computer. Local recognition requires no API key.",
        ["GPU 가속을 사용할 수 없으면 CPU로 실행합니다. 느리면 가벼운 모델을 선택하세요."]="If GPU acceleration is unavailable, the app uses the CPU. Choose a smaller model if needed.",
        ["OpenAI API 키"]="OpenAI API key",["선택한 음성은 OpenAI로 전송되며 API 비용이 발생합니다. 키는 현재 Windows 사용자 계정으로 암호화합니다."]="Audio is sent to OpenAI and API charges apply. The key is protected by your OS account.",
        ["Codex 실행 경로"]="Codex executable",["Claude 실행 경로"]="Claude executable",["클릭하여 실행 파일 선택"]="Click to select an executable",
        ["번역 방식·말투·용어 규칙을 설정하세요. 시작 시 세션 지침으로 고정되며 다음 녹음부터 변경값을 적용합니다."]="Set translation style, tone, and terminology. Changes apply to the next recording.",
        ["중지 후 후속 작업"]="Post-recording workflow",["요약 완료 후 같은 Agent가 실행합니다. 설치된 Skill·MCP를 사용하며 기존 권한·승인 정책을 따릅니다. 비워두면 실행하지 않습니다."]="After the summary, the same agent can run this instruction using configured skills and MCP tools. Leave blank to skip.",
        ["원문 언어"]="Source language",["번역 언어"]="Translation language",["자동 감지는 음성 구간마다 언어를 판단합니다. 알고 있는 언어를 고정하면 오인식을 줄일 수 있습니다."]="Detect language automatically or select a known source language.",
        ["실시간 번역에 적용합니다. 요약 언어는 요약 탭에서 별도로 지정합니다. 언어 목록에서 글자를 입력해 해당 항목으로 이동할 수 있습니다."]="Used for live translation. Choose the summary language separately in Summary settings.",
        ["전문용어와 고유명사"]="Terminology and names",["한 줄에 하나씩 입력하세요. 실제 음성에 나오는 용어를 넣으면 인식에 도움이 됩니다."]="Enter one term per line to help recognition.",
        ["시스템 출력 장치"]="System output device",["기본 출력 장치"]="Default output device",["선택한 장치에서 재생되는 소리를 듣습니다. 마이크는 사용하지 않습니다."]="Captures the selected playback device. The microphone is not used.",
        ["오디오 입력 확인 (4초, 전송 없음)"]="Check audio input (4 seconds, local only)",["소리를 재생하고 입력을 확인하세요."]="Play audio and check the input.",["4초 동안 입력 확인 중…"]="Checking audio for 4 seconds…",["오디오가 정상적으로 들어오고 있습니다."]="Audio input detected.",["소리가 감지되지 않았습니다. 재생 장치를 확인하세요."]="No audio detected. Check the output device.",
        ["자막 확정 속도"]="Caption segmentation",["문맥을 더 기다리면 자연스러워질 수 있지만 표시도 늦어집니다."]="Waiting for more context may improve naturalness but increases delay.",["빠르게"]="Fast",["균형 (권장)"]="Balanced (recommended)",["문맥 우선"]="More context",
        ["말하는 중 원문 미리보기"]="Preview speech while speaking",["미리보기"]="Preview",["약 2초부터 원문을 주기적으로 갱신합니다. 확정 전 문장은 바뀔 수 있습니다. PC가 느리면 이 기능을 끌 수 있습니다."]="The preview updates periodically after about 2 seconds and may change before it is finalized.",
        ["저장 폴더"]="Save folder",["저장 폴더 선택"]="Choose save folder",["자막 저장 폴더"]="Caption save folder",["지정한 저장 폴더를 탐색기로 열기"]="Open save folder",["원음도 WAV 파일로 저장"]="Save original audio as WAV",["자동 저장"]="Automatic saving",
        ["원문과 번역은 TXT/SRT로 저장합니다. 녹취 모드는 원문만 저장하며, 요약은 선택한 MD/TXT 형식으로 만듭니다."]="Captions are saved as TXT and SRT. Transcription mode saves only the source. Summaries use the selected format.",
        ["중지 후 요약 실행"]="Summarize after stopping",["요약 완료 후 저장 폴더 열기"]="Open folder after summary",["요약 언어"]="Summary language",["실시간 번역 언어와 별도로 적용합니다. 녹취 모드에서도 사용할 수 있습니다."]="Independent of live translation and available in transcription mode.",
        ["요약 파일 형식"]="Summary format",["일반 텍스트 (.txt)"]="Plain text (.txt)",["카테고리 관리"]="Categories",["카테고리 이름"]="Category name",["요약 프롬프트"]="Summary prompt",["새 카테고리 추가"]="Add category",["선택한 카테고리 삭제"]="Delete category",["새 카테고리"]="New category",
        ["이름과 프롬프트를 편집하면 자동 저장됩니다. 삭제해도 기존 요약 파일은 지우지 않습니다."]="Changes save when you leave the field. Deleting a category keeps existing summaries.",
        ["원하는 구성과 강조할 내용을 작성하세요. 파일명은 Codex가 내용에 맞춰 작성한 제목을 사용합니다."]="Describe the structure and emphasis. The agent chooses a title for the filename.",
        ["테마"]="Theme",["시스템 설정"]="System",["밝은 화면"]="Light",["어두운 화면"]="Dark",["시스템 설정을 따르거나 밝은 화면과 어두운 화면을 선택하세요."]="Follow the system theme or choose light or dark.",
        ["자막에 집중"]="Focus on captions",["마우스를 벗어나면 조작부가 숨겨집니다. 아이콘 위에 마우스를 올리면 기능 설명이 표시됩니다."]="Controls hide when the pointer leaves the window. Hover over icons for their labels.",
        ["창 배치"]="Window position",["제목 영역을 끌어 이동하고 가장자리와 모서리를 끌어 크기를 조절하세요."]="Drag the title area to move and the edges to resize.",
        ["전역 단축키"]="Global shortcuts",["다른 앱을 사용하거나 트레이에 숨겨도 동작합니다. Ctrl, Alt 또는 Win 조합을 입력하세요. Backspace/Delete로 해제합니다. 다른 앱과 충돌한 키는 실행되지 않습니다."]="Work in the background. Use Ctrl, Alt, or Win; press Backspace or Delete to clear a shortcut. Conflicting shortcuts will not run.",
        ["클릭 후 키 조합 입력 · Backspace로 해제"]="Click and press a shortcut · Backspace to clear",["시작 / 중지"]="Start / stop",["설정 열기"]="Open settings",["번역 / 녹취 모드 전환"]="Switch translation / transcription",["요약 켜기 / 끄기"]="Toggle summary",["최신 자막으로"]="Latest caption",["저장 폴더 열기"]="Open save folder",["최소화"]="Minimize",
        ["주제 힌트"]="Topic hint",["번역 모드"]="Translation",["녹취 모드"]="Transcription",["녹음 모드"]="Recording mode",["녹음 시작"]="Start recording",["일시정지"]="Pause recording",["녹음 재개"]="Resume recording",["일시정지 / 재개"]="Pause / resume",["요약 실행"]="Run summary",["녹음 중지 및 요약"]="Stop and summarize",["모델 준비 취소"]="Cancel setup",["요약 취소"]="Cancel summary",["후속 작업 취소"]="Cancel workflow",
        ["백그라운드로 숨기기"]="Hide to tray",["끌어서 창 이동"]="Drag to move",["번역 또는 원문만 녹취"]="Translation or transcription",["원문 언어: 자동 감지 또는 특정 언어"]="Source language: auto-detect or select",["Agent 세션 ID"]="Agent session ID",
        ["시작하면 PC에서 재생되는 소리를 자막으로 표시합니다"]="Start to caption audio playing on this computer",["듣는 언어 그대로,\n이해하는 언어로."]="Listen in any language.\nUnderstand in yours.",
        ["원문 미리보기 · 인식 중 수정될 수 있습니다"]="Source preview · may change during recognition",["자막 글자 크기"]="Caption text size",["창 투명도"]="Window transparency",["창 투명도 (오른쪽일수록 선명하게)"]="Window opacity (higher is less transparent)",["창 불투명도"]="Window opacity",["중지 후 노트 정리 방식"]="Summary category",["요약 다시 만들기"]="Summarize again",["저장 및 요약 처리 중"]="Saving and summarizing",
        ["언어 자동 감지"]="Auto-detect",["없음"]="None",["교육/강의 요약 정리"]="Education / lecture",["회의/업무 정리"]="Meeting / work",["인터뷰/대담 정리"]="Interview",["기술 튜토리얼 정리"]="Technical tutorial",["일반 콘텐츠 요약"]="General content",
        ["자동 감지 대기"]="Waiting for language detection",["원문 언어 고정"]="Source language fixed",["언어 판단 불가"]="Language undetermined",["감지: "]="Detected: ",["세션 "]="Session ",[" (클릭해서 복사)"]=" (click to copy)",["번역 중…"]="Translating…",
        ["번역 미완료 · "]="Translation incomplete · ",["녹취 모드에서는 번역하지 않습니다. 요약 언어는 설정에서 별도로 선택하세요."]="Transcription mode does not translate. Choose the summary language in settings.",
        ["음성 인식 준비"]="Preparing speech recognition",["선택한 음성 엔진과 새 Agent 세션을 준비합니다."]="Preparing the speech engine and a new agent session.",["준비 취소"]="Cancel setup",["모델 준비를 취소했습니다."]="Setup canceled.",["음성 인식 준비가 완료되지 않았습니다"]="Speech recognition setup failed",
        ["녹음 마무리 · 파일 저장 중"]="Finishing recording and saving files",["남은 음성 인식과 번역을 마무리하고 있습니다."]="Finishing remaining transcription and translation.",["중지됨 · 원문과 번역 파일을 저장했습니다"]="Stopped · captions saved",["마무리 중 오류가 발생했습니다"]="An error occurred while finishing",
        ["원문과 선택한 기록을 저장했습니다. 요약은 실행하지 않았습니다."]="Captions saved. Summary was skipped.",["저장 완료"]="Saved",["전체 원문 요약 중"]="Summarizing the full transcript",["요약 저장 완료"]="Summary saved",["요약 저장 완료 · 새 세션 시작 대기 · "]="Summary saved · ready for a new recording · ",["요약 미완료 · "]="Summary incomplete · ",["요약을 완료하지 못했습니다"]="Summary could not be completed",["요약 응답 시간이 초과되었습니다"]="Summary request timed out",["요약이 취소되었습니다"]="Summary canceled",["요약 취소 · 원문과 번역은 저장되어 있습니다"]="Summary canceled · captions are saved",
        ["후속 워크플로우 실행 중"]="Running post-recording workflow",["후속 워크플로우 완료"]="Workflow complete",["후속 워크플로우 미완료"]="Workflow incomplete",["실행 보고서를 저장했습니다."]="The workflow report was saved.",
        ["설정한 요청을 같은 Agent 세션에서 실행합니다. Agent 권한 정책이 적용됩니다."]="Running the instruction in the same agent session under its permission policy.",
        ["설정 읽기 실패: "]="Could not load settings: ",["화면 설정 저장 실패: "]="Could not save appearance: ",["요약 카테고리 저장 실패: "]="Could not save category: ",["종료 오류: "]="Stop error: ",[" · 원문과 번역은 보존됩니다"]=" · captions are preserved",[" · 폴더 열기 실패: "]=" · could not open folder: ",[" · Codex 응답을 기다리고 있습니다."]=" · waiting for Codex.",
        ["작은 창으로, 더 선명하게 이해하세요."]="A small window for clearer understanding.",["계속 들어보세요. 원문과 번역은 PC에 저장됩니다."]="Keep listening. Captions are saved on this computer.",["미리보기 · 실제 음성을 처리하고 있지 않습니다"]="Preview · no audio is being processed",
        ["균형 · large-v3-turbo / 약 548 MB"]="Balanced · large-v3-turbo / ~548 MB",["가볍게 · small / 약 181 MB"]="Light · small / ~181 MB",["품질 우선 · large-v3 / 약 1 GB"]="Quality · large-v3 / ~1 GB",
        ["원문에 근거해 주요 내용을 주제별로 정리하세요."]="Organize the source by topic.",["자동 저장됨"]="Saved automatically",["선택한 카테고리"]="Selected category",
        ["연결 확인 대기"]="Waiting for connection check",["요약 카테고리"]="Summary category",["켬"]="On",["끔"]="Off",["종료"]="Quit",["CatchLessons 열기"]="Open CatchLessons",["CatchLessons · 닫으면 백그라운드에서 계속 실행"]="CatchLessons · continues in the background",
        ["저장 폴더의 전체 경로를 입력하세요."]="Enter an absolute path for the save folder.",["폴더의 전체 경로를 입력하세요."]="Enter an absolute folder path.",["용어는 최대 50개, 각 100자 이하로 입력하세요. < > 문자는 제외해 주세요."]="Enter up to 50 terms of 100 characters each without < or >.",["현재 사용할 수 없는 단축키:\n"]="Unavailable shortcuts:\n",
        ["CLI 실행 파일|*.exe;*.js|모든 파일|*.*"]="CLI executables|*.exe;*.js|All files|*.*",["취소 처리 중… 기존 저장 파일은 유지됩니다."]="Canceling… Existing files are preserved.",["Codex 요약 시간 초과 · ‘요약 다시’로 재시도하세요"]="Summary timed out · use Summarize again to retry",
        ["감지 언어: "]="Detected language: ",["녹음 마무리 중…"]="Finishing recording…",["연결 확인 중…"]="Checking connection…",["요약 다시"]="Summarize again",["음성 인식 및 새 Agent 세션 준비 중…"]="Preparing speech recognition and a new agent session…",["최신 자막"]="Latest caption",["취소 / 확인"]="Cancel / OK",["후속 워크플로우 실행 중…"]="Running post-recording workflow…",["Agent 연결을 확인하세요."]="Check the agent connection.",
        ["내용을 주제별로 정리하세요."]="Organize the content by topic.",["로컬 Whisper"]="Local Whisper",["문맥 대기"]="Caption timing",["선택 카테고리 삭제"]="Delete selected category",["요약 완료 후 폴더 열기"]="Open folder after summary",["요약 형식"]="Summary format",["원문 스트리밍 미리보기"]="Live source preview",["원음 WAV 저장"]="Save original audio as WAV",["음성 모델"]="Speech model",["전문용어 (한 줄에 하나)"]="Terminology (one per line)",
        ["중지 후 후속 작업 (Skill·MCP, 기존 Agent 승인 정책 적용)"]="Post-recording workflow (skills and MCP; agent permissions apply)",["지정한 폴더 열기"]="Open selected folder",["출력 장치"]="Output device",["카테고리"]="Category",["카테고리 추가"]="Add category",["클릭 후 키 조합 입력 · Backspace/Delete로 해제 · 앱 포커스 필요"]="Press a shortcut · Backspace/Delete clears it · app focus required",["화면·키"]="Appearance & shortcuts",["Agent 실제 응답 확인 중…"]="Checking agent response…",["Agent 연결 확인"]="Check agent connection",["CPU만 사용"]="CPU only",["Custom Instruction (선택)"]="Custom instruction (optional)",["Fast 모드 · 사용량 증가 (기본 꺼짐)"]="Fast mode · higher usage (off by default)",["OpenAI API Key (운영체제 보안 저장소)"]="OpenAI API key (OS secure storage)",
        ["음성 인식 준비 중…"]="Preparing speech recognition…",["새 Agent 세션을 여는 중…"]="Opening a new agent session…",["시스템 오디오를 듣고 있습니다"]="Listening to system audio",["녹음 중단"]="Recording stopped",["녹음이 일시정지되었습니다"]="Recording paused",["요약 저장 완료 · 녹음 계속 중 · "]="Summary saved · recording continues · ",["로컬 음성 모델을 메모리에 올리고 있습니다…"]="Loading the local speech model…",["GPU 초기화 실패 · CPU로 다시 준비하고 있습니다…"]="GPU setup failed · preparing CPU mode…",["GPU 음성 엔진 예열 중… 준비가 끝나면 녹음이 시작됩니다."]="Warming up the GPU speech engine… Recording starts when ready.",["OpenAI 음성 인식 연결 중…"]="Connecting to OpenAI speech recognition…",["남은 음성 인식 처리 중… (최대 120초)"]="Finishing transcription… (up to 120 seconds)",["남은 번역 저장 중… (최대 15초)"]="Saving remaining translations… (up to 15 seconds)",
        ["다른 앱에서 사용 중이거나 예약된 키입니다."]="Already in use by another app or reserved by the system.",["Ctrl, Alt 또는 Win 조합이 필요합니다."]="A Ctrl, Alt, or Win combination is required.",["모델과 effort를 선택하세요."]="Select a model and effort level.",["Custom Instruction은 8,000자 이내로 입력하세요."]="Custom instructions must be no longer than 8,000 characters.",["후속 작업 프롬프트는 8,000자 이내로 입력하세요."]="The post-recording instruction must be no longer than 8,000 characters.",
        ["카테고리 이름(1~60자)과 프롬프트(1~12,000자)를 확인하세요."]="Category names must contain 1–60 characters and prompts 1–12,000 characters.",["카테고리 이름이 중복되지 않게 입력하세요."]="Category names must be unique.",["단축키 형식을 확인하세요."]="Check the shortcut format.",["중복된 단축키가 있습니다: "]="Duplicate shortcut: ",["알 수 없는 단축키 보조키: "]="Unknown shortcut modifier: ",["지원하지 않는 단축키: "]="Unsupported shortcut: ",
        ["광둥어 고정 인식에는 균형 또는 품질 우선 모델을 선택하세요."]="Select the balanced or quality model for Cantonese.",["로컬 음성 인식이 중단됐습니다."]="Local speech recognition stopped.",["오디오 장치 연결이 끊겼거나 캡처 버퍼가 넘쳤습니다."]="The audio device disconnected or the capture buffer overflowed.",["로컬 전사가 재생 속도를 따라가지 못합니다. 더 가벼운 모델을 선택하세요."]="Transcription cannot keep up. Choose a smaller local model.",
        ["요약 카테고리가 없음으로 선택되어 있습니다."]="No summary category is selected.",["인식된 원문이 없어 요약을 만들지 않았습니다."]="No transcript was recognized, so no summary was created.",["Agent 응답 확인 실패"]="Agent connection check failed",["Agent 세션을 확인하지 못했습니다."]="Could not confirm the agent session.",["번역 결과가 비어 있거나 중복되었습니다."]="The translation was empty or duplicated.",["번역 문장 일부가 누락되었습니다."]="Some translated captions are missing.",["Agent가 빈 요약을 반환했습니다."]="The agent returned an empty summary.",
        ["다운로드한 모델의 SHA-256을 검증하고 있습니다…"]="Verifying the downloaded model…",["전체 내용 통합 · 요약 파일 작성 중…"]="Combining notes and writing the summary…",["요약 생성 완료 · 파일 저장 중…"]="Summary generated · saving file…",["마지막 음성 구간의 전사 완료를 확인하지 못했습니다."]="The final audio segment could not be confirmed.",["일부 번역이 미완료입니다. 원문은 저장했습니다."]="Some translations are incomplete. The source was saved.",["말소리 구간 검출"]="Voice activity detection",
    };
    public static string T(string text)=>Language=="ko"?text:English.TryGetValue(text,out var value)?value:text;
    public static string Status(string text)
    {
        if(Language=="ko")return text;
        if(English.TryGetValue(text,out var exact))return exact;
        var timeout=System.Text.RegularExpressions.Regex.Match(text,@"^(\w+) 응답 시간 초과 \((\d+)초\)\. 네트워크 또는 Agent 응답 지연입니다\.$");
        if(timeout.Success)return $"{timeout.Groups[1].Value} timed out after {timeout.Groups[2].Value} seconds. Check the connection or agent response.";
        var failed=System.Text.RegularExpressions.Regex.Match(text,@"^(\w+) 실행 실패 \((\d+)\)\. (.*)$");
        if(failed.Success)return $"{failed.Groups[1].Value} failed (exit {failed.Groups[2].Value}). {failed.Groups[3].Value}";
        var segments=System.Text.RegularExpressions.Regex.Match(text,@"^(\d+)개 구간의 번역이 실패했습니다\. 원문과 처리 상태는 저장했습니다\.$");
        if(segments.Success)return $"Translation failed for {segments.Groups[1].Value} segments. The source and status were saved.";
        var missingCli=System.Text.RegularExpressions.Regex.Match(text,@"^(\w+) CLI를 찾지 못했습니다\. 설치·로그인 또는 실행 파일 경로를 확인하세요\.$");
        if(missingCli.Success)return $"{missingCli.Groups[1].Value} CLI not found. Check installation, sign-in, or the executable path.";
        foreach(var pair in new[]{("로컬 모델 확인 중: ","Checking local model: "),("모델 다운로드 ","Downloading model "),("PC에서 원문 인식 중 · 대기 ","Transcribing · queued: "),("전체 원문 정리 중 · 단계 ","Summarizing transcript · stage "),("중복된 단축키가 있습니다: ","Duplicate shortcut: "),("알 수 없는 단축키 보조키: ","Unknown shortcut modifier: "),("지원하지 않는 단축키: ","Unsupported shortcut: ")})
            if(text.StartsWith(pair.Item1,StringComparison.Ordinal))return pair.Item2+text[pair.Item1.Length..].Replace("개 구간"," segments");
        return text;
    }
    public static string Key(string text)=>English.FirstOrDefault(pair=>pair.Value==text).Key??text;
    public static string LanguageLabel(string code,string name)
    {
        if(Language=="ko")return name;
        if(code=="auto")return "Auto-detect";
        try{return CultureInfo.GetCultureInfo(code).EnglishName;}catch(CultureNotFoundException){return code;}
    }
}


