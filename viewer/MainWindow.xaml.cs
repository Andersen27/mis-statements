using System;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Net;
using System.Net.Sockets;
using MIS_Statements_View.Properties;
using System.Threading;
using System.Data;

namespace MIS_Statements_View {
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        private FontFamily MAIN_FONT = new FontFamily("Dubai Medium");
        private int MAX_BYTES = 2000000;
        private BlurEffect blur = new BlurEffect();
        private List<string> filterParameters = new List<string>() { "Выбрать параметр...", "Дата отправки", "Имя", "Фамилия", "Отчество", "Дата рождения", "Пол" };
        private List<string> dataComparisonParameters = new List<string>() { "до", "равна", "после" };
        private List<string> genderParameters = new List<string>() { "мужской", "женский" };
        private List<int> removableStatements = new List<int>();
        private DataTable StatementsDT;
        private Socket sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private Socket receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private Socket serverUpdateSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private Thread remoteDataThread;

        public MainWindow()
        {
            blur.Radius = 20;
            InitializeComponent();

            Data_view_checkbox.IsChecked = Settings.Default.Data_Viewed;
            Name_view_checkbox.IsChecked = Settings.Default.Name_Viewed;
            Server_address.Text = Settings.Default.Server_Address;

            if (!Data_view_checkbox.IsChecked ?? false)
                foreach (Grid grid in Statements_stack.Children)
                    ((Grid)grid.Children[1]).ColumnDefinitions[0].Width = new GridLength(0);
            if (!Name_view_checkbox.IsChecked ?? false)
                foreach (Grid grid in Statements_stack.Children)
                    ((Grid)grid.Children[1]).ColumnDefinitions[1].Width = new GridLength(0);

        }

        private void Begin(object sender, RoutedEventArgs e)
        {
            List<byte> bufferList = new List<byte>();
            byte[] buffer = new byte[MAX_BYTES];

            if (StringToIP(Settings.Default.Server_Address) &&
                ServerConnect(IPAddress.Parse(Settings.Default.Server_Address)) &&
                ServerSendMessage(CreateServerMessage("SELECT * FROM statements ORDER BY datetime DESC"))) {
                //do {
                    sendSocket.Receive(buffer);
                //} while (buffer[0] != 3);
                TryCloseSendSocket();
                bufferList = buffer.ToList();
                bufferList.RemoveAt(0);
                int indexOfNull = bufferList.IndexOf(0);
                bufferList.RemoveRange(indexOfNull, bufferList.Count - indexOfNull);

                remoteDataThread = new Thread(ReceiveRemoteData);
                remoteDataThread.Start();
            }
            else {
                ShowNotification("Не удалось подключиться к серверу");
                var bc = new BrushConverter();
                Connected_label.Background = (Brush)bc.ConvertFrom("#FFF37777");
                Connected_label.Content = "Не подключено";
            }
            StatementsDT = GetDataTable(bufferList.ToArray());
            for (int i = 0; i < StatementsDT.Rows.Count; i++) {
                Statements_stack.Children.Add(CreateStatement(StatementsDT.Rows[i]));
            }
        }

