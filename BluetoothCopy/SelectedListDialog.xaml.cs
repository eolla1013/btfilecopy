﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BluetoothCopy
{
    /// <summary>
    /// SelectedListDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class SelectedListDialog : Window
    {

        public SelectedListDialog() {
            InitializeComponent();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e) {

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {

            this.DialogResult = false;
            this.Close();
        }

    }
}
