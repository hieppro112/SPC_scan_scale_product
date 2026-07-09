using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace scancode.Binding
{
    public class DataBinding:INotifyPropertyChanged
    {
        private string _barcode;
        public event PropertyChangedEventHandler PropertyChanged;

        public string Barcode
        {
            get => _barcode;
            set
            {
                if (_barcode!=value)
                {
                    _barcode = value;
                    OnPropertyChanged(nameof(Barcode));
                }
            }

        }

        public void OnPropertyChanged(string propertyname)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private bool _isScaleConnected = false;
        public bool IsScaleConnected
        {
            get => _isScaleConnected;
            set
            {
                if(_isScaleConnected != value)
                {
                    _isScaleConnected = value;
                    OnPropertyChanged(nameof(IsScaleConnected));
                    OnPropertyChanged(nameof(ScaleStatus));
                }
            }
        }

        public string ScaleStatus
        {
            get => IsScaleConnected ? "Đã kết nối" : "Chưa kết nối";
        }

        private int _totalList = 0;
        public int totalList
        {
            get => _totalList;
            set
            {
                if (_totalList != value)
                {
                    _totalList = value;
                    OnPropertyChanged(nameof(totalList));
                }
            }
        }

        private int _totalpage = 0; 
        public int totalPage
        {
            get => _totalpage;
            set
            {
                if(_totalpage !=value){
                    _totalpage = value;
                    OnPropertyChanged(nameof(_totalpage));
                }
            }
        }

        private int _sluong_page;
        public int sluong_page
        {
            get => _sluong_page;
            set
            {
                if (_sluong_page != value)
                {
                    _sluong_page = value;
                    OnPropertyChanged(nameof(sluong_page));
                }
            }
        }

        private int _currentPage =1;
        public int currentPage
        {
            get => _currentPage;
            set
            {
                if (_currentPage != value)
                {
                    _currentPage = value;
                    OnPropertyChanged(nameof(currentPage));
                }
            }
        }

        private bool _sttBtn_prev = false;
        public bool SttBtn_prev
        {
            get=>_sttBtn_prev;
            set
            {
                if (_sttBtn_prev != value)
                {
                    _sttBtn_prev = value;
                    OnPropertyChanged(nameof(SttBtn_prev));
                }
            }
        }

        private bool _sttBtn_next = true;
        public bool SttBtn_next
        {
            get => _sttBtn_next;
            set
            {
                if (_sttBtn_next != value)
                {
                    _sttBtn_next = value;
                    OnPropertyChanged(nameof(SttBtn_next));
                }
            }
        }

        private int _curren_index_item = 1;
        public int Curren_index_item
        {
            get => _curren_index_item;
            set
            {
                if (_curren_index_item != value)
                {
                    _curren_index_item = value;
                    OnPropertyChanged(nameof(Curren_index_item));
                }
            }
        }

        private int _to_index_item = 1;
        public int To_index_item
        {
            get => _to_index_item;
            set
            {
                if (_to_index_item != value)
                {
                    _to_index_item = value;
                    OnPropertyChanged(nameof(To_index_item));
                }
            }
        }


    }
}