        public void ReceiveRemoteData()
        {
            while (true) {
                List<byte> bufferList = new List<byte>();
                byte[] buffer = new byte[MAX_BYTES];
                //do {
                serverUpdateSocket = receiveSocket.Accept();
                serverUpdateSocket.Receive(buffer);
                TryCloseSendSocket();
                //} while (buffer[0] != 1 && buffer[0] != 2 && buffer[0] != 3);
                bufferList = buffer.ToList();
                bufferList.RemoveAt(0);
                int indexOfNull = bufferList.IndexOf(0);
                bufferList.RemoveRange(indexOfNull, bufferList.Count - indexOfNull);
                switch (buffer[0]) {
                    case 1:
                        // Новая заявка
                        DataRow newStatement = GetDataTable(bufferList.ToArray()).Rows[0];
                        StatementsDT.Rows.InsertAt(StatementsDT.NewRow(), 0);
                        StatementsDT.Rows[0].ItemArray = newStatement.ItemArray;
                        Dispatcher.Invoke(() => Statements_stack.Children.Insert(0, CreateStatement(newStatement)));
                        break;
                    case 2:
                        // Обновление отредактированного комментария заявки
                        DataRow updatedStatement = GetDataTable(bufferList.ToArray()).Rows[0];
                        string updateStatementID = updatedStatement.ItemArray[0].ToString();
                        for (int i = 0; i < StatementsDT.Rows.Count; i++) {
                            if (StatementsDT.Rows[i][0].ToString() == updateStatementID) {
                                StatementsDT.Rows[i][9] = updatedStatement.ItemArray[2];
                                Dispatcher.Invoke(() => UpdateStatementComment((Grid)Statements_stack.Children[i], updatedStatement.ItemArray[2].ToString()));
                                break;
                            }
                        }
                        break;
                    case 3:
                        // Удаление заявок
                        DataTable deleteStatementIDs = GetDataTable(bufferList.ToArray());
                        for (int i = 0; i < StatementsDT.Rows.Count; i++) {
                            for (int j = 0; j < deleteStatementIDs.Rows.Count; j++) {
                                if (StatementsDT.Rows[i][0].ToString() == deleteStatementIDs.Rows[j][0].ToString()) {
                                    StatementsDT.Rows.RemoveAt(i);
                                    Dispatcher.Invoke(() => Statements_stack.Children.RemoveAt(i));
                                    i--;
                                    break;
                                }
                            }
                        }
                        break;
                }
            }
        }

        public bool StringToIP(string ip)
        {
            try {
                IPAddress.Parse(Settings.Default.Server_Address);
                return true;
            }
            catch {
                return false;
            }
        }

        public bool ServerConnect(IPAddress ip)
        {
            try {
                receiveSocket.Bind(new IPEndPoint(ip, 707));
                receiveSocket.Listen(1);
                return true;
            }
            catch {
                return false;
            }
        }

        public bool ServerSendMessage(byte[] message)
        {
            try {
                sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //sendSocket.SendTimeout = 5000;
                sendSocket.Connect(Settings.Default.Server_Address, 708);
                sendSocket.Send(message);
                return true;
            }
            catch {
                sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                return false;
            }
        }

        public DataTable GetDataTable (byte[] buffer)
        {
            string dataTableString = Encoding.UTF8.GetString(buffer);
            DataTable result = new DataTable();
            for (int i = 0; i < 10; i++)
                result.Columns.Add(new DataColumn());
            List<string> row = new List<string>();
            int indexBegin = 0;
            int indexEnd;
            while (indexBegin < dataTableString.Length - 1) {
                indexEnd = dataTableString.IndexOf("\\,", indexBegin);
                row.Add(dataTableString.Substring(indexBegin, indexEnd - indexBegin));
                if (dataTableString.Substring(indexEnd + 2, 2) == "\\;") {
                    result.Rows.Add(row.ToArray());
                    row.Clear();
                    indexEnd+=2;
                }
                indexBegin = indexEnd + 2;
            }
            return result;
        }

