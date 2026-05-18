using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using RabbitMQ.Client;

namespace ZavaBank.ZavaQueueBridge
{
    internal class Program
    {
        private static volatile bool _keepRunning = true;
        private static readonly object PendingLock = new object();
        private static readonly HashSet<string> PendingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static FileSystemWatcher _watcher;
        private static string _rabbitHost;
        private static int _rabbitPort;
        private static string _rabbitVHost;
        private static string _rabbitUser;
        private static string _rabbitPassword;
        private static string _filedropExchange;
        private static string _deadLetterExchange;
        private static string _dropPath;
        private static string _processedPath;
        private static string _errorPath;
        private static int _pollIntervalMs;

        private static void Main(string[] args)
        {
            LoadConfig();
            EnsureDirectories();
            ConfigureWatcher();

            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += delegate { _keepRunning = false; };

            Console.WriteLine("[QueueBridge] Starting file-to-queue bridge.");
            Console.WriteLine("[QueueBridge] DropPath={0}, Exchange={1}, RabbitMQ={2}:{3}", _dropPath, _filedropExchange, _rabbitHost, _rabbitPort);

            while (_keepRunning)
            {
                try
                {
                    PollFiles();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[QueueBridge] Poll cycle failed: " + ex.Message);
                }

                if (_keepRunning)
                {
                    Thread.Sleep(_pollIntervalMs);
                }
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }

            Console.WriteLine("[QueueBridge] Graceful shutdown complete.");
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("[QueueBridge] Ctrl+C received. Stopping...");
            _keepRunning = false;
            e.Cancel = true;
        }

        private static void LoadConfig()
        {
            _rabbitHost = Env("RABBITMQ_HOST", "RabbitMQ.Host", "rabbitmq");
            _rabbitPort = EnvInt("RABBITMQ_PORT", "RabbitMQ.Port", 5672);
            _rabbitVHost = Env("RABBITMQ_VHOST", "RabbitMQ.VHost", "/zavabank");
            _rabbitUser = Env("RABBITMQ_USER", "RabbitMQ.User", "zava_app");
            _rabbitPassword = Env("RABBITMQ_PASSWORD", "RabbitMQ.Password", "zava_pass");
            _filedropExchange = Env("FILEDROP_EXCHANGE", "RabbitMQ.FiledropExchange", "filedrop.events");
            _deadLetterExchange = Env("RABBITMQ_DLX", "RabbitMQ.DeadLetterExchange", "zava.dlx");
            _pollIntervalMs = EnvInt("WORKER_POLL_INTERVAL_MS", "Worker.PollIntervalMs", 5000);

            var configuredDropPath = Env("FILEDROP_PATH", "FileDrop.Path", "/shared/filedrop");
            if (Directory.Exists(configuredDropPath))
            {
                _dropPath = configuredDropPath;
            }
            else if (Directory.Exists("/data/filedrop"))
            {
                _dropPath = "/data/filedrop";
            }
            else
            {
                _dropPath = configuredDropPath;
            }

            _processedPath = Env("FILEDROP_PROCESSED_PATH", "FileDrop.ProcessedPath", Path.Combine(_dropPath, "processed"));
            _errorPath = Env("FILEDROP_ERROR_PATH", "FileDrop.ErrorPath", Path.Combine(_dropPath, "error"));
        }

        private static string Env(string envKey, string appSettingKey, string defaultValue)
        {
            var env = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env;
            }

            var appSetting = ConfigurationManager.AppSettings[appSettingKey];
            if (!string.IsNullOrWhiteSpace(appSetting))
            {
                return appSetting;
            }

            return defaultValue;
        }

        private static int EnvInt(string envKey, string appSettingKey, int defaultValue)
        {
            var value = Env(envKey, appSettingKey, defaultValue.ToString());
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(_dropPath);
            Directory.CreateDirectory(_processedPath);
            Directory.CreateDirectory(_errorPath);
        }

        private static void ConfigureWatcher()
        {
            _watcher = new FileSystemWatcher(_dropPath);
            _watcher.Filter = "*.*";
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
            _watcher.IncludeSubdirectories = false;
            _watcher.EnableRaisingEvents = true;
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            QueuePendingFile(e.FullPath);
        }

