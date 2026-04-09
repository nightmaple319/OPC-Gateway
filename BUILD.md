# 構建和部署指南

本文檔說明如何構建、測試和部署 OPC DA to OPC UA Gateway 應用程式。

## 構建需求

### 開發環境
- **Visual Studio 2019** 或更新版本
- **.NET Framework 4.8.1** 開發套件
- **Windows SDK** 10.0 或更新版本
- **Git** (用於版本控制)

### 系統需求
- **Windows 10** (版本 1809) 或更新版本
- **Windows Server 2019** 或更新版本
- **管理員權限** (用於 COM 組件註冊)

### 必要的軟體組件
- **OPC Core Components** (OPC Foundation)
- **Matrikon OPC Simulation Server** (用於測試)

## 構建步驟

### 1. 準備開發環境

#### 安裝 Visual Studio
```powershell
# 下載並安裝 Visual Studio Community 2019 或更新版本
# 確保安裝以下工作負載：
# - .NET 桌面開發
# - Windows 應用程式開發
```

#### 安裝 OPC Core Components
```powershell
# 下載並安裝 OPC Core Components Redistributable
# 下載位置: https://opcfoundation.org/developer-tools/developer-kits-classic/core-components/
```

#### 安裝 Matrikon OPC Server
```powershell
# 下載並安裝 Matrikon OPC Simulation Server
# 註冊 DCOM 設定以允許本地訪問
```

### 2. 克隆和配置專案

```bash
# 克隆專案
git clone [repository-url] OPCGateway
cd OPCGateway

# 確認專案結構
dir
```

### 3. 還原 NuGet 套件

```powershell
# 使用 .NET CLI
dotnet restore OPCGatewayTool.csproj

# 或者在 Visual Studio 中
# 右鍵解決方案 -> 還原 NuGet 套件
```

### 4. 構建專案

#### 除錯版本
```powershell
dotnet build OPCGatewayTool.csproj --configuration Debug --verbosity normal
```

#### 發行版本
```powershell
dotnet build OPCGatewayTool.csproj --configuration Release --verbosity normal
```

#### 使用 Visual Studio
```
1. 開啟 OPCGatewayTool.csproj
2. 選擇建置設定 (Debug/Release)
3. 建置 -> 建置方案 (Ctrl+Shift+B)
```

## 測試

### 運行基本測試
```powershell
# 編譯測試專案
dotnet build Tests/BasicTests.cs --configuration Debug

# 運行基本功能測試
dotnet run --project Tests/BasicTests.cs

# 運行集成測試 (需要 OPC 伺服器)
dotnet run --project Tests/BasicTests.cs integration
```

### 手動測試步驟

1. **啟動 Matrikon OPC Simulation Server**
   ```
   - 執行 Matrikon OPC Server for Simulation
   - 確認伺服器狀態為 "Running"
   ```

2. **執行應用程式**
   ```powershell
   # 直接運行
   dotnet run --project OPCGatewayTool.csproj
   
   # 或執行編譯後的 exe
   .\bin\Release\net481\OPCGatewayTool.exe
   ```

3. **測試 OPC DA 連接**
   - 點擊「掃描」按鈕，確認找到 Matrikon.OPC.Simulation.1
   - 點擊「連接 OPC DA」，檢查連接狀態
   - 點擊「瀏覽項目」，確認可以看到模擬項目

4. **測試 OPC UA 伺服器**
   - 點擊「啟動 OPC UA」按鈕
   - 使用 UaExpert 連接到 `opc.tcp://localhost:4840`
   - 確認可以瀏覽到 Gateway 節點

## 部署

### 創建發行版本

```powershell
# 發佈自包含應用程式
dotnet publish OPCGatewayTool.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output ./publish/win-x64

# 發佈依賴框架的應用程式
dotnet publish OPCGatewayTool.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output ./publish/framework-dependent
```

### 手動部署

1. **複製檔案**
   ```powershell
   # 創建部署目錄
   mkdir C:\OPCGateway
   
   # 複製應用程式檔案
   Copy-Item -Path "./publish/win-x64/*" -Destination "C:\OPCGateway\" -Recurse
   ```

