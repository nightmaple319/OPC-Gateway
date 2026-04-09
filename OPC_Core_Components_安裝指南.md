# OPC Core Components 安裝指南

## 問題診斷
您遇到的錯誤 `80040154 類別未登錄` 表示系統中缺少 OPC Core Components。

## 解決方案

### 方法1: 安裝 Matrikon OPC Explorer (推薦)
1. 下載 Matrikon OPC Explorer：
   - 官方網站：https://www.matrikonopc.com/downloads/opc-drivers
   - 或搜索 "Matrikon OPC Explorer free download"

2. 安裝 Matrikon OPC Explorer
   - 這會自動安裝必要的 OPC Core Components
   - 包含 OPC Automation 接口

3. 安裝後重啟電腦

### 方法2: 安裝 OPC Foundation Core Components
1. 下載 OPC Foundation Core Components
2. 以管理員權限安裝
3. 重啟電腦

### 方法3: 使用 KEPServerEX (如果可用)
KEPServerEX 安裝時也會包含必要的 OPC Core Components。

## 驗證安裝

安裝完成後，可以驗證：

1. **檢查註冊表**：
   - 執行 `regedit`
   - 查找 `HKEY_CLASSES_ROOT\OPC.Automation`

2. **使用我們的工具**：
   - 重新啟動 OPCGatewayTool
   - 點擊「掃描伺服器」
   - 應該會看到真實的伺服器列表

## DCOM 設定 (如果需要)

如果安裝後仍有權限問題：

1. 執行 `dcomcnfg.exe` (以管理員權限)
2. 展開 Component Services > Computers > My Computer > DCOM Config
3. 找到您的 OPC 伺服器 (如 Matrikon.OPC.Simulation.1)
4. 右鍵 > Properties > Security
5. 在 Authentication Level 設為 "None"
6. 在 Access Permissions 和 Launch Permissions 中添加 "Everyone" 並給予完整控制

## 測試步驟

1. 確保您的 Matrikon.OPC.Simulation.1 正在運行
2. 以管理員權限啟動 OPCGatewayTool
3. 點擊「掃描伺服器」- 應該會找到真實伺服器
4. 選擇 "Matrikon.OPC.Simulation.1" 並點擊「連接」

現在修復後的版本會：
- 只掃描真實存在的 OPC DA 伺服器
- 提供詳細的錯誤診斷
- 不會顯示任何模擬伺服器
- 給出具體的解決建議

請先安裝 OPC Core Components，然後重新測試！