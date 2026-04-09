# OPC DA to OPC UA Gateway

一個將 OPC DA 伺服器數據轉換為 OPC UA 格式的 Windows 應用程式，使用 C# WPF 開發。

## 功能特點

### 核心功能
- ✅ 連接到 OPC DA 伺服器並讀取數據
- ✅ 內建 OPC UA 伺服器，提供 OPC UA 客戶端連接
- ✅ 支援多個 OPC UA 客戶端同時連接
- ✅ 即時數據轉換和同步
- ✅ 自動重連機制

### 使用者介面
- ✅ 直觀的 WPF 界面，分為多個功能區域
- ✅ 連接配置區：OPC DA 和 OPC UA 伺服器設定
- ✅ 狀態監控區：連接狀態、客戶端數量、數據傳輸統計
- ✅ 項目瀏覽區：樹狀結構顯示和選擇 OPC DA 項目
- ✅ 數據映射區：顯示和管理項目映射關係
- ✅ 即時數據區：表格顯示即時數據值、品質、時間戳
- ✅ 日誌區：系統運行日誌和錯誤信息
- ✅ 功能按鈕：啟動/停止、保存/載入配置

### 技術規格
- **目標框架**: .NET Framework 4.8.1
- **OPC DA**: COM Interop (OPCAutomation)
- **OPC UA**: OPC Foundation官方NuGet包
- **架構模式**: MVVM模式
- **數據綁定**: PropertyChanged通知
- **多執行緒**: UI與OPC操作執行緒分離

## 系統需求

### 軟體需求
- Windows 10/11 或 Windows Server 2016+
- .NET Framework 4.8.1
- Visual Studio 2019+ 或 Visual Studio Code

### 硬體需求
- CPU: Intel/AMD x64 處理器
- 記憶體: 4GB RAM (建議 8GB+)
- 硬碟: 100MB 可用空間
- 網路: TCP/IP 連接能力

### OPC 伺服器需求
- 已安裝並配置的 OPC DA 伺服器
- 推薦使用 Matrikon OPC Simulation Server 進行測試

## 安裝步驟

### 1. 克隆專案
```bash
git clone [repository-url]
cd OPC-Gateway
```

### 2. 還原 NuGet 套件
```bash
dotnet restore
```

### 3. 建置專案
```bash
dotnet build --configuration Release
```

### 4. 執行應用程式
```bash
dotnet run
```

或者直接執行編譯後的 exe 檔案。

## 使用說明

### 快速開始

1. **啟動應用程式**
   - 執行 OPCGatewayTool.exe
   - 應用程式會自動創建必要的目錄和配置檔案

2. **配置 OPC DA 連接**
   - 在左側面板中選擇或輸入 OPC DA 伺服器名稱
   - 設定主機名稱 (預設: localhost)
   - 點擊「連接 OPC DA」按鈕

3. **瀏覽和選擇項目**
   - 點擊「瀏覽項目」按鈕載入可用的 OPC DA 項目
   - 勾選要轉換的項目
   - 點擊「添加選中」將項目加入映射

4. **啟動 OPC UA 伺服器**
   - 在右側面板中設定 OPC UA 伺服器參數
   - 點擊「啟動 OPC UA」按鈕
   - 記錄顯示的端點 URL

