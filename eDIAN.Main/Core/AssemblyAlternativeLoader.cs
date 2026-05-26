using eDIAN.Core;
using eDIAN.Main.Data;
using log4net;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
namespace eDIAN.Main.Core
{
    public class AssemblyAlternativeLoader
    {
        private readonly ILog logger = PluginLogger.getLogger("AssemblyAlternativeLoader", "application.log");

        /// <summary>
        /// 지정된 여러 어셈블리를 먼저 로딩하고, 로딩된 어셈블리를 어플리케이션에서 
        /// 요구하는 어셈블리보다 동일 디렉터리내에 지정한 어셈블리를 우선 인식하게 함.
        /// 
        /// AutoCAD 호스트에서 플러그인 DLL을 로드할 때 System.Management 버전/로드 컨텍스트 충돌이 자주 발생.
        /// 이는 AutoCAD가 자체적으로 System.Management 어셈블리를 로드하고, 플러그인 DLL이 다른 버전을 참조할 때 발생.
        /// UtilityManager.GetMacAddress()(또는 GetLocalIPAddress())가 내부에서 WMI(System.Management)를 사용하는데
        /// NET의 System.Net.* API를 이용해 구현하면 System.Management의 의존을 제거할 수 있음. 
        /// 
        /// 1. 애플리케이션의 기본 디렉터리에 어셈블리 파일(dll)이 있는 경우 로드. 
        /// 2. 어셈블리 확인 핸들러를 등록하여 어플리케이션에서 어셈블리에 대한 향후 요청이 동일한 위치에서 대체 확인되도록 설정. 
        /// - 메인 어플리케이션(여기서는 eDIAN.dll)과 동일한 폴더에 존재하는 대체 어셈블리를 조회
        /// - 공개 토큰 값(publicKeyToken)이 일치하는 지 확인
        /// - 필요 시 최소 버전 이상인지 확인(옵션. 버전이 null 일 경우 확인 안함)
        /// </summary>
        /// <remarks>
        /// 이는 플러그인 어플리케이션이나 동적 로딩 환경을 지원하는 어플리케이션의 런타임 시 
        /// 어셈블리를 조건에 따라 선택 인식하게 하거나  
        /// 메인 어플리케이션에서 로드되지 않는 어셈블리를 대체 어셈블리로 인식하게 함.
        /// 
        /// eDIAN에서는 디버그에서는 정상적으로 어셈블리를 인식하지만 배포된 환경에서 
        /// 메인 어플리케이션의 로딩 조건에 의해 해당 어셈블리를 제대로 인식하지 못하는 케이스에 해당.
        /// </remarks>
        public void preloadAssembly(List<AssemblyData> alertnativeAssemblySpecList)
        {
            if (alertnativeAssemblySpecList == null || alertnativeAssemblySpecList.Count == 0)
            {
                return;
            }

            // 메인 어플리케이션이 실행되는 기본 디렉터리

            String baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (String.IsNullOrEmpty(baseDir))
            {
                return;
            }

            // 해당 어셈블리 파일이 존재하면 기본 컨텍스트에 미리 로딩
            foreach (AssemblyData alertnativeAssemblySpec in alertnativeAssemblySpecList)
            {
                String alertnativeAssemblyPath = Path.Combine(baseDir, alertnativeAssemblySpec.Name + ".dll");

                if (File.Exists(alertnativeAssemblyPath))
                {
                    try
                    {
                        // 바인딩 우선권 확보를 위해 기본 컨텍스트에 대체 어샘블리를 로딩
                        Assembly.LoadFile(alertnativeAssemblyPath);

                        logger.Debug($"* Success pre-loading assembly : {alertnativeAssemblyPath}");
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"* Failure pre-loading assembly : {alertnativeAssemblyPath}");
                        logger.Error(ex);
                    }
                }
            }

            // AssemblyResolve 의 역할은 어플리케이션에서 요구하는 어셈블리를 인식하지 못하는 경우에 대한 처리
            // 여기에서는 동일 폴더 내의 동일한 이름의 대체 어셈블리를 로딩해서 우선적으로 인식하게 함.

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                // 어플리케이션에서 요구하는 어셈블리 정보
                AssemblyName requestAssemblySpec = new AssemblyName(args.Name);

