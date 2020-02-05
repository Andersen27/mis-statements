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
using MIS_Statements_Client.Properties;
using System.Threading;
using System.Data;
using System.Net.NetworkInformation;

namespace MIS_Statements_Client {
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        private char[] macAddress = new char[12];
        private FontFamily MAIN_FONT = new FontFamily("Dubai Medium");
        private int MAX_BYTES = 2000000;
        private BlurEffect blur = new BlurEffect();
        private int editingStatement = -1;
        private string editingComment;
        private DataTable StatementsDT;
        private Socket sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private Socket receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private Socket serverUpdateSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private Thread remoteDataThread;

        public MainWindow()
        {
            blur.Radius = 20;
            InitializeComponent();

            Remember_data_checkbox.IsChecked = Settings.Default.Remember_Data;
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
            macAddress = GetMACAddress();

            List<byte> bufferList = new List<byte>();
            byte[] buffer = new byte[MAX_BYTES];

            if (StringToIP(Settings.Default.Server_Address) &&
                ServerConnect(IPAddress.Parse(Settings.Default.Server_Address)) &&
                ServerSendMessage(CreateServerMessage(
                    "SELECT * FROM statements WHERE mac='" + new string(macAddress) + "' ORDER BY datetime DESC"))) {
                //do {
                sendSocket.Receive(buffer);
                //} while (buffer[0] != 4);
                TryCloseSendSocket(sendSocket);
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

        public char[] GetMACAddress()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            string result = "";
            foreach (NetworkInterface adapter in nics) {
                if (result == "") {
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    result = adapter.GetPhysicalAddress().ToString();
                }
            }
            return result.ToCharArray();
        }

        public byte[] CreateServerMessage(string query)
        {
            List<byte> message = new List<byte>();
            message.Add(2);
            message.AddRange(Encoding.UTF8.GetBytes(macAddress));

            message.AddRange(Encoding.UTF8.GetBytes(query));
            return message.ToArray();
        }

        public DataTable GetDataTable(byte[] buffer)
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
                    indexEnd += 2;
                }
                indexBegin = indexEnd + 2;
            }
            return result;
        }

        public Grid CreateStatement(DataRow statement)
        {
            Grid firstGrid = new Grid() { Height = 50, Margin = new Thickness(20, 5, 20, 5) };
            {
                firstGrid.ColumnDefinitions.Add(new ColumnDefinition());
                firstGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
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
                    secondGrid.Children.Add(commentLabel);
                    Grid.SetColumn(commentLabel, 2);
                }
                firstGrid.Children.Add(secondGrid);
                // Кнопка редактирования записи
                Button editStatement = new Button() {
                    Content = "✎",
                    FontFamily = MAIN_FONT,
                    FontSize = 20,
                    //Foreground = (Brush)bc.ConvertFrom("#FFFF7E12")
                };
                editStatement.Click += Statement_begin_edit_Click;
                firstGrid.Children.Add(editStatement);
                Grid.SetColumn(editStatement, 1);
            }
            return firstGrid;
        }

        public void UpdateStatementComment(Grid statement, string comment)
        {
            ((statement.Children[0] as Grid).Children[5] as Label).Content = comment;
        }


        private void Statement_begin_edit_Click(object sender, RoutedEventArgs e)
        {
            int statementID = Convert.ToInt32(
                StatementsDT.Rows[Statements_stack.Children.IndexOf((sender as Button).Parent as Grid)][0]);
            if (TryChangeStatus(statementID, "editing")) {
                Main_tabcontrol.Effect = blur;
                Settings_grid.Effect = blur;
                Blackout_rectangle.Visibility = Visibility.Visible;
                DataRow statement = StatementsDT.Rows[Statements_stack.Children.IndexOf(((sender as Button).Parent as Grid))];
                string date = statement.ItemArray[3].ToString();
                date.Remove(date.Length - 3, 3);
                Edit_statement_date_label.Content = date;
                Edit_statement_name_label.Content =
                    statement.ItemArray[5].ToString() + " " + statement.ItemArray[4].ToString() + " " + statement.ItemArray[6].ToString();
                Edit_statement_birthdate_label.Content = statement.ItemArray[7].ToString().Remove(10, 8);
                Edit_statement_gender_label.Content = statement.ItemArray[8].ToString() == "False" ? "мужской" : "женский";
                Edit_statement_comment_textbox.TextChanged -= Edit_statement_TextBox_KeyDown;
                Edit_statement_comment_textbox.Text = statement.ItemArray[9].ToString();
                Edit_statement_comment_textbox.TextChanged += Edit_statement_TextBox_KeyDown;
                editingComment = statement.ItemArray[9].ToString();
                Edit_statement_window.Visibility = Visibility.Visible;
            }
        }

        private bool TryCloseSendSocket(Socket socket)
        {
            try {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
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
                //} while (buffer[0] != 4);
                TryCloseSendSocket(sendSocket);
                bufferList = buffer.ToList();
                bufferList.RemoveAt(0);

                switch (Encoding.UTF8.GetString(bufferList.ToArray()).Replace("\0", "")) {
                    case "free\\,\\;":
                        ServerSendMessage(CreateServerMessage("UPDATE statements SET status='" + status + "' WHERE id=" + statementID));
                        editingStatement = statementID;
                        return true;
                    case "browsing\\,\\;":
                    case "editing\\,\\;":
                        ShowNotification("Заявка сейчас просматривается");
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
        private int GetFreeID()
        {
            List<byte> bufferList;
            byte[] buffer = new byte[MAX_BYTES];
            if (ServerSendMessage(CreateServerMessage("SELECT id FROM statements"))) {
                //do {
                sendSocket.Receive(buffer);
                //} while (buffer[0] != 4);
                TryCloseSendSocket(sendSocket);
                bufferList = buffer.ToList();
                bufferList.RemoveAt(0);
                int indexOfNull = bufferList.IndexOf(0);
                bufferList.RemoveRange(indexOfNull, bufferList.Count - indexOfNull);
                DataTable idsDT = GetDataTable(bufferList.ToArray());
                List<int> ids = new List<int>();
                foreach (DataRow row in idsDT.Rows)
                    ids.Add(Convert.ToInt32(row.ItemArray[0]));

                // Поиск свободного ID, начиная с 0
                for (int id = 0; id < ids.Count; id ++) {
                    for (int j = id; j < ids.Count; j++) {
                        if (id == ids[j])
                            break;
                        else if (j == ids.Count - 1)
                            return id;
                    }
                    if (id == ids.Count - 1)
                        return id + 1;
                }
                return 0;
            }
            else {
                return -1;
            }
        }

        private void Remember_data_checkbox_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Default.Remember_Data = true;
            Settings.Default.Save();
        }
        private void Remember_data_checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.Default.Remember_Data = false;
            Settings.Default.Save();
        }
        private void Data_view_checkbox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[0]).ColumnDefinitions[0].Width = new GridLength(90);
            Settings.Default.Data_Viewed = Data_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void Data_view_checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[0]).ColumnDefinitions[0].Width = new GridLength(0);
            Settings.Default.Data_Viewed = Data_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void Name_view_checkbox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[0]).ColumnDefinitions[1].Width = new GridLength(160);
            Settings.Default.Name_Viewed = Name_view_checkbox.IsChecked ?? false;
            Settings.Default.Save();
        }
        private void Name_view_checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (Grid grid in Statements_stack.Children)
                ((Grid)grid.Children[0]).ColumnDefinitions[1].Width = new GridLength(0);
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

        private void TextBox_Changed(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Contains('\\'))
                e.Handled = true;
        }
        private void New_comment_TextBox_KeyDown(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            Symbols_label.Content = textBox.Text.Length.ToString() + "/1000";
            if (textBox.Text.Length == 999)
                Symbols_label.Foreground = Brushes.Gray;
            else if (textBox.Text.Length == 1000)
                Symbols_label.Foreground = Brushes.Crimson;
        }
        private void Edit_statement_TextBox_KeyDown(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox.Text == editingComment || textBox.Text.Length == 0)
                Send_Edit_statement_button.Visibility = Visibility.Hidden;
            else if (Send_Edit_statement_button.Visibility == Visibility.Hidden)
                Send_Edit_statement_button.Visibility = Visibility.Visible;
        }

        private void Send_statement_Click(object sender, RoutedEventArgs e)
        {
            if (New_statement_name_textbox.Text.Length > 0 &&
                New_statement_surname_textbox.Text.Length > 0 &&
                New_statement_birthday_datepicker.SelectedDate.HasValue &&
                New_statement_comment_textbox.Text.Length > 0) {
                // Отправка запроса на добавление новой заявки
                int freeID = GetFreeID();
                if (freeID != -1) {
                    string birthday = New_statement_birthday_datepicker.SelectedDate.ToString();
                    if (ServerSendMessage(CreateServerMessage(
                        "INSERT INTO statements(id, mac, name, surname, midname, birthday, gender, comment) VALUES(" +
                        freeID.ToString() + ", '" +
                        new string(macAddress) + "', '" +
                        New_statement_name_textbox.Text + "', '" +
                        New_statement_surname_textbox.Text + "', '" +
                        New_statement_midname_textbox.Text + "', '" +
                        birthday.Substring(6, 4) + "-" + birthday.Substring(3, 2) + "-" + birthday.Substring(0, 2) + "', " +
                        New_statement_gender_combobox.SelectedIndex.ToString() + ", '" +
                        New_statement_comment_textbox.Text + "')"))) {

                        Main_tabcontrol.SelectedIndex = 0;
                        if (!Settings.Default.Remember_Data) {
                            New_statement_name_textbox.Text = "";
                            New_statement_surname_textbox.Text = "";
                            New_statement_midname_textbox.Text = "";
                            New_statement_midname_textbox.Text = "";
                            New_statement_birthday_datepicker.SelectedDate = null;
                            New_statement_gender_combobox.SelectedIndex = 0;
                        }
                        New_statement_comment_textbox.Text = "";
                        ShowNotification("Заявка отправлена");
                    }
                    else {
                        ShowNotification("Ошибка соединения с сервером");
                    }
                }
                else {
                    ShowNotification("Ошибка соединения с сервером");
                }
            }
            else {
                ShowNotification("Заполните все необходимые поля");
            }
        }

        private void Edit_statement_Click(object sender, RoutedEventArgs e)
        {
            if (ServerSendMessage(CreateServerMessage(
                "UPDATE statements SET status='free', comment='" + Edit_statement_comment_textbox.Text + 
                "' WHERE id=" + editingStatement))) {

                editingStatement = -1;
                ShowNotification("Измененная заявка отправлена");
                Edit_statement_window.Visibility = Visibility.Hidden;
            }
            else {
                ShowEditErrorNotification("Ошибка соединения с сервером");
            }
        }

        private void Settings_Open(object sender, RoutedEventArgs e)
        {
            Main_tabcontrol.Effect = blur;
            Settings_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Settings_window.Visibility = Visibility.Visible;
        }

        private void Close_settings_Click(object sender, RoutedEventArgs e)
        {
            Settings_window.Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_tabcontrol.Effect = null;
            Settings_grid.Effect = null;
            Server_address.Text = Settings.Default.Server_Address;
        }

        private void Apply_settings_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.Server_Address = Server_address.Text;
            Settings.Default.Save();
            ShowSettingsNotification("Настройки применены");
        }

        private void IPAddress_Changed(object sender, TextChangedEventArgs e)
        {
            if (Server_address.Text != Settings.Default.Server_Address)
                Apply_settings_button.Visibility = Visibility.Visible;
            else {
                Apply_settings_button.Visibility = Visibility.Hidden;
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
                TryCloseSendSocket(serverUpdateSocket);
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
                receiveSocket.Bind(new IPEndPoint(ip, 706));
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

        private void Exit_button_Click(object sender, RoutedEventArgs e)
        {
            Main_tabcontrol.Effect = blur;
            Settings_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Exit_window.Visibility = Visibility.Visible;
        }
        private void Shutdown_button_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        private void Close_statement_Click(object sender, RoutedEventArgs e)
        {
            ServerSendMessage(CreateServerMessage("UPDATE statements SET status='free' WHERE id=" + editingStatement));
            editingStatement = -1;
            Edit_statement_window.Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_tabcontrol.Effect = null;
            Settings_grid.Effect = null;
        }
        private void Cancel_button_Click(object sender, RoutedEventArgs e)
        {
            Button cancel = (Button)sender;
            Grid tempGrid = (Grid)cancel.Parent;
            tempGrid = (Grid)tempGrid.Parent;
            tempGrid.Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_tabcontrol.Effect = null;
            Settings_grid.Effect = null;
        }
        private void Close(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (editingStatement != -1)
                ServerSendMessage(CreateServerMessage("UPDATE statements SET status='free' WHERE id=" + editingStatement));
            else
                ServerSendMessage(CreateServerMessage("SELECT 1"));
            if (receiveSocket.IsBound)
                receiveSocket.Dispose();
            if (remoteDataThread != null && remoteDataThread.IsAlive)
                remoteDataThread.Abort();
        }

        private void OK_button_Click(object sender, RoutedEventArgs e)
        {
            Button cancel = (Button)sender;
            ((Grid)cancel.Parent).Visibility = Visibility.Hidden;
            Blackout_rectangle.Visibility = Visibility.Hidden;
            Main_tabcontrol.Effect = null;
            Settings_grid.Effect = null;
        }

        private void OK_EditError_button_Click(object sender, RoutedEventArgs e)
        {
            Panel.SetZIndex(Notification_window, 3);
            Panel.SetZIndex(Blackout_rectangle, 2);
            Edit_statement_window.Effect = null;
            OK_button.Click -= OK_EditError_button_Click;
            OK_button.Click += OK_button_Click;
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

        private void ShowEditErrorNotification(string text)
        {
            Panel.SetZIndex(Blackout_rectangle, 4);
            Panel.SetZIndex(Notification_window, 5);
            Notification_label.Content = text;
            Edit_statement_window.Effect = blur;
            OK_button.Click -= OK_button_Click;
            OK_button.Click += OK_EditError_button_Click;
            Notification_window.Visibility = Visibility.Visible;
        }

        private void ShowNotification(string text)
        {
            Main_tabcontrol.Effect = blur;
            Settings_grid.Effect = blur;
            Blackout_rectangle.Visibility = Visibility.Visible;
            Notification_label.Content = text;
            Notification_window.Visibility = Visibility.Visible;
        }
    }
}
