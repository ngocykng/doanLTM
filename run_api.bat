@echo off
set PATH=C:\Program Files\dotnet;%PATH%
echo Dang khoi dong BilingualVocabApi (http://localhost:5046)...
dotnet run --project BilingualVocabApi/BilingualVocabApi.csproj --launch-profile http
pause