        public Grid CreateStatement(DataRow statement)
        {
            Grid firstGrid = new Grid() { Height = 50, Margin = new Thickness(20, 5, 20, 5) };
            {
                firstGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
                firstGrid.ColumnDefinitions.Add(new ColumnDefinition());
                // Кнопка удаления записи
                CheckBox deleteStatement = new CheckBox() {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };
                deleteStatement.LayoutTransform = new ScaleTransform() { ScaleX = 2.75, ScaleY = 2.75 };
                deleteStatement.Checked += Statement_checkbox_Checked;
                firstGrid.Children.Add(deleteStatement);
                // Предпросмотр заявления
                var bc = new BrushConverter();
                Grid secondGrid = new Grid() { Margin = new Thickness(10, 0, 0, 0), Background = (Brush)bc.ConvertFrom("#FFECFFFD") };
                {
                    if (Data_view_checkbox.IsChecked ?? false)
                        secondGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(90) });
                    else
                        secondGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0) });
                    if (Name_view_checkbox.IsChecked ?? false)
                        secondGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(160) });
                    else
                        secondGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0) });
                    secondGrid.ColumnDefinitions.Add(new ColumnDefinition());
                    // Границы
                    Border mainBorder = new Border() {
                        BorderBrush = (Brush)bc.ConvertFrom("#FF54AEA6"),
                        BorderThickness = new Thickness(2),
                    };
                    secondGrid.Children.Add(mainBorder);
                    Grid.SetColumnSpan(mainBorder, 3);
                    Border dataBorder = new Border() {
                        BorderBrush = (Brush)bc.ConvertFrom("#FF54AEA6"),
                        BorderThickness = new Thickness(0, 0, 2, 0),
                    };
                    secondGrid.Children.Add(dataBorder);
                    Border nameBorder = new Border() {
                        BorderBrush = (Brush)bc.ConvertFrom("#FF54AEA6"),
                        BorderThickness = new Thickness(0, 0, 2, 0),
                    };
                    secondGrid.Children.Add(nameBorder);
                    Grid.SetColumn(nameBorder, 1);
                    // Данные
                    Label dateLabel = new Label() {
                        Content = statement.ItemArray[3].ToString().Remove(10).Remove(6, 2),
                        FontFamily = MAIN_FONT,
                        FontSize = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    secondGrid.Children.Add(dateLabel);
                    string name = statement.ItemArray[5].ToString();
                    if (name.Length > 11) {
                        name.Remove(11);
                        name += "...";
                    }
                    else if (name.Length > 9) {
                        name += " ...";
                    }
                    else {
                        name += " " + statement.ItemArray[4].ToString()[0] + ". " +
                        (statement.ItemArray[6].ToString().Length > 0 ? statement.ItemArray[6].ToString()[0] + "." : "");
                    }
                    Label nameLabel = new Label() {
                        Content = name,
                        FontFamily = MAIN_FONT,
                        FontSize = 20,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    secondGrid.Children.Add(nameLabel);
                    Grid.SetColumn(nameLabel, 1);
                    Label commentLabel = new Label() {
                        Content = statement.ItemArray[9].ToString().Length > 60 ?
                            statement.ItemArray[9].ToString().Remove(60).Replace("\r\n", " ") : statement.ItemArray[9].ToString().Replace("\r\n", " "),
                        FontFamily = MAIN_FONT,
                        FontSize = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    secondGrid.MouseLeftButtonDown += StatementSelect;
                    secondGrid.Children.Add(commentLabel);
                    Grid.SetColumn(commentLabel, 2);
                }
                firstGrid.Children.Add(secondGrid);
                Grid.SetColumn(secondGrid, 1);
            }
            return firstGrid;
        }

        public void UpdateStatementComment(Grid statement, string comment)
        {
            ((statement.Children[1] as Grid).Children[5] as Label).Content = comment;
        }

        public bool FilterCheck(DataRow statement)
        {
            for (int i = 0; i < Filter_stack.Children.Count; i++) {
                switch (((Filter_stack.Children[i] as Grid).Children[2] as ComboBox).SelectedItem.ToString()) {
                    case "Дата отправки":
                        DateTime? statementDate = ((Filter_stack.Children[i] as Grid).Children[4] as DatePicker).SelectedDate;
                        byte comparisonDate = // 0 - дата не выбрана, 1 - дата отправки меньше, 2 - дата отправки равна, 3 - дата отправки больше
                            statementDate.HasValue ? 
                                (statementDate.Value.CompareTo(DateTime.Parse(statement.ItemArray[3].ToString().Remove(10))) > 0 ?
                                    (byte)1 : 
                                    (statementDate.Value.CompareTo(DateTime.Parse(statement.ItemArray[3].ToString().Remove(10))) < 0 ?
                                        (byte)3 :
                                        (byte)2
                                    )
                                )
                                : (byte)0;
                        switch (((Filter_stack.Children[i] as Grid).Children[3] as ComboBox).SelectedItem.ToString()) {
                            case "до":
                                if (comparisonDate == 3)
                                    return false;
                                break;
                            case "равна":
                                if (comparisonDate == 1 || comparisonDate == 3)
                                    return false;
                                break;
                            case "после":
                                if (comparisonDate == 1 || comparisonDate == 2)
                                    return false;
                                break;
                        }
                        break;
                    case "Дата рождения":
                        DateTime? statementBirthday = ((Filter_stack.Children[i] as Grid).Children[4] as DatePicker).SelectedDate;
                        byte comparisonBirthday = // 0 - дата не выбрана, 1 - дата отправки меньше, 2 - дата отправки равна, 3 - дата отправки больше
                            statementBirthday.HasValue ?
                                (statementBirthday.Value.CompareTo(DateTime.Parse(statement.ItemArray[7].ToString().Remove(10))) > 0 ?
                                    (byte)1 :
                                    (statementBirthday.Value.CompareTo(DateTime.Parse(statement.ItemArray[7].ToString().Remove(10))) < 0 ?
                                        (byte)3 :
                                        (byte)2
                                    )
                                )
                                : (byte)0;
                        switch (((Filter_stack.Children[i] as Grid).Children[3] as ComboBox).SelectedItem.ToString()) {
                            case "до":
                                if (comparisonBirthday == 3)
                                    return false;
                                break;
                            case "равна":
                                if (comparisonBirthday == 1 || comparisonBirthday == 3)
                                    return false;
                                break;
                            case "после":
                                if (comparisonBirthday == 1 || comparisonBirthday == 2)
                                    return false;
                                break;
                        }
                        break;
                    case "Имя":
                        string statementName = ((Filter_stack.Children[i] as Grid).Children[4] as TextBox).Text;
                        if (statementName != "" && statementName != statement.ItemArray[4].ToString())
                            return false;
                        break;
                    case "Фамилия":
                        string statementSurname = ((Filter_stack.Children[i] as Grid).Children[4] as TextBox).Text;
                        if (statementSurname != "" && statementSurname != statement.ItemArray[5].ToString())
                            return false;
                        break;
                    case "Отчество":
                        string statementMidname = ((Filter_stack.Children[i] as Grid).Children[4] as TextBox).Text;
                        if (statementMidname != "" && statementMidname != statement.ItemArray[6].ToString())
                            return false;
                        break;
                    case "Пол":
                        if (((Filter_stack.Children[i] as Grid).Children[4] as ComboBox).SelectedItem.ToString() == "мужской") {
                            if (statement.ItemArray[8].ToString() == "True")
                                return false;
                        }
                        else {
                            if (statement.ItemArray[8].ToString() == "False")
                                return false;
                        }
                        break;
                }
            }
            return true;
        }

        private void StatementSelect(object sender, MouseButtonEventArgs e)
        {
            if (removableStatements.Count > 0) {
                e.Handled = true;
            }
            else {
                int statementID = Convert.ToInt32(
                    StatementsDT.Rows[Statements_stack.Children.IndexOf((sender as Grid).Parent as Grid)][0]);
                if (TryChangeStatus(statementID, "browsing")) {
                    Main_grid.Effect = blur;
                    Blackout_rectangle.Visibility = Visibility.Visible;
                    DataRow statement = StatementsDT.Rows[Statements_stack.Children.IndexOf(((sender as Grid).Parent as Grid))];
                    string date = statement.ItemArray[3].ToString();
                    date.Remove(date.Length - 3, 3);
                    Statement_date_label.Content = date;
                    Statement_name_label.Content =
                        statement.ItemArray[5].ToString() + " " + statement.ItemArray[4].ToString() + " " + statement.ItemArray[6].ToString();
                    Statement_birthdate_label.Content = statement.ItemArray[7].ToString().Remove(10, 8);
                    Statement_gender_label.Content = statement.ItemArray[8].ToString() == "False" ? "мужской" : "женский";
                    Statement_comment_textblock.Text = statement.ItemArray[9].ToString();
                    Statement_window.Visibility = Visibility.Visible;
                }
            }
        }

        private void Settings_Open(object sender, RoutedEventArgs e)
        {
            Main_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Settings_window.Visibility = Visibility.Visible;
        }

        private void Close_settings_Click(object sender, RoutedEventArgs e)
        {
            Settings_window.Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_grid.Effect = null;
            Server_address.Text = Settings.Default.Server_Address;
        }

        private void Apply_settings_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.Server_Address = Server_address.Text;
            Settings.Default.Save();
            ShowSettingsNotification("Настройки применены");
        }


        private void Delete_button_Click(object sender, RoutedEventArgs e)
        {
            Main_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Delete_statements_window.Visibility = Visibility.Visible;
        }

        private void Delete_statements(object sender, RoutedEventArgs e)
        {
            Delete_statements_window.Visibility = Visibility.Hidden;
            string ids = "";
            for (int i = 0; i < removableStatements.Count; i++)
                ids += removableStatements[i] + ", ";
            ServerSendMessage(CreateServerMessage("DELETE FROM statements WHERE id IN(" + ids.Remove(ids.Length - 2) + ")"));
            removableStatements.Clear();
            Delete_button.Visibility = Visibility.Hidden;
            ShowNotification("Заявки удалены");
        }

        private bool TryCloseSendSocket ()
        {
            try {
                sendSocket.Shutdown(SocketShutdown.Both);
                sendSocket.Close();
                return true;
            }
            catch {
                return false;
            }
        }

        private bool TryChangeStatus(int statementID, string status)
        {
            List<byte> bufferList;
            byte[] buffer = new byte[13];

            if (ServerSendMessage(CreateServerMessage("SELECT status FROM statements WHERE id=" + statementID))) {
                //do {
                sendSocket.Receive(buffer);
                //} while (buffer[0] != 3);
                TryCloseSendSocket();
                bufferList = buffer.ToList();
                bufferList.RemoveAt(0);

                switch (Encoding.UTF8.GetString(bufferList.ToArray()).Replace("\0", "")) {
                    case "free\\,\\;":
                        ServerSendMessage(CreateServerMessage("UPDATE statements SET status='" + status + "' WHERE id=" + statementID));
                        removableStatements.Add(statementID);
                        return true;
                    case "browsing\\,\\;":
                        ShowNotification("Заявка сейчас просматривается");
                        return false;
                    case "editing\\,\\;":
                        ShowNotification("Заявка сейчас редактируется");
                        return false;
                    default:
                        return false;
                }
            }
            else {
                ShowNotification("Ошибка соединения с сервером");
                return false;
            }
        }

        private void Statement_checkbox_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = (CheckBox)sender;
            checkBox.Unchecked += Statement_checkbox_Unchecked;
            int statementID = Convert.ToInt32(
                StatementsDT.Rows[Statements_stack.Children.IndexOf((Grid)checkBox.Parent)][0]);
            if (TryChangeStatus(statementID, "editing")) {
                if (removableStatements.Count == 1) {
                    Delete_button.Visibility = Visibility.Visible;
                }
            }
            else {
                checkBox.Unchecked -= Statement_checkbox_Unchecked;
                checkBox.IsChecked = false;
            }
        }
        private void Statement_checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            int statementID = Convert.ToInt32(
                StatementsDT.Rows[Statements_stack.Children.IndexOf((Grid)(sender as CheckBox).Parent)][0]);
            ServerSendMessage(CreateServerMessage("UPDATE statements SET status='free' WHERE id=" + statementID));
            removableStatements.Remove(statementID);
            if (removableStatements.Count == 0) {
                Delete_button.Visibility = Visibility.Hidden;
            }
        }
        private void Data_view_checkbox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[1]).ColumnDefinitions[0].Width = new GridLength(90);
            Settings.Default.Data_Viewed = Data_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void Data_view_checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[1]).ColumnDefinitions[0].Width = new GridLength(0);
            Settings.Default.Data_Viewed = Data_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void Name_view_checkbox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[1]).ColumnDefinitions[1].Width = new GridLength(160);
            Settings.Default.Name_Viewed = Name_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void Name_view_checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[1]).ColumnDefinitions[1].Width = new GridLength(0);
            Settings.Default.Name_Viewed = Name_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void View_data_button_Click(object sender, RoutedEventArgs e)
        {
            if (View_data_grid.Visibility.Equals(Visibility.Visible))
                View_data_grid.Visibility = Visibility.Collapsed;
            else
                View_data_grid.Visibility = Visibility.Visible;
        }

        public byte[] CreateServerMessage(string query)
        {
            List<byte> message = new List<byte>();
            message.Add(1);
            for (int i = 1; i < 13; i++)
                message.Add(48);
            message.AddRange(Encoding.UTF8.GetBytes(query));
            return message.ToArray();
        }

        private void Filter_button_Click(object sender, RoutedEventArgs e)
        {
            if (Filter_scrollviewer.Visibility.Equals(Visibility.Visible))
                Filter_scrollviewer.Visibility = Visibility.Collapsed;
            else
                Filter_scrollviewer.Visibility = Visibility.Visible;
        }

        private void Add_filter_button_Click(object sender, RoutedEventArgs e)
        {
            var bc = new BrushConverter();
            Grid newFilter = new Grid() { Margin = new Thickness(10), Background = (Brush)bc.ConvertFrom("#FFFFCFA6") };
            {
                Border filterBorder = new Border() {
                    BorderBrush = (Brush)bc.ConvertFrom("#FFFFA356"),
                    BorderThickness = new Thickness(2),
                };
                newFilter.Children.Add(filterBorder);
                Grid.SetColumnSpan(filterBorder, 4);
                newFilter.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
                newFilter.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0, GridUnitType.Auto) });
                newFilter.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0, GridUnitType.Auto) });
                newFilter.ColumnDefinitions.Add(new ColumnDefinition());

                Button deleteFilterButton = new Button() {
                    Content = "X",
                    FontSize = 16,
                    FontFamily = new FontFamily("Arial"),
                    Margin = new Thickness(2, 2, 0, 2),
                    Foreground = (Brush)bc.ConvertFrom("#FFD40000")
                };
                deleteFilterButton.Click += Delete_filter_button_Click;
                newFilter.Children.Add(deleteFilterButton);
                Grid.SetColumn(deleteFilterButton, 0);
                ComboBox filterParameterCombobox = new ComboBox() { SelectedIndex = 0, ItemsSource = filterParameters, FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 0, 2) };
                filterParameterCombobox.DropDownOpened += Filter_Parameter_Dropdown;
                filterParameterCombobox.DropDownClosed += Filter_Parameter_Dropup;
                filterParameterCombobox.SelectionChanged += Filter_Parameter_Changed;
                newFilter.Children.Add(filterParameterCombobox);
                Grid.SetColumn(filterParameterCombobox, 1);
            }
            Filter_stack.Children.Add(newFilter);
            if (Filter_stack.Children.Count == 8) {
                if (Apply_filters_button.Visibility == Visibility.Visible)
                    Add_filter_button.Visibility = Visibility.Hidden;
                else {
                    Add_filter_button.Visibility = Visibility.Collapsed;
                    Apply_filters_button.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Apply_filters_button_Click(object sender, RoutedEventArgs e)
        {
            if (Filter_stack.Children.Count > 0) {
                for (int i = 0; i < StatementsDT.Rows.Count; i++) {
                    if (FilterCheck(StatementsDT.Rows[i]))
                        (Statements_stack.Children[i] as Grid).Visibility = Visibility.Visible;
                    else
                        (Statements_stack.Children[i] as Grid).Visibility = Visibility.Collapsed;
                }
                Filter_button.Width = 116;
            }
            else {
                Filter_button.Width = 100;
                foreach (Grid filter in Statements_stack.Children)
                    filter.Visibility = Visibility.Visible;
            }

            if (Add_filter_button.Visibility == Visibility.Visible)
                Apply_filters_button.Visibility = Visibility.Hidden;
            else {
                Apply_filters_button.Visibility = Visibility.Collapsed;
                Add_filter_button.Visibility = Visibility.Collapsed;  
            }
        }

        private void Filter_Parameter_Dropdown(object sender, EventArgs e)
        {
             filterParameters.RemoveAt(0);
        }
        private void Filter_Parameter_Dropup(object sender, EventArgs e)
        {
             filterParameters.Insert(0, "Выбрать параметр...");
        }

        private void Filter_Parameter_Changed(object sender, EventArgs e)
        {
            ComboBox filterParameter = (ComboBox)sender;
            Grid currentFilter = (Grid)filterParameter.Parent;
            switch (filterParameter.SelectedIndex) {
                case 0:
                case 4:
                    currentFilter.Children.RemoveRange(3, 2);
                    ComboBox parametersCB1 = new ComboBox() { SelectedIndex = 0, ItemsSource = dataComparisonParameters, FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 0, 2) };
                    parametersCB1.SelectionChanged += Parameter_Changed;
                    currentFilter.Children.Add(parametersCB1);
                    DatePicker parametersDP = new DatePicker() { FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 2, 2), Background = Brushes.White };
                    parametersDP.SelectedDateChanged += Parameter_Changed;
                    currentFilter.Children.Add(parametersDP);
                    break;
                case 1:
                case 2:
                case 3:
                    currentFilter.Children.RemoveRange(3, 2);
                    currentFilter.Children.Add(new Button() { Content = "равно", IsEnabled = false, FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 0, 2) });
                    TextBox parametersTB = new TextBox() { FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 2, 2) };
                    parametersTB.PreviewTextInput += TextBox_Changed;
                    parametersTB.TextChanged += TextBox_KeyDown;
                    currentFilter.Children.Add(parametersTB);
                    break;
                case 5:
                    currentFilter.Children.RemoveRange(3, 2);
                    currentFilter.Children.Add(new Button() { Content = "равен", IsEnabled = false, FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 0, 2) });
                    ComboBox parametersCB2 = new ComboBox() { SelectedIndex = 0, ItemsSource = genderParameters, FontSize = 16, FontFamily = MAIN_FONT, Margin = new Thickness(5, 2, 2, 2) };
                    parametersCB2.SelectionChanged += Parameter_Changed;
                    currentFilter.Children.Add(parametersCB2);
                    break;
            }
            Grid.SetColumn(currentFilter.Children[3], 2);
            Grid.SetColumn(currentFilter.Children[4], 3);

            Apply_filters_button.Visibility = Visibility.Visible;
            if (Add_filter_button.Visibility == Visibility.Collapsed)
                Add_filter_button.Visibility = Visibility.Hidden;
        }

        private void IPAddress_Changed(object sender, TextChangedEventArgs e)
        {
            if (Server_address.Text != Settings.Default.Server_Address)
                Apply_settings_button.Visibility = Visibility.Visible;
            else {
                Apply_settings_button.Visibility = Visibility.Hidden;
            }
        }
        private void TextBox_Changed(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Contains('\\'))
                e.Handled = true;
        }
        private void TextBox_KeyDown(object sender, TextChangedEventArgs e)
        {
            if (Apply_filters_button.Visibility != Visibility.Visible) {
                Apply_filters_button.Visibility = Visibility.Visible;
                if (Add_filter_button.Visibility == Visibility.Collapsed)
                    Add_filter_button.Visibility = Visibility.Hidden;
            }
        }
        private void Parameter_Changed(object sender, EventArgs e)
        {
            if (Apply_filters_button.Visibility != Visibility.Visible) {
                Apply_filters_button.Visibility = Visibility.Visible;
                if(Add_filter_button.Visibility == Visibility.Collapsed)
                    Add_filter_button.Visibility = Visibility.Hidden;
            }
        }

        private void Delete_filter_button_Click(object sender, RoutedEventArgs e)
        {
            Button deleteFilterButton = (Button)sender;
            Filter_stack.Children.Remove((Grid)deleteFilterButton.Parent);
            Apply_filters_button.Visibility = Visibility.Visible;
            if (Filter_stack.Children.Count == 7)
                Add_filter_button.Visibility = Visibility.Visible;
        }

        private void Exit_button_Click(object sender, RoutedEventArgs e)
        {
            Main_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Exit_window.Visibility = Visibility.Visible;
        }

        private void Shutdown_button_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        private void Close(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (removableStatements.Count > 0)
                for (int i = 0; i < removableStatements.Count; i++)
                    ServerSendMessage(CreateServerMessage("UPDATE statements SET status='free' WHERE id=" + removableStatements[i]));
            else
                ServerSendMessage(CreateServerMessage("SELECT 1"));
            if (receiveSocket.IsBound)
                receiveSocket.Dispose();
            if (remoteDataThread != null && remoteDataThread.IsAlive)
                remoteDataThread.Abort();
        }

        private void Delete_statement_Click(object sender, RoutedEventArgs e)
        {
            Statement_window.Effect = blur;
            Panel.SetZIndex(Blackout_rectangle, 4);
            Panel.SetZIndex(Notification_window, 5);
            Blackout_rectangle.Visibility = Visibility.Visible;
            Delete_statement_window.Visibility = Visibility.Visible;
        }

        private void Close_statement_Click(object sender, RoutedEventArgs e)
        {
            ServerSendMessage(CreateServerMessage("UPDATE statements SET status='free' WHERE id=" + removableStatements[0]));
            removableStatements.Clear();
            Statement_window.Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_grid.Effect = null;
        }

        private void Cancel_button_Click(object sender, RoutedEventArgs e)
        {
            Button cancel = (Button)sender;
            Grid tempGrid = (Grid)cancel.Parent;
            tempGrid = (Grid)tempGrid.Parent;
            tempGrid.Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_grid.Effect = null;
        }

        private void Delete_current_statement(object sender, RoutedEventArgs e)
        {
            Delete_statement_window.Visibility = Visibility.Hidden;
            ServerSendMessage(CreateServerMessage("DELETE FROM statements WHERE id IN(" + removableStatements[0] + ")"));
            removableStatements.Clear();
            Panel.SetZIndex(Blackout_rectangle, 2);
            Statement_window.Visibility = Visibility.Hidden;
            Statement_window.Effect = null;
            ShowNotification("Заявка удалена");
        }

        private void Cancel_Delete_button_Click(object sender, RoutedEventArgs e)
        {
            Button cancel = (Button)sender;
            Grid tempGrid = (Grid)cancel.Parent;
            tempGrid = (Grid)tempGrid.Parent;
            tempGrid.Visibility = Visibility.Hidden;
            Panel.SetZIndex(Notification_window, 3);
            Panel.SetZIndex(Blackout_rectangle, 2);
            Statement_window.Effect = null;
        }

        private void OK_button_Click(object sender, RoutedEventArgs e)
        {
            Button cancel = (Button)sender;
            ((Grid)cancel.Parent).Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_grid.Effect = null;
        }

        private void OK_Reboot_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
            System.Windows.Forms.Application.Restart();
        }

        private void ShowSettingsNotification(string text)
        {
            Settings_window.Effect = blur;
            Panel.SetZIndex(Blackout_rectangle, 4);
            Panel.SetZIndex(Notification_window, 5);
            Blackout_rectangle.Visibility = Visibility.Visible;
            OK_button.Width = 200;
            Notification_label.Content = text;
            OK_button.Content = "Перезапустить";
            OK_button.Click -= OK_button_Click;
            OK_button.Click += OK_Reboot_Click;
            Notification_window.Visibility = Visibility.Visible;
        }

        private void ShowNotification(string text)
        {
            Main_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Notification_label.Content = text;
            Notification_window.Visibility = Visibility.Visible;
        }
    }
}
