using System.Collections.ObjectModel;
using System.ComponentModel;

namespace OPCGatewayTool.Models
{
    public class OPCTreeNode : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;
        private string _name;
        private string _fullPath;
        private bool _hasChildren;
        private bool _isLoaded;

        public OPCTreeNode(string name, string fullPath, bool hasChildren = false)
        {
            _name = name;
            _fullPath = fullPath;
            _hasChildren = hasChildren;
            _isLoaded = false;
            Children = new ObservableCollection<OPCTreeNode>();
            
            // 暫時禁用佔位符機制進行測試
            // if (_hasChildren && !_isLoaded && !string.IsNullOrEmpty(fullPath))
            // {
            //     Children.Add(new OPCTreeNode("Loading...", "##PLACEHOLDER##", false));
            // }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }

        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                _hasChildren = value;
                OnPropertyChanged(nameof(HasChildren));
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                _isLoaded = value;
                OnPropertyChanged(nameof(IsLoaded));
            }
        }

        public ObservableCollection<OPCTreeNode> Children { get; }

        public bool IsFolder => HasChildren;
        public bool IsItem => !HasChildren;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}