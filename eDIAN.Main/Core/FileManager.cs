using eDIAN.Data;
using eDIAN.Core;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using log4net;
using System;

namespace eDIAN.Main.Core
{
    public class FileManager
    {
        private static readonly ILog logger = PluginLogger.getLogger("FileManager", "application.log");

        private static String prefixExtStr = "p";                                                         // 레이블 적용 파일의 확장자 접두문자  

        /// <summary>
        /// 임시 MIP 임시 경로내 파일 삭제
        /// </summary>
        /// <param name="mipDataPath"></param>
        public static void deleteMipTempFiles()
        {
            String mipTempPath = CommonConstants.PLUGIN_MIP_TEMP_PATH;

            // Check if the directory exists

            if (Directory.Exists(mipTempPath))
            {
                FileManager.deleteFilesWithPattern(mipTempPath, "*.*");

                logger.Debug($" - deleteMipTempFiles : ...\\*.*");
            }
            else
            {
                logger.Debug($" - deleteMipTempFiles : Mip Temp Directory not exists : {mipTempPath}");
            }
        }

        /// <summary>
        /// 파일명(확장자 제외)과 동일한 모든 파일 삭제
        /// </summary>
        /// <param name="filePath"></param>
        /// <exception cref="Exception"></exception>
        public static void deleteFilesByName(String filePath)
        {
            String dirName = Path.GetDirectoryName(filePath) ?? "";                         // 디렉토리 경로
            String fileName = Path.GetFileName(filePath);                                   // 파일명과 확장자를 포함
            String fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);   // 확장자를 제외한 파일명
            String extension = Path.GetExtension(filePath);                                 // 확장자만

            try
            {
                FileManager.deleteFilesWithPattern(dirName, $"{fileNameWithoutExtension}.*");

                logger.Debug($" - deleteFilesByName : {fileNameWithoutExtension}.*");
            }
            catch (Exception ex)
            {
                logger.Error($" - deleteFilesByName : {Path.GetFileName(filePath)} {ex.Message}");

                throw;
            }
        }

