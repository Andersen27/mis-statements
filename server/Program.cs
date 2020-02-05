using MIS_Statements_Server.Properties;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace MIS_Statements_Server {
    class Program {
        static DataBaseConnection dataBaseConnection;
        static Socket receiveSocket, sendSocket;
        static List<ClientConnection> clients;
        static string consoleOutput;
        static int viewersNumber, clientsNumber;
        static bool lostConnection;
        static int iterationNumber;
        static string deleteIds = "";
        static List<string> macs = new List<string>();
        const int UPDATE_CLIENTS_ITERATION_STEP = 1;
        const int UPDATE_DATABASE_LOST_CONNECTION_SECONDS = 5;

        static void Main(string[] args)
        {
            dataBaseConnection = new DataBaseConnection();
            receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clients = new List<ClientConnection>();
            consoleOutput = "";
            lostConnection = false;
            iterationNumber = 0;

            Console.ForegroundColor = ConsoleColor.Cyan;
            dataBaseConnection = new DataBaseConnection();

            // Подключение к базе данных
            byte connectionMode = 255; // 0 - Первое подключение, 1 - Подключение к БД в памяти, 2 - Новое подключение
            if (Settings.Default.DB_Server != "") {
                do {
                    Console.WriteLine("[1 - Подключиться к " + Settings.Default.DB_Server + ", 2 - Создать новое подключение]:");
                    ConsoleKeyInfo cki = Console.ReadKey();
                    if (cki.KeyChar.Equals('1'))
                        connectionMode = 1;
                    else if (cki.KeyChar.Equals('2'))
                        connectionMode = 2;
                    Console.Clear();
                } while (connectionMode != 1 && connectionMode != 2);
            }
            else {
                connectionMode = 0;
            }
            if (connectionMode == 1) {
                if (!dataBaseConnection.CreateConnection())
                    connectionMode = 2;
            }
            if (connectionMode == 0 || connectionMode == 2) {
                string server, port, username, password, database;
                do {
                    if (connectionMode == 0)
                        Console.WriteLine("Настройка подключения к БД:");
                    else
                        Console.WriteLine("Настройка нового подключения к БД:");
                    Console.Write("server: ");
                    server = Console.ReadLine();
                    Console.Write("port: ");
                    port = Console.ReadLine();
                    Console.Write("username: ");
                    username = Console.ReadLine();
                    Console.Write("password: ");
                    password = Console.ReadLine();
                    Console.Write("database: ");
                    database = Console.ReadLine();
                } while (!dataBaseConnection.CreateConnection(server, port, username, password, database));
                Console.Clear();
            }

            consoleOutput = "[" + DateTime.Now.ToShortDateString() + ", " +
                DateTime.Now.ToLongTimeString() + "] " +
                "Соединение с базой данных установлено.\n";
            Console.Write(consoleOutput);
            Console.Beep();

            // Соединение со всеми клиентами
            receiveSocket.Bind(new IPEndPoint(IPAddress.Any, 708));
            receiveSocket.Listen(5000);

            while (true) {
                if (!lostConnection) {
                    // Проверка подключенных клиентов и удаление отключенных
                    if (iterationNumber == UPDATE_CLIENTS_ITERATION_STEP) {
                        viewersNumber = 0;
                        clientsNumber = 0;
                        for (int i = 0; i < clients.Count; i++) {
                            if (ClientSendUpdateMessage(clients[i], new byte[1])) {
                                if (clients[i].clientType == 1)
                                    viewersNumber++;
                                else
                                    clientsNumber++;
                            }
                            else {
                                clients.Remove(clients[i]);
                                i--;
                            }
                        }
                        // Обновление информации о подключениях в консоли
                        {
                            Console.Clear();
                            Console.Write(consoleOutput +
                                "\n==============================\n" +
                                "[" + DateTime.Now.ToShortDateString() + ", " + DateTime.Now.ToLongTimeString() + "]\n" +
                                "Viewers: " + viewersNumber + ", Clients: " + clientsNumber +
                                "\n==============================\n");
                        }
                        iterationNumber = 0;
                    }
                    iterationNumber++;

                    // Принятие данных от старого или нового клиента
                    if (clients.Count < 5000) {
                        ClientConnection currentClient = new ClientConnection(receiveSocket.Accept());
                        if (currentClient.clientType != 0) {
                            if (clients.Count > 0) {
                                for (int i = 0; i < clients.Count; i++) {
                                    if (clients[i] == currentClient) {
                                        clients[i].query = currentClient.query;
                                        break;
                                    }
                                    else if (i == clients.Count - 1) {
                                        clients.Add(currentClient);
                                        break;
                                    }
                                }
                            }
                            else {
                                clients.Add(currentClient);
                            }
                            // Формирование SQL-запроса, полученного от клиента
                            string query = currentClient.query;
                            // Обработка запроса и отправка данных View и Client приложениям
                            try {
                                if (query.Substring(0, 6).Equals("SELECT")) {
                                    // Если клиент запрашивает данные
                                    DataTable dataTable = new DataTable();
                                    MySqlDataAdapter dataAdapter = new MySqlDataAdapter(query, dataBaseConnection.connection);
                                    dataAdapter.Fill(dataTable);
                                    ClientSendMessage(currentClient, ServerMessage(4, dataTable));
                                }
                                else {
                                    // Если клиент изменяет данные

                                    dataBaseConnection.OpenConnection();
                                    // Заранее сохраняем MAC-адресов, прежде чем заявки будут удалены
                                    if (query.Substring(0, 6).Equals("DELETE")) {
                                        deleteIds = query.Substring(query.IndexOf("IN") + 3);
                                        DataTable dataTable = new DataTable();
                                        MySqlDataAdapter dataAdapter = new MySqlDataAdapter(
                                            "SELECT DISTINCT mac FROM statements WHERE id IN(" + deleteIds.Remove(deleteIds.Length - 1) + ")", dataBaseConnection.connection);
                                        dataAdapter.Fill(dataTable);
                                        macs.Clear();
                                        for (int i = 0; i < dataTable.Rows.Count; i++)
                                            macs.Add(dataTable.Rows[i][0].ToString());
                                    }
                                    
                                    MySqlCommand command = new MySqlCommand(query, dataBaseConnection.connection);
                                    if (command.ExecuteNonQuery() > 0) {
                                        // Рассылка изменений во View и Client приложения
                                        if (query.Substring(0, 6).Equals("DELETE")) {
                                            // Была удалена заявка                                            
                                            foreach (ClientConnection cc in clients) {
                                                if (cc.clientType == 1 || macs.Contains(cc.mac)) {
                                                    ClientSendUpdateMessage(cc, ServerMessage(3, deleteIds.Remove(deleteIds.Length - 1)));
                                                }
                                            }
                                        }
                                        else {
                                            string updateId;
                                            DataTable dataTable = new DataTable();
                                            if (query.Substring(0, 6).Equals("INSERT")) {
                                                // Была добавлена заявка
                                                int valuesIndex = query.IndexOf("VALUES");
                                                updateId = query.Substring(valuesIndex + 7, query.IndexOf(',', valuesIndex) - valuesIndex - 7);
                                                MySqlDataAdapter dataAdapter = new MySqlDataAdapter(
                                                "SELECT * FROM statements WHERE id=" + updateId, dataBaseConnection.connection);
                                                dataAdapter.Fill(dataTable);
                                                macs.Clear();
                                                macs.Add(dataTable.Rows[0][2].ToString());
                                                foreach (ClientConnection cc in clients) {
                                                    if (cc.clientType == 1 || cc.mac == macs[0]) {
                                                        ClientSendUpdateMessage(cc, ServerMessage(1, dataTable));
                                                    }
                                                }
                                            }
                                            else if (query.Contains("comment")) {
                                                // Был отредактирован комментарий
                                                updateId = query.Substring(query.IndexOf("id=") + 3);
                                                MySqlDataAdapter dataAdapter = new MySqlDataAdapter(
                                                "SELECT id, mac, comment FROM statements WHERE id=" + updateId, dataBaseConnection.connection);
                                                dataAdapter.Fill(dataTable);
                                                macs.Clear();
                                                macs.Add(dataTable.Rows[0][1].ToString());
                                                foreach (ClientConnection cc in clients) {
                                                    if (cc.clientType == 1 || cc.mac == macs[0]) {
                                                        ClientSendUpdateMessage(cc, ServerMessage(2, dataTable));
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (MySqlException exception) {
                                consoleOutput += "[" + DateTime.Now.ToShortDateString() + ", " +
                                DateTime.Now.ToLongTimeString() + "] " +
                                "Ошибка MySQL:\n" + exception.Message + "\nПереподключение...\n";
                                Console.Clear();
                                Console.WriteLine(consoleOutput);
                                lostConnection = true;
                            }
                            finally {
                                dataBaseConnection.CloseConnection();
                            }
                        }
                    }
                }
                else {
                    Thread.Sleep(UPDATE_DATABASE_LOST_CONNECTION_SECONDS * 1000);
                    try {
                        dataBaseConnection.OpenConnection();
                        if (dataBaseConnection.connection.State == ConnectionState.Open) {
                            consoleOutput += "[" + DateTime.Now.ToShortDateString() + ", " +
                                DateTime.Now.ToLongTimeString() + "] " + 
                                "Соединение с базой данных восстановлено.\n";
                            lostConnection = false;
                        }
                    }
                    catch { }
                }
            }
        }

        static byte[] ServerMessage(byte type, DataTable data)
        {
            // type: 1 - новые данные, 2 - обновленные данные, 3 - ID удаленных данных, 4 - данные по запросу
            StringBuilder builder = new StringBuilder();
            foreach (DataRow row in data.Rows) {
                for (int i = 0; i < row.ItemArray.Length; i++) {
                    string rowText = row.ItemArray[i].ToString();
                    builder.Append(rowText + "\\,");
                }
                builder.Append("\\;");
            }
            List<byte> result = Encoding.UTF8.GetBytes(builder.ToString()).ToList();
            result.Insert(0, type);
            return result.ToArray();
        }
        static byte[] ServerMessage(byte type, string ids)
        {
            // type: 1 - новые данные, 2 - обновленные данные, 3 - ID удаленных данных, 4 - данные по запросу
            List<byte> result = Encoding.UTF8.GetBytes(ids.Replace(" ", "").Replace(",", "\\,\\;") + "\\,\\;").ToList();
            
            result.Insert(0, type);
            return result.ToArray();
        }
        static bool ClientSendMessage(ClientConnection client, byte[] message)
        {
            try {
                client.socket.Send(message);
                client.socket.Shutdown(SocketShutdown.Both);
                return true;
            }
            catch {
                sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                return false;
            }
        }
        static bool ClientSendUpdateMessage(ClientConnection client, byte[] message)
        {
            try {
                sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //sendSocket.SendTimeout = 5000;
                if (client.clientType == 1)
                    sendSocket.Connect((client.socket.RemoteEndPoint as IPEndPoint).Address, 707);
                else
                    sendSocket.Connect((client.socket.RemoteEndPoint as IPEndPoint).Address, 706);
                sendSocket.Send(message);
                sendSocket.Shutdown(SocketShutdown.Both);
                sendSocket.Close();
                return true;
            }
            catch {
                sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                return false;
            }
        }
    }
}
