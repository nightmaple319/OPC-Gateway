# Claude Code 提示詞：OPC DA to OPC UA Gateway 工具

## 完整提示詞

```
請使用C# WPF創建一個OPC DA to OPC UA Gateway轉換工具，具備以下功能：

## 項目需求

### 1. 核心功能
- 連接到OPC DA伺服器並讀取數據 (需要連接到實際的OPC DA Server而不是用模擬的，在本機已架設好Matrikon的模擬伺服器，所以不用在程式內撰寫Code模擬數值)
- 內建OPC UA伺服器，將OPC DA數據轉換為OPC UA格式，提供OPC Client連線讀取資料(如使用uaexpert等第三方Client能正常讀取)
- 支援多個OPC UA客戶端同時連接
- 即時數據轉換和同步
- 自動重連機制

### 2. 使用者介面 (WPF)
主視窗包含以下區域：
- **連接配置區**：OPC DA伺服器設定、OPC UA伺服器設定
- **狀態監控區**：連接狀態、客戶端數量、數據傳輸統計
- **項目瀏覽區**：樹狀結構顯示OPC DA項目，支援勾選要轉換的項目
- **數據映射區**：顯示OPC DA到OPC UA的項目映射關係
- **即時數據區**：表格顯示即時數據值、品質、時間戳
- **日誌區**：顯示系統運行日誌和錯誤信息
- **功能按鈕區**：啟動/停止、保存配置、匯入/匯出設定

### 3. 技術規格
- **目標框架**：.net framework 4.8 (或根據套件來調整框架版本)
- **OPC DA**：使用COM Interop (OPCAutomation) 或是利用現有套件等 (如TitaniumAS OPC)
- **OPC UA**：使用OPC Foundation的官方NuGet包 (OPCFoundation.NetStandard.Opc.Ua)
- **架構模式**：MVVM模式
- **數據綁定**：支援PropertyChanged通知
- **多執行緒**：UI執行緒與OPC操作執行緒分離

### 4. 詳細功能需求

#### OPC DA客戶端功能
- 自動掃描可用的OPC DA伺服器
- 瀏覽伺服器項目結構（群組和項目）
- 批量訂閱選定的項目
- 處理數據變化事件
- 連接狀態監控和自動重連

#### OPC UA伺服器功能
- 動態創建OPC UA節點結構
- 支援標準OPC UA數據類型映射
- 實現數據變化通知 (Subscriptions)
- 提供伺服器資訊和狀態節點
- 支援安全端點配置

#### 數據轉換功能
- OPC DA品質碼到OPC UA StatusCode轉換
- 自動數據類型映射 (VT_I4 → Int32, VT_R8 → Double等)
- 保持原始時間戳或使用系統時間戳
- 支援自定義命名空間和節點標識符

#### 配置管理
- XML或JSON格式的配置檔案
- 支援多個配置檔案的切換
- 項目映射關係的保存和載入
- 連接參數的持久化存儲

### 5. 項目結構
```
OPCGatewayTool/
├── MainWindow.xaml/MainWindow.xaml.cs     # 主視窗
├── ViewModels/
│   ├── MainViewModel.cs                   # 主視圖模型
│   ├── OPCDAViewModel.cs                  # OPC DA相關視圖模型
│   └── OPCUAViewModel.cs                  # OPC UA相關視圖模型
├── Models/
│   ├── OPCDAItem.cs                       # OPC DA項目模型
│   ├── OPCUANode.cs                       # OPC UA節點模型
│   └── GatewayConfig.cs                   # 配置模型
├── Services/
│   ├── OPCDAService.cs                    # OPC DA服務
│   ├── OPCUAService.cs                    # OPC UA服務
│   └── DataMappingService.cs              # 數據映射服務
├── UserControls/                          # 自定義用戶控件
├── Converters/                            # WPF值轉換器
└── Resources/                             # 資源文件
```

### 6. 具體實現要求

#### 主視窗界面設計
- 使用Grid佈局，分為左中右三欄
- 左欄：OPC DA伺服器連接和項目瀏覽
- 中欄：數據映射和轉換狀態
- 右欄：OPC UA伺服器狀態和客戶端列表
- 底部：狀態列和日誌區域

#### 關鍵功能實現
1. **OPC DA連接管理**
   - 使用後台執行緒處理OPC操作
   - 實現IDisposable模式正確釋放COM資源
   - 提供連接狀態事件通知

2. **OPC UA伺服器實現**
   - 繼承StandardServer類
   - 動態創建NodeManager
   - 實現數據源接口

3. **數據同步機制**
   - 使用Producer-Consumer模式
   - 實現數據佇列處理
   - 確保執行緒安全

4. **錯誤處理**
   - 全域異常處理
   - 詳細的錯誤日誌記錄
   - 用戶友好的錯誤提示

### 7. NuGet套件需求
```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua" Version="1.4.371.60" />
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Server" Version="1.4.371.60" />
<PackageReference Include="Microsoft.Toolkit.Mvvm" Version="7.1.2" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="NLog" Version="5.2.5" />
```

### 8. 測試需求
- 包含與Matrikon OPC Simulation Server的測試代碼
- 提供OPC UA客戶端測試工具
- 包含單元測試項目

請生成完整的可執行項目，包括所有必要的檔案、配置和說明文檔。確保代碼具有良好的註釋和錯誤處理機制。
```

---
請實現功能，並提供完整的架構設計文檔。
```

---

```
"請特別注意以下技術細節：
- COM資源的正確釋放
- 多執行緒的執行緒安全
- WPF數據綁定的效能優化
- OPC UA安全配置的最佳實務"
```
