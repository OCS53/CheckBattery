# CheckBattery

Windows 노트북 배터리가 지금 어느 정도인지 재는 콘솔 프로그램입니다.

두 가지를 같이 보여 줍니다.

- **건강도.** 배터리 컨트롤러가 보고한 완충 용량을 설계 용량으로 나눈 값입니다. 셀이 얼마나 남았는지의 기준입니다.
- **환산 용량.** 어댑터를 뺀 채 실제로 흐른 전력(W)을 시간으로 적분해 에너지(Wh)를 구하고, 그 사이 줄어든 잔량 비율로 100%까지 늘립니다. 컨트롤러 숫자와 별도로, 이번 방전에서 나온 용량입니다.

```
환산 용량 = 사용 에너지(Wh) ÷ (시작 잔량% − 종료 잔량%) × 100
건강도 = 완충 용량 ÷ 설계 용량 × 100
```

## 준비

Windows 10 또는 Windows 11, 그리고 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)가 필요합니다. Visual Studio 2026을 쓰면 SDK가 함께 설치됩니다.

## IDE에서 이어서 빌드

저장소 루트의 `CheckBattery.sln` 을 엽니다. Visual Studio, Rider, Cursor, VS Code 모두 이 파일을 사용합니다.

1. 시작 프로젝트는 `CheckBattery` 입니다. 다른 프로젝트가 시작 프로젝트면 `CheckBattery`로 바꿉니다. `CheckBattery.Core` 는 라이브러리라 직접 실행되지 않습니다.
2. 빌드는 `Debug` 또는 `Release`, 플랫폼은 `Any CPU` 입니다.
3. 실행 프로필은 다섯 개입니다. 기본값 `Demo` 는 가상 성적서라 배터리 없이도 동작합니다.

| 프로필 | 동작 |
| --- | --- |
| Demo | 가상 배터리 성적서 |
| Demo worn | 건강도 65% 가상 배터리 |
| Info | 이 노트북의 배터리를 한 번 읽습니다 |
| Monitor | 잔량과 전력을 계속 표시합니다 |
| Test | 30분 방전 시험, 부하 light |

Cursor나 VS Code에서는 C# 확장을 설치한 뒤 디버그 패널에서 같은 프로필을 고릅니다. 빌드 단축키는 솔루션 빌드이고, 테스트 작업은 `dotnet test` 입니다.

처음 열면 솔루션을 복원합니다. 복원이 끝나면 `CheckBattery` 프로젝트를 빌드하면 됩니다.

```bat
dotnet publish src\CheckBattery -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
dist\CheckBattery.exe info
```

SDK만 있고 바로 실행하려면 저장소 루트의 `CheckBattery.cmd` 에 인자를 넘기면 됩니다. 인자가 없으면 Demo 프로필이 실행됩니다.

```bat
CheckBattery.cmd
CheckBattery.cmd info
CheckBattery.cmd test --minutes 45 --load steady
CheckBattery.cmd help
```

배터리를 읽는 명령은 노트북의 Windows에서 동작합니다. `demo` 는 가상 데이터로 성적서 모양만 만들므로 다른 운영체제에서도 동작합니다.

## 사용법

```text
CheckBattery info
CheckBattery monitor --interval 2 --output monitor.csv
CheckBattery test --minutes 30 --interval 5 --floor 15 --load light --output reports
CheckBattery demo
CheckBattery demo --worn
```

| 명령 | 하는 일 |
| --- | --- |
| `info` | 설계 용량, 완충 용량, 건강도, 사이클, 지금 전압과 전력을 한 번 읽습니다. |
| `monitor` | 잔량과 충방전 전력을 주기적으로 출력합니다. `Ctrl+C` 로 끝냅니다. |
| `test` | 방전 시험입니다. 어댑터가 연결된 상태에서는 시작하지 않습니다. |
| `demo` | 설계 72Wh 배터리가 약 22W로 40분 방전된 가상 성적서입니다. `--worn` 은 건강도 65% 예시입니다. |

`test` 기본값은 30분, 5초 간격, 잔량 15%에서 중단, 부하 `light` 입니다.

부하는 세 가지입니다.

- `none` — 추가 부하 없이, 시험 동안 노트북을 쓰는 그대로를 잽니다.
- `light` — 스레드 하나가 약 40%만 일합니다. 절전으로 전력이 출렁이는 것을 줄입니다.
- `steady` — 전체 코어의 절반을 계속 사용합니다. 같은 조건으로 다시 재기 위한 기준이며, 일상 사용보다 빨리 닳고 따뜻해질 수 있습니다.

잔량 중단 값(`--floor`)은 10에서 50 사이입니다. 10%보다 아래로 내리지는 않습니다.

## 성적서

`test` 와 `demo` 는 `reports` 폴더에 텍스트, HTML, CSV를 저장합니다.

- **건강도 90% 이상** 최상, **80% 이상** 양호, **70% 이상** 보통, **60% 이상** 주의, 그 아래는 교체 권장입니다.
- **센서/게이지** 는 전력 센서로 적산한 에너지를 같은 구간의 잔량 감소로 나눈 값입니다. 1에 가까우면 두 측정이 맞습니다.
- **예상 사용 시간** 은 이번 시험의 평균 전력이 완충에서 방전 끝까지 유지된다고 본 값입니다. 화면을 켠 채 재므로 화면 전력도 포함됩니다.

잔량이 5퍼센트포인트보다 적게 줄면 환산 용량은 비워 둡니다. 15퍼센트포인트보다 짧은 방전은 오차가 크다고 성적서에 적습니다.

전력 센서가 없는 기기는 잔량 감소만으로 에너지를 계산하고, 그 경우는 게이지와 독립된 확인이 아니라고 적습니다.

## 시험할 때

1. 완충에 가깝게 충전한 뒤 어댑터를 분리합니다.
2. 밝기와 전원 모드를 재려는 사용 조건에 맞춥니다. `steady` 는 비교용 고정 부하입니다.
3. 노트북을 딱딱하고 통풍이 되는 곳에 둡니다.
4. `CheckBattery test --minutes 60 --load steady` 처럼 잔량이 15퍼센트포인트 이상 줄 시간을 줍니다.
5. 뜨거워지면 `Ctrl+C` 로 끊습니다. 그때까지 기록으로 성적서를 저장합니다.

어댑터를 다시 꽂으면 그 구간은 방전량에서 빼고 측정을 잠시 멈춥니다. 분리하면 이어서 잽니다. 절전으로 보이는 5분 넘는 간격도 적분에서 뺍니다.

## 값의 한계

용량과 전력은 `root\wmi` 의 `BatteryStatus`, `BatteryFullChargedCapacity`, `BatteryStaticData`, `BatteryCycleCount` 와 `Win32_Battery` 에서 읽습니다. 설계 용량, 완충 용량, 잔량, 전압, 충방전 속도는 밀리 단위를 와트시·와트·볼트로 바꿉니다.

제조사에 따라 전력 센서가 0으로만 나오거나 사이클 수가 없을 수 있습니다. 건강도는 컨트롤러가 학습한 완충 용량이라, 긴 방전으로 환산한 용량과 다를 수 있습니다. 짧은 시험일수록 그 차이가 커집니다.

## 개발

대상 프레임워크는 `net10.0` 입니다. `global.json` 은 .NET 10 이상의 안정 SDK를 고릅니다. 계산과 성적서 문장은 Windows API와 분리되어 있습니다. 저장소 루트에서 다음으로 확인합니다.

```bash
dotnet test
dotnet run --project src/CheckBattery -- demo
dotnet run --project src/CheckBattery -- demo --worn
```