                // 대체 로딩할 어셈블리 정보 목록에서 어플리케이션에서 요구하는 어셈블리명과 동일한 어셈블리 정보 조회 
                AssemblyData alternativeAssemblySpec = alertnativeAssemblySpecList.FirstOrDefault(s => s.Name.Equals(requestAssemblySpec.Name, StringComparison.OrdinalIgnoreCase));

                if (alternativeAssemblySpec == null) 
                {
                    // 조회된 대체 어셈블리 정보가 없는 경우 

                    logger.Debug($"* AssemblyResolve : No matching assembly data for requested assembly '{requestAssemblySpec.Name}'");

                    return null; 
                }

                if (!String.IsNullOrEmpty(alternativeAssemblySpec.PublicKeyToken))
                {
                    // 요청하는 어셈블리의 토큰 값과 대체 로딩 할 어셈블리의 토큰 값이 일치하는지 확인(어셈블리 토큰 충돌 방지)

                    String requestAssemblyToken = BitConverter.ToString(requestAssemblySpec.GetPublicKeyToken() ?? new byte[0]).Replace("-", "").ToLowerInvariant();

                    if (!requestAssemblyToken.Equals(alternativeAssemblySpec.PublicKeyToken, StringComparison.OrdinalIgnoreCase)) 
                    {
                        // 요청한 어셈블리와 강제 로딩 할 어셈블리의 토큰 값이 불일치
                        logger.Debug($"* AssemblyResolve : Public key token mismatch for assembly '{requestAssemblySpec.Name}'. Expected: '{alternativeAssemblySpec.PublicKeyToken}', Actual: '{requestAssemblyToken}'");

                        return null;
                    }
                }

                // 어플리케이션이 요구하는 어셈블리 파일 경로
                String alternativeAssemblyPath = Path.Combine(baseDir, alternativeAssemblySpec.Name + ".dll");

                if (!File.Exists(alternativeAssemblyPath))
                {
                    // 어플리케이션이 요구하는 어셈블리 파일 경로에 해당하는 어셈블리 파일이 존재하지 않음
                    logger.Debug($"* AssemblyResolve : Assembly file not found for '{requestAssemblySpec.Name}' at '{alternativeAssemblyPath}'");

                    return null;
                }

                // 대체 로딩할 어셈블리 정보에 최소 버전 값이 있을 경우에만 버전을 확인
                if (alternativeAssemblySpec.MinVersion != null)
                {
                    try
                    {
                        // 대체 로딩할 어셈블리 정보에 해당하는 어셈블리 파일에서 어셈블리 정보 생성
                        AssemblyName alyternativeAssemblySpecByFile = AssemblyName.GetAssemblyName(alternativeAssemblyPath);

                        if (alyternativeAssemblySpecByFile != null && alyternativeAssemblySpecByFile.Version != null && alyternativeAssemblySpecByFile.Version < alternativeAssemblySpec.MinVersion)
                        {
                            // 대체 요청 어셈블리의 최소 버전보다 어셈블리 파일의 버전이 낮으면 로딩하지 않음
                            logger.Debug($"* AssemblyResolve : Assembly version '{alyternativeAssemblySpecByFile.Version}' is less than required minimum version '{alternativeAssemblySpec.MinVersion}' for assembly '{requestAssemblySpec.Name}'");

                            return null;
                        }
                    }
                    catch
                    {
                        // 버전 확인 실패(다른 대체 어셈블리 로딩 시도는 계속)
                        logger.Error($"* AssemblyResolve : Failed to get assembly version for '{requestAssemblySpec.Name}' at '{alternativeAssemblyPath}'");
                    }
                }

                try
                {
                    // 어플리케이션에서 요구하는 어셈블리를 대체할 강제 로딩 어셈블리 로딩

                    return Assembly.LoadFrom(alternativeAssemblyPath);
                }
                catch
                {
                    // 어셈블리 로딩 실패(다른 대체 어셈블리 로딩 시도는 계속)
                    logger.Error($"* AssemblyResolve : Failed to load assembly from '{alternativeAssemblyPath}' for requested assembly '{requestAssemblySpec.Name}'");
                    return null;
                }
            };
        }
    }
}
