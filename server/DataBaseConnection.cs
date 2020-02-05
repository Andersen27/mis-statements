using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MIS_Statements_Server.Properties;

namespace MIS_Statements_Server {
    class DataBaseConnection {
        public MySqlConnection connection { get; private set; }

        public bool CreateConnection()
        {
            connection = new MySqlConnection(
                "server=" + Settings.Default.DB_Server +
                ";port=" + Settings.Default.DB_Port +
                ";username=" + Settings.Default.DB_Username +
                ";password=" + Settings.Default.DB_Password +
                ";database=" + Settings.Default.DB_database);
            try {
                connection.Open();
            }
            catch (MySqlException exception) {
                Console.WriteLine(exception.Message);
                return false;
            }
            connection.Close();
            return true;
        }
        public bool CreateConnection(string server, string port, string username, string password, string database)
        {
            connection = new MySqlConnection(
                "server=" + server +
                ";port=" + port +
                ";username=" + username +
                ";password=" + password +
                ";database=" + database);
            try {
                connection.Open();
            }
            catch (MySqlException exception) {
                Console.WriteLine(exception.Message);
                return false;
            }
            if (connection.State == System.Data.ConnectionState.Closed) {
                connection = null;
                return false;
            }
            else {
                connection.Close();
                Settings.Default.DB_Server = server;
                Settings.Default.DB_Port = port;
                Settings.Default.DB_Username = username;
                Settings.Default.DB_Password = password;
                Settings.Default.DB_database = database;
                Settings.Default.Save();
                return true;
            }
        }
        public void OpenConnection()
        {
            if (connection.State == System.Data.ConnectionState.Closed)
                connection.Open();
        }
        public void CloseConnection()
        {
            if (connection.State == System.Data.ConnectionState.Open)
                connection.Close();
        }
    }
}
