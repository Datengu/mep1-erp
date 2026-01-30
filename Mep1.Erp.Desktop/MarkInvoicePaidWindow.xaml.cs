using System;
using System.Globalization;
using System.Windows;

namespace Mep1.Erp.Desktop
{
    public partial class MarkInvoicePaidWindow : Window
    {
        public DateTime PaidDate { get; private set; }
        public decimal AmountPaidThisTime { get; private set; }

        public MarkInvoicePaidWindow(DateTime defaultPaidDate, decimal defaultAmountPaidThisTime)
        {
            InitializeComponent();

            PaidDatePicker.SelectedDate = defaultPaidDate;
            AmountPaidTextBox.Text = defaultAmountPaidThisTime.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            if (PaidDatePicker.SelectedDate == null)
            {
                ShowError("Paid date is required.");
                return;
            }

            if (!decimal.TryParse(AmountPaidTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt))
            {
                ShowError("Amount must be a number (e.g. 1200.00).");
                return;
            }

            if (amt <= 0m)
            {
                ShowError("Amount must be greater than 0.");
                return;
            }

            PaidDate = PaidDatePicker.SelectedDate.Value.Date;
            AmountPaidThisTime = amt;

            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
