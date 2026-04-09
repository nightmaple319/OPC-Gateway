using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OPCGatewayTool.Models;
using NLog;

namespace OPCGatewayTool.Services
{
    public class ConfigurationService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private const string DEFAULT_CONFIG_FILE = "Config/gateway_config.json";
        private const string CONFIG_DIRECTORY = "Config";

        public event EventHandler<GatewayConfig> ConfigurationLoaded;
        public event EventHandler<string> ConfigurationSaved;
        public event EventHandler<string> LogMessage;

        public async Task<GatewayConfig> LoadConfigurationAsync(string filePath = null)
        {
            return await Task.Run(() => LoadConfiguration(filePath));
        }

        public GatewayConfig LoadConfiguration(string filePath = null)
        {
            try
            {
                var configFile = filePath ?? DEFAULT_CONFIG_FILE;
                
                if (!File.Exists(configFile))
                {
                    logger.Info($"配置文件不存在，創建預設配置: {configFile}");
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig, configFile);
                    return defaultConfig;
                }

                logger.Info($"正在載入配置文件: {configFile}");
                var json = File.ReadAllText(configFile);
                var config = JsonConvert.DeserializeObject<GatewayConfig>(json);
                
                if (config == null)
                {
                    logger.Warn("配置文件解析失敗，使用預設配置");
                    config = CreateDefaultConfiguration();
                }
                else
                {
                    // 驗證配置
                    var validationErrors = ValidateConfiguration(config);
                    if (validationErrors.Count > 0)
                    {
                        var errorDetail = string.Join("\n", validationErrors);
                        logger.Warn($"配置驗證有錯誤:\n{errorDetail}");
                        LogMessage?.Invoke(this, $"配置驗證警告: {string.Join("; ", validationErrors)}");
                    }
                    logger.Info("配置文件載入成功");
                }

                ConfigurationLoaded?.Invoke(this, config);
                LogMessage?.Invoke(this, $"配置已載入: {configFile}");
                
                return config;
            }
            catch (JsonException ex)
            {
                logger.Error(ex, "配置文件格式錯誤");
                LogMessage?.Invoke(this, $"配置文件格式錯誤: {ex.Message}");
                return CreateDefaultConfiguration();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "載入配置時發生錯誤");
                LogMessage?.Invoke(this, $"載入配置錯誤: {ex.Message}");
                return CreateDefaultConfiguration();
            }
        }

        public async Task<bool> SaveConfigurationAsync(GatewayConfig config, string filePath = null)
        {
            return await Task.Run(() => SaveConfiguration(config, filePath));
        }

        public bool SaveConfiguration(GatewayConfig config, string filePath = null)
        {
            try
            {
                if (config == null)
                {
                    logger.Error("配置物件為空，無法保存");
                    return false;
                }

                var configFile = filePath ?? DEFAULT_CONFIG_FILE;
                
                // 確保目錄存在
                var directory = Path.GetDirectoryName(configFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    logger.Info($"創建配置目錄: {directory}");
                }

                // 驗證配置
                var validationErrors = ValidateConfiguration(config);
                if (validationErrors.Count > 0)
                {
                    var errorDetail = string.Join("; ", validationErrors);
                    logger.Error($"配置驗證失敗，無法保存: {errorDetail}");
                    LogMessage?.Invoke(this, $"保存失敗 — {errorDetail}");
                    return false;
                }

                // 序列化配置
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                // 寫入文件
                File.WriteAllText(configFile, json);
                
                logger.Info($"配置已保存到: {configFile}");
                ConfigurationSaved?.Invoke(this, configFile);
                LogMessage?.Invoke(this, $"配置已保存: {configFile}");
                
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "保存配置時發生錯誤");
                LogMessage?.Invoke(this, $"保存配置錯誤: {ex.Message}");
                return false;
            }
        }

        public GatewayConfig CreateDefaultConfiguration()
        {
            logger.Info("創建預設配置");
            
            var config = new GatewayConfig
            {
                OPCDAConfig = new OPCDAConfig
                {
                    ServerName = "Matrikon.OPC.Simulation.1",
                    ServerProgId = "Matrikon.OPC.Simulation",
                    HostName = "localhost",
                    UpdateRate = 1000,
                    UseLocalHost = true,
                    ConnectionTimeoutSeconds = 10,
                    ReconnectIntervalSeconds = 30
                },
                OPCUAConfig = new OPCUAConfig
                {
                    ServerName = "OPC Gateway Server",
                    ApplicationName = "OPC DA to UA Gateway",
                    ApplicationUri = "urn:localhost:OPCGateway",
                    Port = 4840,
                    EnableSecurity = false,
                    MaxClients = 100,
                    SessionCheckIntervalMs = 2000
                },
                LoggingConfig = new LoggingConfig
                {
                    LogLevel = "Info",
                    LogFilePath = "Logs/gateway.log",
                    MaxFileSize = 10,
                    MaxFiles = 5
                }
            };

            // 添加一些範例映射
            config.ItemMappings.AddRange(new[]
            {
                new ItemMapping
                {
                    OPCDAItemId = "Random.Int1",
                    OPCUABrowseName = "Random_Int1",
                    OPCUANodeId = "Gateway.Random_Int1",
                    IsEnabled = true
                },
                new ItemMapping
                {
                    OPCDAItemId = "Random.Real4",
                    OPCUABrowseName = "Random_Real4",
                    OPCUANodeId = "Gateway.Random_Real4",
                    IsEnabled = true
                },
                new ItemMapping
                {
                    OPCDAItemId = "Saw-toothed Waves.Int1",
                    OPCUABrowseName = "Sawtooth_Int1",
                    OPCUANodeId = "Gateway.Sawtooth_Int1",
                    IsEnabled = true
                }
            });

            return config;
        }

        public bool ExportConfiguration(GatewayConfig config, string filePath)
        {
            return SaveConfiguration(config, filePath);
        }

        public GatewayConfig ImportConfiguration(string filePath)
        {
            return LoadConfiguration(filePath);
        }

        public bool BackupConfiguration(string originalPath = null)
        {
            try
            {
                var configFile = originalPath ?? DEFAULT_CONFIG_FILE;
                
                if (!File.Exists(configFile))
                {
                    logger.Warn($"要備份的配置文件不存在: {configFile}");
                    return false;
                }

                var backupFile = $"{configFile}.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(configFile, backupFile);
                
                logger.Info($"配置已備份到: {backupFile}");
                LogMessage?.Invoke(this, $"配置已備份: {backupFile}");
                
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "備份配置時發生錯誤");
                LogMessage?.Invoke(this, $"備份配置錯誤: {ex.Message}");
                return false;
            }
        }

        public void EnsureConfigurationDirectory()
        {
            try
            {
                if (!Directory.Exists(CONFIG_DIRECTORY))
                {
                    Directory.CreateDirectory(CONFIG_DIRECTORY);
                    logger.Info($"創建配置目錄: {CONFIG_DIRECTORY}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "創建配置目錄時發生錯誤");
            }
        }

        /// <summary>
        /// 驗證配置並自動修正可修正的欄位。
        /// 回傳驗證錯誤列表（嚴重錯誤），空列表代表通過。
        /// </summary>
        private List<string> ValidateConfiguration(GatewayConfig config)
        {
            var errors = new List<string>();

            if (config == null)
            {
                errors.Add("配置物件為 null");
                return errors;
            }

            // ---- OPC DA ----
            if (config.OPCDAConfig == null)
            {
                errors.Add("OPC DA 配置區段遺失");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.OPCDAConfig.ServerName))
                    errors.Add("OPC DA 伺服器名稱不能為空");

                if (string.IsNullOrWhiteSpace(config.OPCDAConfig.HostName))
                    config.OPCDAConfig.HostName = "localhost";

                if (config.OPCDAConfig.UpdateRate <= 0)
                    config.OPCDAConfig.UpdateRate = 1000;

                if (config.OPCDAConfig.ConnectionTimeoutSeconds <= 0)
                    config.OPCDAConfig.ConnectionTimeoutSeconds = 10;

                if (config.OPCDAConfig.ReconnectIntervalSeconds <= 0)
                    config.OPCDAConfig.ReconnectIntervalSeconds = 30;
            }

            // ---- OPC UA ----
            if (config.OPCUAConfig == null)
            {
                errors.Add("OPC UA 配置區段遺失");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.OPCUAConfig.ServerName))
                    errors.Add("OPC UA 伺服器名稱不能為空");

                if (string.IsNullOrWhiteSpace(config.OPCUAConfig.ApplicationName))
                    config.OPCUAConfig.ApplicationName = "OPC Gateway";

                if (string.IsNullOrWhiteSpace(config.OPCUAConfig.ApplicationUri))
                    config.OPCUAConfig.ApplicationUri = "urn:localhost:OPCGateway";

                if (config.OPCUAConfig.Port <= 0 || config.OPCUAConfig.Port > 65535)
                {
                    logger.Warn($"OPC UA 端口 {config.OPCUAConfig.Port} 無效，已修正為 4840");
                    config.OPCUAConfig.Port = 4840;
                }

                if (config.OPCUAConfig.MaxClients <= 0)
                    config.OPCUAConfig.MaxClients = 100;

                if (config.OPCUAConfig.SessionCheckIntervalMs <= 0)
                    config.OPCUAConfig.SessionCheckIntervalMs = 2000;
            }

            // ---- Logging ----
            if (config.LoggingConfig == null)
                config.LoggingConfig = new LoggingConfig();

            if (string.IsNullOrWhiteSpace(config.LoggingConfig.LogLevel))
                config.LoggingConfig.LogLevel = "Info";

            if (string.IsNullOrWhiteSpace(config.LoggingConfig.LogFilePath))
                config.LoggingConfig.LogFilePath = "Logs/gateway.log";

            // ---- Item Mappings ----
            if (config.ItemMappings == null)
                config.ItemMappings = new System.Collections.Generic.List<ItemMapping>();

            for (int i = config.ItemMappings.Count - 1; i >= 0; i--)
            {
                var mapping = config.ItemMappings[i];
                if (string.IsNullOrWhiteSpace(mapping.OPCDAItemId))
                {
                    logger.Warn($"映射 #{i} 的 OPC DA 項目 ID 為空，已移除");
                    config.ItemMappings.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mapping.OPCUABrowseName))
                    mapping.OPCUABrowseName = mapping.OPCDAItemId.Replace(".", "_");

                if (string.IsNullOrWhiteSpace(mapping.OPCUANodeId))
                    mapping.OPCUANodeId = $"Gateway.{mapping.OPCUABrowseName}";
            }

            if (errors.Count == 0)
                logger.Debug("配置驗證通過");
            else
                logger.Warn($"配置驗證發現 {errors.Count} 個錯誤: {string.Join("; ", errors)}");

            return errors;
        }
    }
}