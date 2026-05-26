using eDIAN.Data;
using eDIAN.Main.Data;
using eDIAN.Core;
using log4net;
using System.IO;
using System;
using System.Collections.Generic;

namespace eDIAN.Main.Core
{
    public class PluginInitializer
    {
        private readonly ILog logger = PluginLogger.getLogger("PlugInInitializer", "plugIn.log");

        public PluginInitializer()
        {

        }

        /// <summary>
        /// 플러그인 기동시 디렉토리 초기화
        /// </summary>
        /// <param name="rootDirectory"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public Boolean InitializeDirectory()
        {
            Boolean result = false;

            try
            {
                // 플러그인 로그 경로 설정 (...\eDIAN Plus for AutoCAD 2026\logs)

                String logPath = Path.Combine(CommonConstants.PLUGIN_LOG_PATH);
                
                if (!Directory.Exists(logPath))
                {
                    Directory.CreateDirectory(logPath);

                    logger.Info($"* Create Plugin Log Directory.");
                }
                else 
                {
                    logger.Info($"* Exists Plugin Log Directory : {Path.GetDirectoryName(logPath)}");
                }

                // 플러그인 MIP 로그 경로 설정
                String mipLogPath = Path.Combine(CommonConstants.PLUGIN_MIP_LOG_PATH);

                if (!Directory.Exists(mipLogPath))
                {
                    Directory.CreateDirectory(mipLogPath);

                    logger.Info($"* Create Plugin Mip Log Directory.");
                }
                else
                {
                    logger.Info($"* Exists Plugin MIP Log Directory : {Path.GetDirectoryName(mipLogPath)}");
                }

                // MIP 임시 디렉토리 설정
                String mipTempPath = Path.Combine(CommonConstants.PLUGIN_MIP_TEMP_PATH);
                
                if (!Directory.Exists(mipTempPath))
                {
                    Directory.CreateDirectory(mipTempPath);
                
                    logger.Info($"* Create Plugin MIP Temp Directory.");
                }
                else
                {
                    logger.Info($"* Exists Plugin MIP Temp Directory : {Path.GetDirectoryName(mipTempPath)}");
                }

                // MIP 임시 디렉토리 모든 사용자 쓰기 권한 허용
                FileManager.setEveryoneMipTempPermission(true);

                logger.Debug("MIP Temp Directory Access Allow.");

                // MIP 임시 디렉토리 정보   
                DirectoryInfo mipTempDirectoryInfo = new DirectoryInfo(mipTempPath);


                if(CommonConstants.IS_FILE_ACL)
                {
                    // 숨김 속성 부여
                    mipTempDirectoryInfo.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
                }

                FileManager.setEveryoneMipTempPermission(false);

                result = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error initializing directories: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 대체 어셈블리를 로드해서 어플리케이션에서 강제로 인식하게 한다.
        /// 플러그인 어플리케이션 구동시 호출하는 어플리케이션에서 인식하는 
        /// 패키지 라이브러리의 하위 호환문제가 발생시 플러그인 어플리케이션에서 사용자하는 
        /// 상위 버전의 패키지 라이브러리를 강제로 로드해서 문제를 해결한다.
        /// </summary>
        public static void preloadAssembly()
        {
            AssemblyAlternativeLoader assemblyManager = new AssemblyAlternativeLoader();

            // 대체 로딩할 어셈블리 정보 목록 (필요 없을 경우 비워둠)
            List<AssemblyData> assembliesToPreload = new List<AssemblyData>();

            assembliesToPreload.Add(new AssemblyData("Microsoft.IdentityModel.Abstractions", "31bf3856ad364e35", new Version(8, 16, 0, 0)));

            // 실제로 대체 로딩할 어셈블리가 존재하면 로드

            assemblyManager.preloadAssembly(assembliesToPreload);
        }
    }
}