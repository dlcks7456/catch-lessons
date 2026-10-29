#define MyAppName "CatchLessons"
#ifndef MyAppVersion
#error Define MyAppVersion using scripts/build-windows-installer.ps1
#endif
[Setup]
AppId={{C39D01BF-F322-45D5-93AB-4402D96C828A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CatchLessons
DefaultDirName={localappdata}\Programs\CatchLessons
DefaultGroupName=CatchLessons
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installers
OutputBaseFilename=CatchLessons-Windows-x64-{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\CatchLessons.exe
WizardStyle=modern
WizardImageFile=wizard.bmp
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
[CustomMessages]
english.WhisperTask=Install local Whisper and voice activity models (about 548 MiB)
korean.WhisperTask=로컬 Whisper 및 음성 감지 모델 설치 (약 548 MiB)
english.AgentTitle=Agent prerequisites
korean.AgentTitle=Agent 설치 확인
english.AgentSubtitle=Install and sign in to at least the agent you intend to use.
korean.AgentSubtitle=사용할 Agent를 설치하고 로그인해야 합니다.
english.Found=Executable found; sign-in is still required.
korean.Found=실행 파일이 확인되었습니다. 로그인 여부는 별도로 확인하세요.
english.Missing=Not detected. Install the CLI and sign in before using it.
korean.Missing=감지되지 않았습니다. CLI를 설치하고 로그인하세요.
english.AgentHelp=Codex: developers.openai.com/codex/cli  |  Claude: code.claude.com/docs/en/setup
korean.AgentHelp=Codex: developers.openai.com/codex/cli  |  Claude: code.claude.com/docs/en/setup
english.ApiOnly=Without local Whisper, select OpenAI API and enter an API key in the app. Audio will be sent to OpenAI.
korean.ApiOnly=로컬 Whisper를 설치하지 않으면 앱에서 OpenAI API와 API 키를 설정해야 합니다. 음성은 OpenAI에 전송됩니다.
[Tasks]
Name: "whisper"; Description: "{cm:WhisperTask}"; Flags: checkedonce
[Files]
Source: "..\artifacts\installer-app\*"; DestDir: "{app}"; Excludes: "models\*,*.pdb,whisper-disabled.flag,install-language.txt"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\installer-app\models\ggml-large-v3-turbo-q5_0.bin"; DestDir: "{app}\models"; Tasks: whisper; Flags: ignoreversion
Source: "..\artifacts\installer-app\models\ggml-silero-v6.2.0.bin"; DestDir: "{app}\models"; Tasks: whisper; Flags: ignoreversion
[Icons]
Name: "{group}\CatchLessons"; Filename: "{app}\CatchLessons.exe"; WorkingDir: "{app}"
[UninstallDelete]
Type: files; Name: "{app}\whisper-disabled.flag"
Type: files; Name: "{app}\install-language.txt"
[Run]
Filename: "{app}\CatchLessons.exe"; Description: "Launch CatchLessons"; Flags: nowait postinstall skipifsilent
Filename: "{app}\CatchLessons.exe"; Flags: nowait skipifnotsilent; Check: IsAutomaticUpdate
[Code]
function IsAutomaticUpdate: Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/AUTOMATICUPDATE') = 0 then Result := True;
end;
function HasExistingWhisper: Boolean;
var P: String;
begin
  Result := False;
  P := ExpandConstant('{app}\models\ggml-large-v3-turbo-q5_0.bin');
  if FileExists(P) then if CompareText(GetSHA256OfFile(P), '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2') = 0 then Result := True;
  if Result then Exit;
  P := ExpandConstant('{app}\models\ggml-small-q5_1.bin');
  if FileExists(P) then if CompareText(GetSHA256OfFile(P), 'ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb') = 0 then Result := True;
  if Result then Exit;
  P := ExpandConstant('{app}\models\ggml-large-v3-q5_0.bin');
  if FileExists(P) then if CompareText(GetSHA256OfFile(P), 'd75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1') = 0 then Result := True;
end;
function IsAgentFound(Name: String): Boolean;
var Paths, Part, Candidate: String; P: Integer;
begin
  Result := False;
  Paths := GetEnv('PATH') + ';' + ExpandConstant('{userappdata}\npm') + ';' + AddBackslash(GetEnv('USERPROFILE')) + '.local\bin';
  while Length(Paths) > 0 do begin
    P := Pos(';', Paths);
    if P = 0 then begin Part := Paths; Paths := ''; end else begin Part := Copy(Paths, 1, P-1); Delete(Paths, 1, P); end;
    Candidate := AddBackslash(RemoveQuotes(Trim(Part))) + Name;
    if FileExists(Candidate + '.exe') or FileExists(Candidate + '.cmd') then begin Result := True; exit; end;
  end;
  if Name = 'claude' then Result := FileExists(ExpandConstant('{userappdata}\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe'));
end;
procedure InitializeWizard;
var Page: TWizardPage; L: TNewStaticText; CodeStatus, ClaudeStatus: String;
begin
  Page := CreateCustomPage(wpSelectTasks, CustomMessage('AgentTitle'), CustomMessage('AgentSubtitle'));
  if IsAgentFound('codex') then CodeStatus := CustomMessage('Found') else CodeStatus := CustomMessage('Missing');
  if IsAgentFound('claude') then ClaudeStatus := CustomMessage('Found') else ClaudeStatus := CustomMessage('Missing');
  L := TNewStaticText.Create(Page); L.Parent := Page.Surface; L.Left := 0; L.Top := 10; L.Width := Page.SurfaceWidth; L.Height := 150; L.AutoSize := False; L.WordWrap := True;
  L.Caption := 'Codex: ' + CodeStatus + #13#10#13#10 + 'Claude: ' + ClaudeStatus + #13#10#13#10 + CustomMessage('AgentHelp') + #13#10#13#10 + CustomMessage('ApiOnly');
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Marker, CodeStatus: String;
begin
  if CurStep = ssPostInstall then begin
    if not FileExists(ExpandConstant('{localappdata}\CaptionGlass\settings.json')) then begin
      if ActiveLanguage = 'korean' then CodeStatus := 'ko' else CodeStatus := 'en';
      if not SaveStringToFile(ExpandConstant('{app}\install-language.txt'), CodeStatus, False) then RaiseException('Cannot save installer language.');
    end;
    Marker := ExpandConstant('{app}\whisper-disabled.flag');
    if WizardIsTaskSelected('whisper') or HasExistingWhisper then begin if FileExists(Marker) then DeleteFile(Marker); end
    else if not SaveStringToFile(Marker, 'API-only installation', False) then RaiseException('Cannot save speech engine selection.');
  end;
end;
