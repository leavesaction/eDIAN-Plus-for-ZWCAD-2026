using eDIAN.Data;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.IO;
using System.Text;

namespace eDIAN.Core
{
    /// <summary>
	/// 어플리케이션 로그 출력을 위한 로거 클래스
	/// </summary>
	/// <seealso cref="eDIAN.Log4net.ILogger" />
	public class PluginLogger
    {
        private const String LOG_PATTERN = "[%date] [%5level] [%-15logger] [%3line] %message%newline";

        public PluginLogger()
        {
            if (!Directory.Exists(CommonConstants.PLUGIN_LOG_PATH))
            {
                Directory.CreateDirectory(CommonConstants.PLUGIN_LOG_PATH);
            }
        }

        private static PatternLayout CreatePatternLayout()
        {
            PatternLayout layout = new PatternLayout{ ConversionPattern = LOG_PATTERN };

            layout.ActivateOptions();

            return layout;
        }

        /// <summary>
        /// loggerName과 fileName에 따라 별도의 파일로 로깅되는 logger를 생성합니다.
        /// </summary>
        public static ILog getLogger(String loggerName, String fileName)
        {
            if (!Directory.Exists(CommonConstants.PLUGIN_LOG_PATH))
            {
                Directory.CreateDirectory(CommonConstants.PLUGIN_LOG_PATH);
            }

            // Get the hierarchy repository
            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            // 이미 존재하는 logger면 반환
            ILogger existingLogger = hierarchy.Exists(loggerName);

            if (existingLogger != null)
            {
                return LogManager.GetLogger(loggerName);
            }

            hierarchy.Configured = true;
            
            String logFileName = Path.Combine(CommonConstants.PLUGIN_LOG_PATH, fileName);
            
            // Configure RollingFileAppender
            RollingFileAppender rollingFileAppender = new RollingFileAppender
            {
                Layout = CreatePatternLayout(),
                ImmediateFlush = true,
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Date,
                MaxSizeRollBackups = 7,
                DatePattern = "_yyyyMMdd'.log'",
                StaticLogFileName = true,
                Encoding = Encoding.UTF8,
                File = logFileName,
                Threshold = Level.All,
                LockingModel = new RollingFileAppender.MinimalLock(),
                Name = loggerName
            };

            rollingFileAppender.ActivateOptions();
            
            log4net.Repository.ILoggerRepository repository = LogManager.CreateRepository(loggerName);

            BasicConfigurator.Configure(repository, rollingFileAppender);

            ILog logger = LogManager.GetLogger(loggerName, loggerName);

            logger.Debug($"{loggerName} Logger Lib initialized");

            return logger;
        }
/*
        /// <summary>
        /// [CAD] CAD 콘솔에 메세지 출력 (DEBUG 용)
        /// </summary>
        /// <param name="message"></param>
        public static void MessageLogging(String? message)
        {
#if DEBUG
            if (CadApplication.DocumentManager.MdiActiveDocument != null)
            {
                Editor ed = CadApplication.DocumentManager.MdiActiveDocument.Editor;
                ed?.WriteMessage($"{message}{Environment.NewLine}");
            }
#endif

//            alogger.Debug(message);
        }
*/    
    }
}