2. **配置權限**
   ```powershell
   # 給予應用程式目錄寫入權限
   icacls "C:\OPCGateway" /grant Users:F /T
   ```

3. **註冊 COM 組件** (如果需要)
   ```powershell
   # 以管理員身份執行
   regsvr32 "C:\Program Files\Common Files\OPC Foundation\Core Components\OPCProxy.dll"
   ```

### 創建 Windows 服務 (可選)

1. **安裝 Windows 服務工具**
   ```powershell
   # 使用 SC 命令創建服務
   sc create "OPCGatewayService" `
     binPath= "C:\OPCGateway\OPCGatewayTool.exe" `
     DisplayName= "OPC DA to OPC UA Gateway" `
     Description= "轉換 OPC DA 數據到 OPC UA 格式的網關服務"
   ```

2. **配置服務**
   ```powershell
   # 設定服務為自動啟動
   sc config "OPCGatewayService" start= auto
   
   # 啟動服務
   sc start "OPCGatewayService"
   ```

## 故障排除

### 編譯錯誤

#### 找不到 OPCAutomation 引用
```
錯誤: 找不到類型或命名空間名稱 'OPCAutomation'
解決方案:
1. 安裝 OPC Core Components
2. 註冊 OPC Proxy DLL
3. 重新生成 Interop 組件
```

#### NuGet 套件版本衝突
```
錯誤: 套件版本衝突
解決方案:
1. 清除 NuGet 快取: dotnet nuget locals all --clear
2. 刪除 packages.config 並重新安裝
3. 檢查套件相容性
```

### 運行時錯誤

#### COM 異常 (0x80040154)
```
錯誤: 類別未註冊 (REGDB_E_CLASSNOTREG)
解決方案:
1. 以管理員身份運行應用程式
2. 重新註冊 OPC Core Components
3. 檢查 DCOM 配置
```

#### 權限被拒絕
```
錯誤: Access is denied
解決方案:
1. 以管理員身份運行
2. 配置 DCOM 安全設定
3. 檢查防火牆設定
```

#### 端口被佔用
```
錯誤: Address already in use
解決方案:
1. 更改 OPC UA 端口設定
2. 檢查其他 OPC UA 伺服器
3. 重啟電腦清除端口佔用
```

## 效能調優

### 記憶體優化
```csharp
// 在 App.xaml.cs 中加入
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
```

### CPU 優化
- 調整 OPC DA 更新頻率 (預設 1000ms)
- 減少同時監控的項目數量
- 使用異步操作避免 UI 阻塞

### 網路優化
- 調整 OPC UA 伺服器設定
- 優化數據壓縮設定
- 配置適當的超時值

## 自動化構建

### GitHub Actions 範例
```yaml
name: Build and Test

on: [push, pull_request]

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v2
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: '6.0.x'
        
    - name: Restore dependencies
      run: dotnet restore
      
    - name: Build
      run: dotnet build --no-restore --configuration Release
      
    - name: Test
      run: dotnet test --no-build --configuration Release
```

### 批次檔構建
```batch
@echo off
echo Building OPC Gateway...

REM 清理舊的構建
dotnet clean

REM 還原套件
dotnet restore

REM 構建 Release 版本
dotnet build --configuration Release --verbosity normal

if %ERRORLEVEL% EQU 0 (
    echo Build successful!
    dotnet publish --configuration Release --runtime win-x64 --self-contained true
) else (
    echo Build failed!
    exit /b 1
)

echo Done.
pause
```

## 文檔生成

### API 文檔
```powershell
# 安裝 DocFX
dotnet tool install -g docfx

# 生成文檔
docfx init
docfx build docfx.json --serve
```

### 使用者手冊
使用 Markdown 編寫使用者手冊，並使用工具如 GitBook 或 MkDocs 生成網站。

## 版本管理

### 語義化版本
- 主版本.次版本.修補版本 (例如: 1.2.3)
- 使用 Git 標籤標記版本
- 更新 AssemblyInfo.cs 中的版本資訊

### 發行說明
每個版本都應包含：
- 新功能說明
- 錯誤修復列表
- 已知問題
- 升級指南