        /// <summary>
        /// 패턴 케이스에 해당하는 파일 삭제 
        /// </summary>
        /// <param name="directoryPath">디렉토리 경로</param>
        /// <param name="searchPattern">파일 패턴</param>
        private static void deleteFilesWithPattern(String directoryPath, String searchPattern)
        {
            if (Directory.Exists(directoryPath))
            {
                FileManager.setUserMipTempWritePermissions(true);

                String[] files = Directory.GetFiles(directoryPath, searchPattern);

                foreach (String file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error deleting file {file}: {ex.Message}");
                    }
                }

                FileManager.setUserMipTempWritePermissions(false);
            }
        }

        /// <summary>
        /// 임시 파일 여부 확인
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static bool isTemporaryFile(String filePath)
        {
            bool result = false;

            if (String.IsNullOrEmpty(filePath))
            {
                return result;
            }

            String fileDirectory = Path.GetDirectoryName(filePath);

            if (fileDirectory == null)
            {
                return result;
            }

            // 1. 플러그인 전용 임시 디렉터리 체크 (대소문자 무시)
            if (fileDirectory.Equals(CommonConstants.PLUGIN_MIP_TEMP_PATH, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. 윈도우 시스템 Temp 디렉터리 경로 획득 및 부분 경로 매칭 검증
            String tempPath = Path.GetTempPath();
            
            bool checkTempPath = fileDirectory.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) ||
                             fileDirectory.IndexOf("\\AppData\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             fileDirectory.IndexOf("\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0;

            if (checkTempPath)
            {
                return true;
            }

            return result;
        }

        /// <summary>
        /// 편집을 위해 복호화 된 파일 경로 생성 (확장자 앞에 prefixExtension 추가`)
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="prefixExtension"></param>
        /// <returns></returns>
        public static String getOutputFilePath(String filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return "";
            }

            // 파일 경로 분석

            String fileName = Path.GetFileNameWithoutExtension(filePath);
            String fileExtension = Path.GetExtension(filePath); // 확장자만
            String fileDirectory = Path.GetDirectoryName(filePath); // 디렉토리 경로

            return String.Format("{0}{1}{2}.{3}{4}", fileDirectory, Path.DirectorySeparatorChar, fileName, prefixExtStr, fileExtension.Substring(1));
        }

        /// <summary>
        /// everyone 사용자의 모든 권한을 부여/해제
        /// </summary>
        /// <param name="isDeny"></param>
        public static void setEveryoneMipTempPermission(bool isAllow)
        {
            if (!CommonConstants.IS_FILE_ACL) return;

            SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            // !!!!! Everyone 계정은 Allow 세팅이 안됨 따라서 Deny 로 처리 !!!!!
            FileManager.controlPermission(everyone, CommonConstants.PLUGIN_MIP_TEMP_PATH, FileSystemRights.FullControl, AccessControlType.Deny, !isAllow);
        }

        /// <summary>
        /// 임시 경로에 로그인 사용자는 쓰기 권한을 대해 대한 everyone 사용자는 전체 권한을  부여/해제
        /// </summary>
        /// <param name="isAllow">true : 허용, false : 해제</param>
        public static void setUserMipTempWritePermissions(bool isAllow)
        {
            if (!CommonConstants.IS_FILE_ACL) return;

            logger.Debug(" - setUserMipTempWritePermissions : Write Permission Allow : " + isAllow);

            // isAllow = true 인 경우 해당 디렉토리의 모든 권한 허용

            // 모든 사용자에 대해 해당 경로 접근 권한 허용
            FileManager.setEveryoneMipTempPermission(isAllow);     // !isPermission -> isPermission 으로 수정

            // 현재 로그인한 사용자 명에 해당하는 윈도우 계정 정보 생성
            NTAccount ntAccount = new NTAccount(WindowsIdentity.GetCurrent().Name);

            // 윈도우 계정의 보안 계정으로 변환
            SecurityIdentifier userSid = (SecurityIdentifier)ntAccount.Translate(typeof(SecurityIdentifier));

            // 해당 경로의 쓰기 권한 허용
            FileManager.controlPermission(userSid, CommonConstants.PLUGIN_MIP_TEMP_PATH, FileSystemRights.Write, AccessControlType.Allow, isAllow);
        }

        /// <summary>
        /// 디렉토리 접근 권한 설정
        /// </summary>
        /// <param name="sid">아이디</param>
        /// <param name="dirPath">대상 경로</param>
        /// <param name="rights">파일 시스템 권한</param>
        /// <param name="type">접근 구분</param>
        /// <param name="isLock">허용 / 해제</param>
        private static void controlPermission(SecurityIdentifier sid, String dirPath, FileSystemRights rights, AccessControlType type, bool isLock)
        {
            try
            {
                // 디렉토리 정보 가져오기 
                DirectoryInfo directoryInfo = new DirectoryInfo(dirPath);

                // 디렉토리 보안 속성 가져오기
                DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();

                // 파일 시스템 접근 규칙 생성
                FileSystemAccessRule fileSystemAccessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.None, PropagationFlags.None, type);

                if (isLock)
                {
                    // 디렉토리 보안 속성에 접근 규칙 추가
                    directorySecurity.AddAccessRule(fileSystemAccessRule);
                }
                else
                {
                    // 디렉토리 보안 속성에서 접근 규칙 제거
                    directorySecurity.RemoveAccessRule(fileSystemAccessRule);
                }

                // 변경된 보안 속성을 디렉토리에 적용
                directoryInfo.SetAccessControl(directorySecurity);
            }
            catch (Exception ex)
            {
                logger.Error($"controlPermission : {System.Environment.NewLine}{ex}");
                return;
            }
        }
    }
}
