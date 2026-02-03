using Mep1.Erp.Core.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Collections;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace Mep1.Erp.Desktop
{
    public partial class LinkInvoiceWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly List<InvoicePickRow> _rows;
        private readonly decimal _compareNet;

        public string ProjectCode { get; }
        public string ClientName { get; }
        public string HeaderLine { get; }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? "";
                OnPropertyChanged(nameof(SearchText));
                InvoiceView.Refresh();
                OnPropertyChanged(nameof(ResultsCountText));
            }
        }

        private bool _unlinkedOnly = true;
        public bool UnlinkedOnly
        {
            get => _unlinkedOnly;
            set
            {
                if (_unlinkedOnly == value) return;
                _unlinkedOnly = value;
                OnPropertyChanged(nameof(UnlinkedOnly));
                InvoiceView.Refresh();
                OnPropertyChanged(nameof(ResultsCountText));
            }
        }

        public ICollectionView InvoiceView { get; }

        private InvoicePickRow? _selectedRow;
        public InvoicePickRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                OnPropertyChanged(nameof(SelectedRow));
            }
        }

        public string ResultsCountText
        {
            get
            {
                var count = 0;
                foreach (var _ in InvoiceView) count++;
                return $"{count} shown";
            }
        }

        public LinkInvoiceWindow(
            ApplicationListEntryDto application,
            List<InvoiceListEntryDto> invoices,
            IEnumerable<int> linkedInvoiceIds)
        {
            InitializeComponent();

            ProjectCode = (application.ProjectCode ?? "").Trim();
            ClientName = (application.ClientName ?? "").Trim();

            var appNo = (application.ApplicationNumber ?? "").Trim();
            HeaderLine = $"App {appNo}  |  Project {ProjectCode}  |  {ClientName}";

            // Compare against agreed net if present, otherwise net
            _compareNet = application.AgreedNetAmount ?? application.NetAmount ?? 0m;

            var linkedSet = new HashSet<int>(linkedInvoiceIds);

            _rows = invoices
                .Where(i => string.Equals((i.ProjectCode ?? "").Trim(), ProjectCode, StringComparison.OrdinalIgnoreCase))
                .Select(i => new InvoicePickRow(i, linkedSet.Contains(i.Id), _compareNet))
                .OrderByDescending(r => r.InvoiceDate)
                .ThenByDescending(r => r.InvoiceNumber)
                .ToList();

            InvoiceView = CollectionViewSource.GetDefaultView(_rows);
            InvoiceView.Filter = FilterRow;

            if (InvoiceView is ListCollectionView lcv)
                lcv.CustomSort = new AbsDeltaNetComparer();

            DataContext = this;

            SelectedRow = _rows.FirstOrDefault(r => !r.IsLinked) ?? _rows.FirstOrDefault();

            OnPropertyChanged(nameof(ResultsCountText));
        }

        public string? SelectedInvoiceNumber => SelectedRow?.InvoiceNumber;

        private bool FilterRow(object obj)
        {
            if (obj is not InvoicePickRow row)
                return false;

            if (UnlinkedOnly && row.IsLinked)
                return false;

            var s = (SearchText ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
                s = s.Replace(" ", "");
                if (!row.InvoiceNumber.Contains(s, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private void LinkSelected_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow == null)
            {
                WpfMessageBox.Show(
                    "Select an invoice first.",
                    "Link to invoice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (SelectedRow.IsLinked)
            {
                var result = WpfMessageBox.Show(
                    "This invoice already appears linked to an application. Link anyway?",
                    "Link to invoice",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            DialogResult = true;
            Close();
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public sealed class InvoicePickRow
        {
            private readonly InvoiceListEntryDto _i;

            public InvoicePickRow(InvoiceListEntryDto invoice, bool isLinked, decimal compareNet)
            {
                _i = invoice;
                IsLinked = isLinked;
                DeltaNet = invoice.NetAmount - compareNet;
            }

            public int Id => _i.Id;
            public string InvoiceNumber => _i.InvoiceNumber;
            public string? JobName => _i.JobName;
            public DateTime InvoiceDate => _i.InvoiceDate;
            public decimal NetAmount => _i.NetAmount;
            public decimal OutstandingNet => _i.OutstandingNet;
            public string? Status => _i.Status;

            public bool IsLinked { get; }
            public string IsLinkedText => IsLinked ? "Yes" : "";

            public decimal DeltaNet { get; }
        }

        private sealed class AbsDeltaNetComparer : IComparer
        {
            public int Compare(object? x, object? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return 1;
                if (y is null) return -1;

                var a = (InvoicePickRow)x;
                var b = (InvoicePickRow)y;

                var ax = Math.Abs(a.DeltaNet);
                var bx = Math.Abs(b.DeltaNet);

                var c = ax.CompareTo(bx);
                if (c != 0) return c;

                // Tie-breakers (optional but helps keep it stable & useful):
                // Prefer nearer-zero actual value consistently, then newest date, then invoice number
                c = a.DeltaNet.CompareTo(b.DeltaNet);
                if (c != 0) return c;

                c = b.InvoiceDate.CompareTo(a.InvoiceDate); // newest first
                if (c != 0) return c;

                return string.Compare(b.InvoiceNumber, a.InvoiceNumber, StringComparison.OrdinalIgnoreCase);
            }
        }

    }
}
