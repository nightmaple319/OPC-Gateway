# 建置與部署

## 開發環境

- Windows 10 / 11
- .NET SDK 8 或更新（`dotnet --version`）；建置目標仍是 .NET Framework 4.8
- Visual Studio 2022（.NET 桌面開發工作負載）或 VS Code + C# Dev Kit
- OPC Core Components Redistributable（32 位元），供 OPC DA 列舉與 DCOM marshalling
- Matrikon OPC Simulation Server，供整合測試與手動驗證

## 建置

```bash
dotnet restore OPCGatewayTool.sln
dotnet build OPCGatewayTool.sln -c Release
```

輸出：`src/OPCGateway.App/bin/Release/net48/`

## 測試

```bash
# 單元測試（不需任何外部服務）
dotnet test tests/OPCGateway.Core.Tests/OPCGateway.Core.Tests.csproj -c Release

# 整合測試：需要本機 Matrikon 與 32 位元 OPC Core Components
set OPCGW_INTEGRATION=1
dotnet test tests/OPCGateway.Core.Tests/OPCGateway.Core.Tests.csproj -c Release --filter "FullyQualifiedName~IntegrationTests"
```

整合測試會在連接埠 48400 與 48401 各啟動一個 OPC UA 伺服器並以 UA 客戶端連入讀值，也會在 `%ProgramData%\OPC Foundation\CertificateStores` 建立測試用的應用程式憑證。第一次執行時 Windows 防火牆可能對 testhost 跳出提示；本機 loopback 連線不受影響，可自行決定是否允許。

## 部署

目標機器需要：

1. .NET Framework 4.8（Windows 10 1903 以上內建；LTSC 2019 需另外安裝離線版）
2. OPC Core Components Redistributable（32 位元）
3. 防火牆放行 OPC UA 連接埠（預設 4840 TCP）
4. 若 OPC DA 伺服器在遠端，需完成 DCOM 設定（兩端的 DCOM 權限與相同帳號）

把 `bin/Release/net48/` 整個資料夾複製到目標機器即可執行；`Config/` 與 `Logs/` 會建立在執行檔旁邊。建議用 Inno Setup 或 WiX 製作安裝程式，把上述前置需求的檢查與安裝一併處理。

程式為 x86，必須以 32 位元執行；請勿改成 AnyCPU 或 x64，除非目標機器安裝了 64 位元 OPC Core Components 且 OPC DA 伺服器支援。

## 版本

版本號在 `Directory.Build.props` 的 `<Version>`，會寫進組件資訊與 UA 的 `Gateway/Status/GatewayVersion` 節點。
