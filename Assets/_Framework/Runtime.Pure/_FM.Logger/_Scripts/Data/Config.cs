using UnityEngine;

namespace _FM.Logger
{
    /// <summary>
    /// {RU} Конфигурация логгера с настройками уровней логирования для разных назначений
    /// <para>{EN} Logger configuration with log level settings for different destinations</para>
    /// </summary>
    [CreateAssetMenu(fileName = "ServerConfig", menuName = "ServerConfig")]
    public class Config : ScriptableObject
    {
        [Header("Unity Console Logging")]
        [SerializeField] private LogType minimalLogTypeUnity;
        [SerializeField] private LogLevel minimalLogLevelUnity;

        [Header("File Logging")]
        [SerializeField] private LogType minimalLogTypeFile;
        [SerializeField] private LogLevel minimalLogLevelFile;

        [Header("Server Logging")]
        [SerializeField] private LogType minimalLogTypeServer;
        [SerializeField] private LogLevel minimalLogLevelServer;

        [Header("Server Config")]
        [SerializeField] private string developmentServerUrl = "//127.0.0.1:5070";

        /// <summary>
        /// {RU} Минимальный тип лога для вывода в Unity Console (видно в редакторе и development сборках)
        /// <para>{EN} Minimum log type for Unity Console output (visible in editor and development builds)</para>
        /// </summary>
        public LogType MinimalLogTypeUnity => minimalLogTypeUnity;
        
        /// <summary>
        /// {RU} Минимальный уровень лога для вывода в Unity Console
        /// <para>{EN} Minimum log level for Unity Console output</para>
        /// </summary>
        public LogLevel MinimalLogLevelUnity => minimalLogLevelUnity;

        /// <summary>
        /// {RU} Минимальный тип лога для записи в файл
        /// <para>{EN} Minimum log type for file writing</para>
        /// </summary>
        public LogType MinimalLogTypeFile => minimalLogTypeFile;
        
        /// <summary>
        /// {RU} Минимальный уровень лога для записи в файл
        /// <para>{EN} Minimum log level for file writing</para>
        /// </summary>
        public LogLevel MinimalLogLevelFile => minimalLogLevelFile;

        /// <summary>
        /// {RU} Минимальный уровень лога для отправки на сервер
        /// <para>{EN} Minimum log level for server transmission</para>
        /// </summary>
        public LogLevel MinimalLogLevelServer => minimalLogLevelServer;
        
        /// <summary>
        /// {RU} Минимальный тип лога для отправки на сервер
        /// <para>{EN} Minimum log type for server transmission</para>
        /// </summary>
        public LogType MinimalLogTypeServer => minimalLogTypeServer;

        /// <summary>
        /// {RU} URL сервера для отправки логов в режиме разработки
        /// <para>{EN} Server URL for sending logs in development mode</para>
        /// </summary>
        public string DevelopmentServerUrl => developmentServerUrl;
    }
}
