using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace _FM.Logger
{
    /// <summary>
    /// {RU} Асинхронный логгер с поддержкой записи в файл, отправки на сервер и вывода в Unity Console
    /// <para>{EN} Asynchronous logger with support for file writing, server transmission, and Unity Console output</para>
    /// </summary>
    public class Logger : ILogger
    {
        private readonly SemaphoreSlim _fileSemaphore = new(1, 1);
        private readonly SemaphoreSlim _serverSemaphore = new(1, 1);
        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private Config _config;
        private readonly string _logFilePath;
        private readonly string _logDirectory;
        private readonly DeviceInfo _deviceInfo;
        private readonly string _sessionId;
        private bool _isInitialized;
        private readonly Task _logProcessingTask;
        private StreamWriter _fileWriter;

        private const int _maxLogFiles = 7;
        private const int _logFlushIntervalMs = 1000;

        private readonly LogType _currentLogTypeUnity;
        private readonly LogType _currentLogTypeFile;
        private readonly LogType _currentLogTypeServer;
        private readonly LogLevel _currentLogLevelUnity;
        private readonly LogLevel _currentLogLevelFile;
        private readonly LogLevel _currentLogLevelServer;

        private struct LogEntry
        {
            public string Message;
            public LogLevel Level;
            public LogType Type;
            public object Details;
            public Exception Exception;
            public string CallerStackTrace;
            public LogDestination Destination;
        }

        [Flags]
        private enum LogDestination
        {
            File = 1,
            Server = 2
        }

        private readonly bool _isInitialize = false;
        private bool _canWriteToFile = false;
        private bool _isCapturingUnityLogs = false;
        private bool _isProcessingUnityLog = false; // Флаг для предотвращения рекурсии

        /// <summary>
        /// {RU} Вызывается при захвате лога (Unity или Logger). Dev Console подписывается для отображения с фильтрацией по Source и Level
        /// <para>{EN} Fired when a log is captured (Unity or Logger). Dev Console subscribes for display with filtering by Source and Level</para>
        /// </summary>
        public event Action<LogCaptureEntry> LogCaptured;

        /// <summary>
        /// {RU} Создает новый экземпляр логгера с заданной конфигурацией
        /// <para>{EN} Creates a new logger instance with the specified configuration</para>
        /// </summary>
        /// <param name="serverConfig">{RU} Конфигурация логгера с настройками уровней логирования<para>{EN} Logger configuration with log level settings</para></param>
        public Logger()
        {
            if (_isInitialize)
                return;

            _sessionId = Guid.NewGuid().ToString();

            try
            {
                _deviceInfo = DeviceInfoCollector.GetCurrentDeviceInfo();
            }
            catch (Exception)
            {
                _deviceInfo = new DeviceInfo();
            }

            try
            {
                _config = Resources.Load<Config>("Data/LoggerConfig.asset");
                _currentLogTypeUnity = _config != null ? _config.MinimalLogTypeUnity : default;
                _currentLogTypeFile = _config != null ? _config.MinimalLogTypeFile : default;
                _currentLogTypeServer = _config != null ? _config.MinimalLogTypeServer : default;
                _currentLogLevelUnity = _config != null ? _config.MinimalLogLevelUnity : default;
                _currentLogLevelFile = _config != null ? _config.MinimalLogLevelFile : default;
                _currentLogLevelServer = _config != null ? _config.MinimalLogLevelServer : default;
            }
            catch
            {
                Debug.LogWarning("Logger: Config not found at Data/LoggerConfig.asset, using defaults");
            }

            try
            {
#if UNITY_EDITOR
                string fileName = $"game_log_{DateTime.Now:yyyy-MM-dd}_pid{System.Diagnostics.Process.GetCurrentProcess().Id}.json";
#else
                string fileName = $"game_log_{DateTime.Now:yyyy-MM-dd}.json";
#endif
                _logDirectory = Path.Combine(Application.persistentDataPath, "Logs");
                _logFilePath = Path.Combine(_logDirectory, fileName);

                Directory.CreateDirectory(_logDirectory);
                CleanupOldLogFiles();
                _canWriteToFile = true;
            }
            catch (Exception ex)
            {
                _canWriteToFile = false;
                Debug.LogWarning($"[ServerLogger] File logging disabled — failed to initialize: {ex.Message}");
            }

            // Запуск фонового обработчика логов
            _logProcessingTask = Task.Run(() => ProcessLogQueueAsync(_cancellationTokenSource.Token));

            _isInitialized = true;
        }

        /// <summary>
        /// {RU} Включает перехват всех Unity Debug.Log/LogWarning/LogError и запись их в файл
        /// <para>{EN} Enables capturing all Unity Debug.Log/LogWarning/LogError and writing them to file</para>
        /// </summary>
        public void EnableUnityLogCapture()
        {
            if (_isCapturingUnityLogs)
                return;

            _isCapturingUnityLogs = true;
            Application.logMessageReceived += OnUnityLogReceived;
        }

        /// <summary>
        /// {RU} Отключает перехват Unity логов
        /// <para>{EN} Disables Unity log capturing</para>
        /// </summary>
        public void DisableUnityLogCapture()
        {
            if (!_isCapturingUnityLogs)
                return;

            _isCapturingUnityLogs = false;
            Application.logMessageReceived -= OnUnityLogReceived;
        }

        private void OnUnityLogReceived(string condition, string stackTrace, UnityEngine.LogType unityLogType)
        {
            // Предотвращаем рекурсию (наш логгер тоже вызывает Debug.Log)
            if (_isProcessingUnityLog)
                return;

            _isProcessingUnityLog = true;

            try
            {
                // Пропускаем логи от самого ServerLogger (по префиксу времени)
                if (string.IsNullOrEmpty(condition) || (condition.StartsWith("[") && condition.Contains("]")))
                    return;

                var (level, type) = ConvertUnityLogType(unityLogType);

                LogCaptured?.Invoke(new LogCaptureEntry(LogCaptureSource.Unity, condition, stackTrace, level, type, unityLogType));

                // Добавляем в очередь напрямую (без вывода в консоль, чтобы избежать дублирования)
                LogDestination destination = 0;

                if (_canWriteToFile && level >= _currentLogLevelFile && type >= _currentLogTypeFile)
                {
                    destination |= LogDestination.File;
                }
                if (level >= _currentLogLevelServer && type >= _currentLogTypeServer)
                {
                    destination |= LogDestination.Server;
                }

                if (destination != 0)
                {
                    _logQueue.Enqueue(new LogEntry
                    {
                        Message = $"[Unity] {condition}",
                        Level = level,
                        Type = type,
                        Details = string.IsNullOrEmpty(stackTrace) ? null : stackTrace,
                        Exception = null,
                        Destination = destination
                    });
                }
            }
            finally
            {
                _isProcessingUnityLog = false;
            }
        }

        private (LogLevel level, LogType type) ConvertUnityLogType(UnityEngine.LogType unityLogType)
        {
            return unityLogType switch
            {
                UnityEngine.LogType.Error => (LogLevel.Error, LogType.Error),
                UnityEngine.LogType.Assert => (LogLevel.Error, LogType.Error),
                UnityEngine.LogType.Warning => (LogLevel.Warning, LogType.Warning),
                UnityEngine.LogType.Log => (LogLevel.Info, LogType.Info),
                UnityEngine.LogType.Exception => (LogLevel.Critical, LogType.Error),
                _ => (LogLevel.Info, LogType.Info)
            };
        }

        public void SetServerConfig(Config serverConfig)
        {
            _config = serverConfig;
        }

        /// <summary>
        /// Удаляет старые файлы логов, оставляя только последние MaxLogFiles файлов
        /// </summary>
        private void CleanupOldLogFiles()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "game_log_*.json");
                if (logFiles.Length <= _maxLogFiles)
                    return;

                var sortedFiles = logFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(_maxLogFiles)
                    .ToList();

                foreach (var fileInfo in sortedFiles)
                {
                    try
                    {
                        fileInfo.Delete();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to delete old log file {fileInfo.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to cleanup old log files: {ex.Message}");
            }
        }

        #region Extensions Action
        /// <summary>
        /// {RU} Записывает лог уровня Trace (максимальная детализация) для диагностики
        /// <para>{EN} Writes a Trace level log (most detailed) for diagnostics</para>
        /// </summary>
        /// <param name="message">{RU} Текст сообщения<para>{EN} Message text</para></param>
        /// <param name="details">{RU} Дополнительные данные (будут сериализованы в JSON)<para>{EN} Additional data (will be serialized to JSON)</para></param>
        /// <param name="exception">{RU} Исключение, если есть<para>{EN} Exception, if any</para></param>
        public void LogTrace(string message, object details = null, Exception exception = null)
        {
            Log(message, LogLevel.Trace, LogType.Info, details, exception);
        }

        /// <summary>
        /// {RU} Записывает лог для разработки и тестирования. Временный метод для логирования во время разработки, который можно легко найти и удалить после тестирования
        /// <para>{EN} Writes a log for development and testing. Temporary method for logging during development that can be easily found and removed after testing</para>
        /// </summary>
        /// <param name="message">{RU} Текст сообщения<para>{EN} Message text</para></param>
        /// <param name="details">{RU} Дополнительные данные (будут сериализованы в JSON)<para>{EN} Additional data (will be serialized to JSON)</para></param>
        /// <param name="exception">{RU} Исключение, если есть<para>{EN} Exception, if any</para></param>
        public void LogDevelop(string message, object details = null, Exception exception = null)
        {
            Log(message, LogLevel.Develop, LogType.Info, details, exception);
        }

        /// <summary>
        /// {RU} Записывает информационный лог для отслеживания общих событий приложения
        /// <para>{EN} Writes an informational log to track general application events</para>
        /// </summary>
        /// <param name="message">{RU} Текст сообщения<para>{EN} Message text</para></param>
        /// <param name="details">{RU} Дополнительные данные (будут сериализованы в JSON)<para>{EN} Additional data (will be serialized to JSON)</para></param>
        /// <param name="exception">{RU} Исключение, если есть<para>{EN} Exception, if any</para></param>
        public void LogInfo(string message, object details = null, Exception exception = null)
        {
            Log(message, LogLevel.Info, LogType.Info, details, exception);
        }

        /// <summary>
        /// {RU} Записывает предупреждение о потенциальных проблемах
        /// <para>{EN} Writes a warning about potential issues</para>
        /// </summary>
        /// <param name="message">{RU} Текст сообщения<para>{EN} Message text</para></param>
        /// <param name="details">{RU} Дополнительные данные (будут сериализованы в JSON)<para>{EN} Additional data (will be serialized to JSON)</para></param>
        /// <param name="exception">{RU} Исключение, если есть<para>{EN} Exception, if any</para></param>
        public void LogWarning(string message, object details = null, Exception exception = null)
        {
            if (exception == null && details == null)
            {
                details = Environment.StackTrace;
            }
            Log(message, LogLevel.Warning, LogType.Warning, details, exception);
        }

        /// <summary>
        /// {RU} Записывает ошибку. Автоматически добавляет стек вызовов если не передано исключение
        /// <para>{EN} Writes an error. Automatically adds stack trace if no exception is provided</para>
        /// </summary>
        /// <param name="message">{RU} Текст сообщения<para>{EN} Message text</para></param>
        /// <param name="details">{RU} Дополнительные данные (будут сериализованы в JSON)<para>{EN} Additional data (will be serialized to JSON)</para></param>
        /// <param name="exception">{RU} Исключение, если есть<para>{EN} Exception, if any</para></param>
        public void LogError(string message, object details = null, Exception exception = null)
        {
            string callerStack = null;
            if (exception == null)
            {
                exception = new Exception(message);
                callerStack = GetCallerStackTrace(skipFrames: 2);
                if (details == null)
                    details = callerStack;
            }
            else if (string.IsNullOrEmpty(exception.StackTrace))
            {
                callerStack = GetCallerStackTrace(skipFrames: 2);
            }

            Log(message, LogLevel.Error, LogType.Error, details, exception, callerStack);
        }

        /// <summary>
        /// {RU} Записывает критическую ошибку, требующую немедленного внимания. Автоматически добавляет стек вызовов
        /// <para>{EN} Writes a critical error requiring immediate attention. Automatically adds stack trace</para>
        /// </summary>
        /// <param name="message">{RU} Текст сообщения<para>{EN} Message text</para></param>
        /// <param name="details">{RU} Дополнительные данные (будут сериализованы в JSON)<para>{EN} Additional data (will be serialized to JSON)</para></param>
        /// <param name="exception">{RU} Исключение, если есть<para>{EN} Exception, if any</para></param>
        public void LogCritical(string message, object details = null, Exception exception = null)
        {
            string callerStack = null;
            if (exception == null)
            {
                exception = new Exception(message);
                callerStack = GetCallerStackTrace(skipFrames: 2);
                if (details == null)
                    details = callerStack;
            }
            else if (string.IsNullOrEmpty(exception.StackTrace))
            {
                callerStack = GetCallerStackTrace(skipFrames: 2);
            }

            Log(message, LogLevel.Critical, LogType.Error, details, exception, callerStack);
        }

        private string GetStackTrace(Exception exception)
        {
            var stackTrace = new System.Diagnostics.StackTrace(exception, true);
            return stackTrace.ToString();
        }

        /// <summary>Захватывает стек вызовов с места вызова (needFileInfo = true для путей и номеров строк).</summary>
        private static string GetCallerStackTrace(int skipFrames)
        {
            var st = new System.Diagnostics.StackTrace(skipFrames, true);
            return st.ToString();
        }
        #endregion

        private void Log(string message, LogLevel level, LogType type, object details = null, Exception exception = null, string callerStackTrace = null)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("ServerLogger is not initialized. Call Initialize() first.");
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (level >= _currentLogLevelUnity && type >= _currentLogTypeUnity)
            {
                LogToConsole(message, level, type);
            }
#endif

            var stackTrace = exception != null ? GetStackTrace(exception) : (callerStackTrace ?? details?.ToString());
            LogCaptured?.Invoke(new LogCaptureEntry(LogCaptureSource.Logger, message, stackTrace, level, type));

            // Определяем куда нужно отправить лог
            LogDestination destination = 0;

            if (_canWriteToFile && level >= _currentLogLevelFile && type >= _currentLogTypeFile)
            {
                destination |= LogDestination.File;
            }
            if (level >= _currentLogLevelServer && type >= _currentLogTypeServer)
            {
                destination |= LogDestination.Server;
            }

            // Добавляем в очередь только если есть хотя бы одно направление
            if (destination != 0)
            {
                _logQueue.Enqueue(new LogEntry
                {
                    Message = message,
                    Level = level,
                    Type = type,
                    Details = details,
                    Exception = exception,
                    CallerStackTrace = callerStackTrace,
                    Destination = destination
                });
            }
        }

        /// <summary>
        /// Фоновый обработчик очереди логов
        /// </summary>
        private async Task ProcessLogQueueAsync(CancellationToken cancellationToken)
        {
            var lastFlushTime = DateTime.UtcNow;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Обрабатываем все логи из очереди
                    while (_logQueue.TryDequeue(out LogEntry entry))
                    {
                        if ((entry.Destination & LogDestination.File) != 0)
                        {
                            await LogToFileAsync(entry);
                        }

                        if ((entry.Destination & LogDestination.Server) != 0)
                        {
                            await LogToServerAsync(entry);
                        }
                    }

                    // Периодический сброс буфера в файл
                    if ((DateTime.UtcNow - lastFlushTime).TotalMilliseconds >= _logFlushIntervalMs)
                    {
                        await FlushFileWriterAsync();
                        lastFlushTime = DateTime.UtcNow;
                    }

                    // Небольшая задержка чтобы не нагружать CPU
                    await Task.Delay(50, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error in log processing loop: {ex.Message}");
                }
            }

            // Обрабатываем оставшиеся логи при завершении
            while (_logQueue.TryDequeue(out LogEntry entry))
            {
                try
                {
                    if ((entry.Destination & LogDestination.File) != 0)
                    {
                        await LogToFileAsync(entry);
                    }

                    if ((entry.Destination & LogDestination.Server) != 0)
                    {
                        await LogToServerAsync(entry);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error processing final log entry: {ex.Message}");
                }
            }

            await FlushFileWriterAsync();
        }

        private void LogToConsole(string message, LogLevel level, LogType type)
        {
            string formattedMessage = FormatConsoleMessage(message, level);

            switch (type)
            {
                case LogType.Info:
                    Debug.Log(formattedMessage);
                    break;
                case LogType.Warning:
                    Debug.LogWarning(formattedMessage);
                    break;
                case LogType.Error:
                    Debug.LogError(formattedMessage);
                    break;
            }
        }

        private async Task LogToFileAsync(LogEntry entry)
        {
            if (!_canWriteToFile)
                return;

            await _fileSemaphore.WaitAsync();

            try
            {
                // Ленивая инициализация StreamWriter
                if (_fileWriter == null)
                {
                    _fileWriter = new StreamWriter(_logFilePath, append: true)
                    {
                        AutoFlush = false // Используем ручной flush для производительности
                    };
                }

                var errorReport = GenerateErrorReport(entry.Message, entry.Level, entry.Type, entry.Details, entry.Exception, entry.CallerStackTrace);
                string json = JsonUtility.ToJson(errorReport, true);
                await _fileWriter.WriteLineAsync(json);
            }
            catch (Exception ex)
            {
                // Отключаем запись в файл при критической ошибке (нехватка места, etc.)
                _canWriteToFile = false;
                Debug.LogWarning($"[ServerLogger] File logging disabled — write error: {ex.Message}");
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }

        private async Task LogToServerAsync(LogEntry entry)
        {
            await _serverSemaphore.WaitAsync();

            try
            {
                var errorReport = GenerateErrorReport(entry.Message, entry.Level, entry.Type, entry.Details, entry.Exception, entry.CallerStackTrace);

                // TODO: Раскомментировать когда сервер готов
                // await NetworkManager.Instance.SendErrorReportAsync(errorReport);

            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to send to server: {ex.Message}");
            }
            finally
            {
                _serverSemaphore.Release();
            }
        }

        /// <summary>
        /// {RU} Принудительно записывает все логи из очереди в файл, минуя проверку UNITY_EDITOR.
        /// Используется перед отправкой файла на сервер
        /// <para>{EN} Force-flushes all queued logs to file, bypassing UNITY_EDITOR check.
        /// Used before sending the log file to server</para>
        /// </summary>
        private async Task ForceFlushToFileAsync()
        {
            // Ждём пока очередь опустеет (обычный процессинг)
            int maxWaitMs = 5000;
            int waitedMs = 0;
            while (!_logQueue.IsEmpty && waitedMs < maxWaitMs)
            {
                await Task.Delay(50);
                waitedMs += 50;
            }

            // Принудительно сбрасываем буфер
            await FlushFileWriterAsync();
        }

        /// <summary>
        /// Сбрасывает буфер StreamWriter в файл
        /// </summary>
        private async Task FlushFileWriterAsync()
        {
            if (_fileWriter == null)
                return;

            await _fileSemaphore.WaitAsync();
            try
            {
                await _fileWriter.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to flush log file: {ex.Message}");
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }

        private string FormatConsoleMessage(string message, LogLevel level)
        {
            string levelTag = GetLevelTag(level);
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            return $"[{timestamp}] [{levelTag}] {message}";
        }

        private string GetLevelTag(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Develop => "DEV",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "LOG"
            };
        }

        private ErrorReport GenerateErrorReport(string message, LogLevel level, LogType type, object details = null, Exception exception = null, string callerStackTrace = null)
        {
            string localStackTrace = "";

            if (level >= LogLevel.Error)
            {
                localStackTrace = exception?.StackTrace;
                if (string.IsNullOrWhiteSpace(localStackTrace) && !string.IsNullOrWhiteSpace(callerStackTrace))
                    localStackTrace = callerStackTrace;
                if (string.IsNullOrWhiteSpace(localStackTrace))
                    localStackTrace = Environment.StackTrace;
            }

            return new ErrorReport(
                message: message,
                level: level.ToString(),
                timestamp: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                type: type.ToString(),
                deviceInfo: _deviceInfo,
                details: details,
                stackTrace: localStackTrace,
                sessionId: GetSessionId()
                );
        }

        private string GetSessionId()
        {
            return _sessionId;
        }

        /// <summary>
        /// {RU} Ожидает завершения записи всех логов из очереди. Используйте перед критическими операциями
        /// <para>{EN} Waits for all logs in the queue to be written. Use before critical operations</para>
        /// </summary>
        /// <returns>{RU} Задача, представляющая асинхронную операцию<para>{EN} Task representing the asynchronous operation</para></returns>
        public async Task FlushAsync()
        {
            if (!_isInitialized)
                return;

            // Ждем пока очередь опустеет
            int maxWaitMs = 5000; // Максимум 5 секунд
            int waitedMs = 0;
            while (!_logQueue.IsEmpty && waitedMs < maxWaitMs)
            {
                await Task.Delay(50);
                waitedMs += 50;
            }

            // Принудительно сбрасываем буфер
            await FlushFileWriterAsync();
        }

        /// <summary>
        /// {RU} Отправляет файл логов текущей сессии на сервер. Перед отправкой завершает запись всех логов в файл
        /// <para>{EN} Sends the current session log file to the server. Flushes all pending logs before sending</para>
        /// </summary>
        /// <returns>{RU} true если отправка успешна, false в случае ошибки<para>{EN} true if upload succeeded, false on error</para></returns>
        public async Task<bool> SendLogFileAsync()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[ServerLogger] Cannot send log file: logger is not initialized.");
                return false;
            }

            if (_config == null || string.IsNullOrEmpty(_config.DevelopmentServerUrl))
            {
                Debug.LogWarning("[ServerLogger] Cannot send log file: server URL is not configured.");
                return false;
            }

            try
            {
                // 1. Принудительно записываем все логи из очереди в файл (в т.ч. в Editor)
                await ForceFlushToFileAsync();

                // 2. Закрываем StreamWriter чтобы файл был доступен для чтения
                await _fileSemaphore.WaitAsync();
                try
                {
                    if (_fileWriter != null)
                    {
                        await _fileWriter.FlushAsync();
                        _fileWriter.Dispose();
                        _fileWriter = null;
                    }
                }
                finally
                {
                    _fileSemaphore.Release();
                }

                // 3. Читаем файл
                if (!File.Exists(_logFilePath))
                {
                    Debug.LogWarning("[ServerLogger] Log file does not exist, nothing to send.");
                    return false;
                }

                string logContent = await Task.Run(() => File.ReadAllText(_logFilePath));
                if (string.IsNullOrEmpty(logContent))
                {
                    Debug.LogWarning("[ServerLogger] Log file is empty, nothing to send.");
                    return false;
                }

                // 4. Формируем JSON-тело запроса
                string fileName = Path.GetFileName(_logFilePath);
                var requestBody = new UploadLogRequest
                {
                    SessionId = _sessionId,
                    FileName = fileName,
                    LogContent = logContent,
                    DeviceInfo = _deviceInfo
                };

                string json = JsonUtility.ToJson(requestBody);

                // 5. Отправляем на сервер (формат URL как в NetworkApiClient)
                string url = $"{_config.DevelopmentServerUrl}/api/Logs/Upload";
                Debug.Log($"[ServerLogger] Sending log to: {url}");
                bool result = await SendPostRequestAsync(url, json);

                if (result)
                {
                    Debug.Log("[ServerLogger] Log file sent to server successfully.");
                }
                else
                {
                    Debug.LogWarning("[ServerLogger] Failed to send log file to server.");
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerLogger] Error sending log file: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task<bool> SendPostRequestAsync(string url, string jsonBody)
        {
            try
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

                using var request = new UnityWebRequest(url, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 30;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return true;
                }

                Debug.LogWarning($"[ServerLogger] Upload failed: {request.responseCode} - {request.error}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerLogger] Upload exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// {RU} Освобождает ресурсы и корректно завершает работу логгера. Должен вызываться при закрытии приложения
        /// <para>{EN} Releases resources and properly shuts down the logger. Should be called when closing the application</para>
        /// </summary>
        public void Dispose()
        {
            if (!_isInitialized)
                return;

            try
            {
                // Отключаем перехват Unity логов
                DisableUnityLogCapture();

                // Останавливаем фоновый процесс
                _cancellationTokenSource.Cancel();

                // Ждем завершения обработки логов (максимум 5 секунд)
                _logProcessingTask?.Wait(TimeSpan.FromSeconds(5));

                // Освобождаем StreamWriter
                _fileWriter?.Flush();
                _fileWriter?.Dispose();
                _fileWriter = null;

                // Освобождаем семафоры
                _fileSemaphore?.Dispose();
                _serverSemaphore?.Dispose();
                _cancellationTokenSource?.Dispose();

                _isInitialized = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during ServerLogger disposal: {ex.Message}");
            }
        }
    }
}