5. **測試連接**
   - 使用 UaExpert 等 OPC UA 客戶端工具
   - 連接到顯示的端點 URL (預設: opc.tcp://localhost:4840)
   - 瀏覽 Gateway 資料夾中的節點

### 配置管理

#### 載入配置
- 點擊「載入配置」按鈕選擇 JSON 配置檔案
- 應用程式會自動套用配置並重建映射

#### 保存配置
- 點擊「保存配置」按鈕將當前設定儲存為 JSON 檔案
- 包含所有 OPC DA/UA 設定和項目映射

#### 預設配置
應用程式首次啟動時會創建預設配置檔案 `Config/gateway_config.json`，包含常用的 Matrikon 模擬伺服器設定。

### 故障排除

#### 常見問題

1. **OPC DA 連接失敗**
   ```
   錯誤: 連接 OPC DA 伺服器失敗
   解決方案:
   - 確認 OPC DA 伺服器已啟動
   - 檢查伺服器名稱是否正確
   - 確認應用程式以適當權限執行
   - 檢查 Windows 防火牆設定
   ```

2. **OPC UA 伺服器啟動失敗**
   ```
   錯誤: OPC UA 伺服器啟動失敗
   解決方案:
   - 檢查端口是否被其他應用程式佔用
   - 確認應用程式有足夠權限建立伺服器
   - 檢查證書配置是否正確
   ```

3. **項目瀏覽失敗**
   ```
   錯誤: 瀏覽 OPC DA 項目失敗
   解決方案:
   - 確認 OPC DA 連接正常
   - 檢查伺服器是否支援項目瀏覽
   - 重新連接 OPC DA 伺服器
   ```

#### 除錯模式
在 `NLog.config` 中將日誌等級改為 `Debug` 以獲得更詳細的除錯資訊。

## 開發說明

### 專案結構
```
OPCGatewayTool/
├── MainWindow.xaml/MainWindow.xaml.cs     # 主視窗
├── ViewModels/
│   └── MainViewModel.cs                   # 主視圖模型
├── Models/
│   ├── OPCDAItem.cs                       # OPC DA項目模型
│   ├── OPCUANode.cs                       # OPC UA節點模型
│   └── GatewayConfig.cs                   # 配置模型
├── Services/
│   ├── OPCDAService.cs                    # OPC DA服務
│   ├── OPCUAService.cs                    # OPC UA服務
│   ├── DataMappingService.cs              # 數據映射服務
│   ├── ConfigurationService.cs           # 配置管理服務
│   ├── LogService.cs                      # 日誌服務
│   └── ExceptionHandler.cs               # 異常處理服務
├── Config/                                # 配置檔案
├── Logs/                                  # 日誌檔案
└── Resources/                             # 資源文件
```

### 主要技術組件

#### OPC DA 服務 (`OPCDAService.cs`)
- 使用 COM Interop 連接 OPC DA 伺服器
- 實現項目瀏覽、訂閱和數據變化監聽
- 提供自動重連機制
- 正確處理 COM 資源釋放

#### OPC UA 服務 (`OPCUAService.cs`)
- 基於 OPC Foundation 官方函式庫
- 動態創建節點結構
- 支援多客戶端連接
- 實現數據變化通知機制

#### 數據映射服務 (`DataMappingService.cs`)
- 管理 OPC DA 項目到 OPC UA 節點的映射
- 處理數據類型轉換
- 支援映射的啟用/停用
- 提供批量操作功能

#### 配置管理 (`ConfigurationService.cs`)
- JSON 格式的配置檔案
- 支援配置的載入、保存、驗證
- 提供預設配置創建
- 支援配置備份功能

### 開發環境設置

1. **安裝 Visual Studio 2019+**
   - 包含 .NET Framework 4.8.1 開發套件
   - WPF 開發工具

2. **安裝必要的 NuGet 套件**
   ```xml
   <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua" Version="1.4.371.60" />
   <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Server" Version="1.4.371.60" />
   <PackageReference Include="Microsoft.Toolkit.Mvvm" Version="7.1.2" />
   <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
   <PackageReference Include="NLog" Version="5.2.5" />
   ```

3. **設置 OPC DA 開發環境**
   - 註冊 OPC Core Components
   - 安裝 Matrikon OPC Simulation Server (用於測試)

### 編譯和部署

#### 除錯版本
```bash
dotnet build --configuration Debug
```

#### 發行版本
```bash
dotnet publish --configuration Release --self-contained true --runtime win-x64
```

#### 創建安裝程式
可以使用 WiX Toolset 或 Advanced Installer 創建 MSI 安裝程式。

## 授權條款

本專案使用 MIT 授權條款。詳細資訊請參閱 LICENSE 檔案。

## 貢獻指南

1. Fork 本專案
2. 創建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交變更 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 開啟 Pull Request

## 支援與聯絡

如有問題或建議，請通過以下方式聯絡：
- 建立 GitHub Issue
- 發送郵件至 [your-email@example.com]

## 版本歷史

### v1.0.0 (目前版本)
- ✅ 基本的 OPC DA 到 OPC UA 網關功能
- ✅ WPF 使用者介面
- ✅ 配置管理系統
- ✅ 日誌和異常處理
- ✅ 項目瀏覽和映射管理

### 計畫中的功能
- 🔄 OPC UA 安全性增強
- 🔄 效能監控和統計
- 🔄 外掛系統支援
- 🔄 Web 管理介面
- 🔄 資料歷史記錄功能