        private static void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            QueuePendingFile(e.FullPath);
        }

        private static void QueuePendingFile(string fullPath)
        {
            if (!IsCandidateFile(fullPath))
            {
                return;
            }

            lock (PendingLock)
            {
                PendingFiles.Add(fullPath);
            }
        }

        private static bool IsCandidateFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fileName = Path.GetFileName(path);
            if (fileName.Equals("processed", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var ext = Path.GetExtension(path);
            return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".dat", StringComparison.OrdinalIgnoreCase);
        }

        private static void PollFiles()
        {
            foreach (var file in Directory.GetFiles(_dropPath))
            {
                QueuePendingFile(file);
            }

            List<string> currentBatch;
            lock (PendingLock)
            {
                currentBatch = PendingFiles.ToList();
                PendingFiles.Clear();
            }

            if (currentBatch.Count == 0)
            {
                Console.WriteLine("[QueueBridge] No files available.");
                return;
            }

            using (var connection = CreateFactory().CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.ExchangeDeclare(_filedropExchange, ExchangeType.Topic, true, false, null);
                channel.ExchangeDeclare(_deadLetterExchange, ExchangeType.Fanout, true, false, null);

                foreach (var fullPath in currentBatch)
                {
                    if (!_keepRunning)
                    {
                        return;
                    }

                    if (!File.Exists(fullPath))
                    {
                        continue;
                    }

                    if (fullPath.StartsWith(_processedPath, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(_errorPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        ProcessSingleFile(channel, fullPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[QueueBridge] Failed file {0}: {1}", fullPath, ex.Message);
                        PublishDeadLetter(channel, fullPath, ex.Message);
                        MoveFile(fullPath, _errorPath);
                    }
                }
            }
        }

        private static ConnectionFactory CreateFactory()
        {
            return new ConnectionFactory
            {
                HostName = _rabbitHost,
                Port = _rabbitPort,
                VirtualHost = _rabbitVHost,
                UserName = _rabbitUser,
                Password = _rabbitPassword,
                AutomaticRecoveryEnabled = false
            };
        }

        private static void ProcessSingleFile(IModel channel, string fullPath)
        {
            WaitForFileReady(fullPath);
            var fileName = Path.GetFileName(fullPath);
            var records = ParseRecords(fullPath);

            var publishedCount = 0;
            foreach (var record in records)
            {
                var payload = BuildPayload(fileName, record);
                var body = Encoding.UTF8.GetBytes(payload);
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";

                var routingKey = BuildRoutingKey(fileName);
                channel.BasicPublish(_filedropExchange, routingKey, properties, body);
                publishedCount++;
            }

            Console.WriteLine("[QueueBridge] Published {0} message(s) from file {1}", publishedCount, fileName);
            MoveFile(fullPath, _processedPath);
        }

        private static void WaitForFileReady(string path)
        {
            const int maxAttempts = 5;
            var attempts = 0;
            while (attempts < maxAttempts)
            {
                try
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return;
                    }
                }
                catch
                {
                    attempts++;
                    Thread.Sleep(250);
                }
            }
        }

        private static List<Dictionary<string, string>> ParseRecords(string path)
        {
            var extension = Path.GetExtension(path);
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                throw new InvalidOperationException("File is empty.");
            }

            if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCsv(lines);
            }

            return ParseFixedWidth(lines);
        }

        private static List<Dictionary<string, string>> ParseCsv(string[] lines)
        {
            var records = new List<Dictionary<string, string>>();
            var headers = SplitCsvLine(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = SplitCsvLine(lines[i]);
                var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < headers.Length; c++)
                {
                    var header = headers[c];
                    var value = c < values.Length ? values[c] : string.Empty;
                    record[header] = value;
                }

                records.Add(record);
            }

            return records;
        }

        private static string[] SplitCsvLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    values.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            values.Add(current.ToString().Trim());
            return values.ToArray();
        }

        private static List<Dictionary<string, string>> ParseFixedWidth(string[] lines)
        {
            var records = new List<Dictionary<string, string>>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var padded = line.PadRight(80);
                var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "RecordType", padded.Substring(0, 10).Trim() },
                    { "CustomerId", padded.Substring(10, 20).Trim() },
                    { "AccountId", padded.Substring(30, 20).Trim() },
                    { "Amount", padded.Substring(50, 15).Trim() },
                    { "Details", padded.Substring(65).Trim() }
                };
                records.Add(record);
            }

            return records;
        }

        private static string BuildPayload(string fileName, Dictionary<string, string> record)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "sourceFile", fileName },
                { "processedAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") },
                { "record", record }
            };

            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(payload);
        }

        private static string BuildRoutingKey(string fileName)
        {
            var extension = Path.GetExtension(fileName).Trim('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = "unknown";
            }

            return "filedrop." + extension;
        }

        private static void PublishDeadLetter(IModel channel, string filePath, string error)
        {
            var payload = new Dictionary<string, object>
            {
                { "sourceFile", Path.GetFileName(filePath) },
                { "error", error },
                { "failedAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") }
            };

            var serializer = new JavaScriptSerializer();
            var body = Encoding.UTF8.GetBytes(serializer.Serialize(payload));
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";

            channel.BasicPublish(_deadLetterExchange, string.Empty, properties, body);
            Console.WriteLine("[QueueBridge] Dead-lettered failed file {0}", Path.GetFileName(filePath));
        }

        private static void MoveFile(string sourcePath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            var fileName = Path.GetFileName(sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, fileName);
            if (File.Exists(destinationPath))
            {
                var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                destinationPath = Path.Combine(destinationDirectory, stamp + "_" + fileName);
            }

            File.Move(sourcePath, destinationPath);
            Console.WriteLine("[QueueBridge] Moved {0} -> {1}", sourcePath, destinationPath);
        }
    }
}
