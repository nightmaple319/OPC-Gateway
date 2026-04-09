using System;
using System.Windows;
using System.Windows.Controls;
using OPCGatewayTool.ViewModels;
using OPCGatewayTool.Models;
using NLog;

namespace OPCGatewayTool
{
    public partial class MainWindow : Window
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private MainViewModel _viewModel;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _viewModel = new MainViewModel();
                DataContext = _viewModel;
                
                logger.Info("主窗口初始化完成");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "主窗口初始化失敗");
                MessageBox.Show($"初始化失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                logger.Info("主窗口正在關閉");
                _viewModel?.Dispose();
                base.OnClosed(e);
                logger.Info("主窗口已關閉");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "關閉主窗口時發生錯誤");
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                bool hasActiveConnections = _viewModel?.IsOPCDAConnected == true || _viewModel?.IsOPCUARunning == true;
                
                if (hasActiveConnections)
                {
                    var result = MessageBox.Show(
                        "OPC服務正在運行中，程式將自動停止所有連線後關閉。\n\n確定要關閉應用程式嗎？", 
                        "確認關閉", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question,
                        MessageBoxResult.No);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }
                    
                    // 使用者確認關閉，開始清理所有連線
                    logger.Info("使用者確認關閉應用程式，開始清理所有連線");
                    CleanupAllConnections();
                }
                else
                {
                    logger.Info("無活動連線，正常關閉應用程式");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "處理窗口關閉事件時發生錯誤");
            }
        }

        private void CleanupAllConnections()
        {
            try
            {
                logger.Info("開始清理所有OPC連線...");
                
                // 停止OPC UA伺服器
                if (_viewModel?.IsOPCUARunning == true)
                {
                    logger.Info("正在停止OPC UA伺服器...");
                    try
                    {
                        _viewModel.StopOPCUACommand?.Execute(null);
                        logger.Info("OPC UA伺服器已停止");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "停止OPC UA伺服器時發生錯誤");
                    }
                }
                
                // 斷開OPC DA連線
                if (_viewModel?.IsOPCDAConnected == true)
                {
                    logger.Info("正在斷開OPC DA連線...");
                    try
                    {
                        _viewModel.DisconnectOPCDACommand?.Execute(null);
                        logger.Info("OPC DA連線已斷開");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "斷開OPC DA連線時發生錯誤");
                    }
                }
                
                // 等待一小段時間確保清理完成
                System.Threading.Thread.Sleep(500);
                logger.Info("連線清理完成");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "清理連線時發生錯誤");
            }
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is OPCTreeNode node)
                {
                    logger.Debug($"展開樹節點: {node.FullPath}, 已載入: {node.IsLoaded}");
                    
                    // 標記事件已處理，防止事件冒泡導致重複處理
                    e.Handled = true;
                    
                    if (!node.IsLoaded && node.HasChildren)
                    {
                        _viewModel?.LoadChildNodes(node);
                    }
                    else
                    {
                        logger.Debug($"節點 {node.FullPath} 已載入或無子項，跳過載入");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "展開樹節點時發生錯誤");
            }
        }
    }
}