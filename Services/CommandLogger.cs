using System;
using System.IO;

namespace HouseholdMS.Services
{
    public class CommandLogger
    {
        public string LogDirectory { get; private set; }
        private readonly string _path;
        public event Action<string> OnLog;

        public CommandLogger()
        {
            LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IT8615Logs");
            Directory.CreateDirectory(LogDirectory);
            _path = Path.Combine(LogDirectory, "log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        }

        public void Log(string line)
        {
            string s = DateTime.Now.ToString("HH:mm:ss.fff") + " " + line;
            File.AppendAllText(_path, s + Environment.NewLine);
            if (OnLog != null) OnLog(s);
        }
    }
}
