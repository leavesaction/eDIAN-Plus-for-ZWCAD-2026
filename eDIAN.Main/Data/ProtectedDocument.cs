using ZwSoft.ZwCAD.ApplicationServices;
using System.IO;
using System;
using System.Collections.Generic;

namespace eDIAN.Main.Data
{
    /// <summary>
    /// MIP 보안 처리를 위한 문서의 속성 정보
    /// </summary>
    public class ProtectedDocument : IDisposable
    {
        // 보호된 파일의 아이디
        public String contentId { get; set; } = String.Empty;       // 콘텐츠 아이디
        // 원본 파일명
        public String onlyFileName { get; set; } = String.Empty;
        // 원본 파일 경로
        public String filePath { get; set; } = String.Empty;        // 경로를 포함한 파일 이름
        // 임시파일명
        public String decryptedTemporaryFileName { get; set; } = String.Empty;

        // 복호화 된 임시 파일 경로 (추가)
        private String _decryptedTemporaryFilePath = String.Empty;

        // 복호화 된 임시파일 경로 Setter, Getter
        public String decryptedTemporaryFilePath
        {
            get
            {
                if (String.IsNullOrEmpty(_decryptedTemporaryFilePath))
                {
                    _decryptedTemporaryFilePath = String.Empty;
                }

                return _decryptedTemporaryFilePath;
            }
            set
            {
                _decryptedTemporaryFilePath = value;
                this.decryptedTemporaryFileName = Path.GetFileName(value); // Store only the file name for later use.
            }
        }

        /**********************************************
         * 파일 상태 속성
         **********************************************/
        // 시스템(CAD) 핸들러 
        public IntPtr handle { get; set; } = IntPtr.Zero;
        // 파일 해시코드
        public int hashCode { get; set; } = 0;
        // 읽기 전용 여부
        public bool isReadOnly { get; set; } = false;
        // 저장 반영 여부
        public bool isUpdated { get; set; } = false;
        // 레이블링 필요 여부
        public bool needLabeling { get; set; } = false;
        // 파일 존재 여부
        public bool isNewFile { get; set; } = false;
        // 권한 제한 메세지
        public String message { get; set; } = String.Empty;

        /**********************************************
         * 권한 속성
         **********************************************/
        // 파일 보호 여부
        public bool isProtected { get; set; } = false;
        // 소유자 여부
        public bool isOwner { get; set; } = false;
        // 수정 권한 여부
        public bool isEdit { get; set; } = false;
        // 조회 권한 여부
        public bool isView { get; set; } = false;
        // 출력 권한 여부
        public bool isPrint { get; set; } = false;
        // 내보내기 권한 여부
        public bool isExport { get; set; } = false;
        // 추출(복사) 권한 여부
        public bool isExtract { get; set; } = false;

        /**********************************************
         * Owner 속성
         **********************************************/
        // Owner userPrincipal (권한 속성 부여자)
        public String protectionOwner { get; set; } = String.Empty;
        // 부여 대상 userPrincipal 목록
        public List<String> appliedUserList { get; set; }  = new List<String>();
        // 부여된 권한 목록
        public List<String> appliedRightList { get; set; } = new List<String>();

        /**********************************************
         * 레이블 속성
         **********************************************/
        // 레이블 아이디
        public String labelId { get; set; } = String.Empty;
        // 레이블 명
        public String labelName { get; set; } = String.Empty;
        // 만료 일자
        public String expireDateTime { get; set; } = String.Empty;

        // 생성자 
        public ProtectedDocument(String fileName)
        {
            if (String.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            this.initialize(fileName, IntPtr.Zero);
        }

        // 생성자
        public ProtectedDocument(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            this.initialize(document, IntPtr.Zero);
        }

        private void initialize(String filePath, IntPtr handle)
        {
            this.filePath = filePath;

            this.onlyFileName = Path.GetFileName(this.filePath);

            this.isNewFile = !File.Exists(this.filePath);

            this.handle = handle;

            // 권한 상태값 초기화 

            this.InitProtectionInfo();
        }

        private void initialize(Document document, IntPtr handle)
        {
            this.filePath = document.Name;

            this.hashCode = document.GetHashCode();

            this.isReadOnly = document.IsReadOnly;

            this.onlyFileName = Path.GetFileName(this.filePath);

            this.isNewFile = !File.Exists(this.filePath);

            this.handle = handle;

            // 권한 상태값 초기화 

            this.InitProtectionInfo();
        }

        // 권한 상태값 초기화 
        private void InitProtectionInfo()
        {
            // This method can be used to initialize protection-related properties if needed.
            // Currently, it does nothing but can be expanded in the future.
            if (this.isNewFile)
            {
                this.isProtected = false;
                this.isOwner = true;
                this.isEdit = true;
                this.isView = true;
                this.isPrint = true;
                this.isExport = true;
                this.isExtract = true;
            }
        }
        // 파일 보호 정보 문자열 생성

        override public String ToString()
        {
            String toString = $"{Environment.NewLine}";

            toString += $"-------------------------------------------------------------\n";
            toString += $" = Document Protection Info \n";
            toString += $"-------------------------------------------------------------\n";

            toString += $"  contentId : {contentId}\n";
            toString += $"  hashCode  : {hashCode}\n";
            toString += $"  handle  : {handle}\n";
            toString += $"  filePath  : '...\\{Path.GetFileName(filePath)}'\n";
            toString += $"  temporaryFilePath : '...\\{Path.GetFileName(decryptedTemporaryFilePath)}'\n";
            toString += $"  isNewFile : {isNewFile} ({(isNewFile ? "새 문서" : "저장된 일반 문서")})\n";
            toString += $"  isReadOny : {isReadOnly} ({(isReadOnly ? "읽기" : "읽기,쓰기")})\n";
            toString += $"  isUpdated : {isUpdated} ({(isUpdated ? "변경됨" : "변경안됨")})\n";
            toString += $"  needLabeling : {needLabeling} ({(needLabeling ? "레이블링 필요" : "레이블링 불필요")})\n";
            toString += $"  isProtected : {isProtected} ({(isProtected ? "암호문서" : "평문문서")})\n";
            toString += $"  message : {message}\n";

            toString += $"  [added protection information] \n";

            if (isProtected)
            {
                toString += $"    * label : {labelId} ({labelName})\n";
                toString += $"    * expireDateTime : {expireDateTime}\n";
                toString += $"    * isOwner : {isOwner}\n";
                toString += $"    * isView : {isView}\n";
                toString += $"    * isEdit : {isEdit}\n";
                toString += $"    * isPrint : {isPrint}\n";
                toString += $"    * isExport : {isExport}\n";
                toString += $"    * isExtract : {isExtract}\n";
            }
            else 
            {
                toString += $"    * Document is Not protected \n";
            }

            toString += $"  [added Owner information] \n";
            toString += $"    * protectionOwner : {protectionOwner}\n";

            if (appliedRightList != null)
            {
                toString += $"    * appliedRight : ";

                foreach (String right in appliedRightList)
                {
                    toString += $"{right}, ";
                }

                toString = toString.Substring(0, toString.Length - 2) + "\n";
            }

            if (appliedUserList != null )
            { 
                toString += $"    * appliedUser : \n";

                foreach (String user in appliedUserList) 
                {
                    toString += $"          {user}\n";
                }
            }

            toString += $"-------------------------------------------------------------";

            return toString;
        }

        // 파일 보호 정보 해제 시 임시파일 삭제
        public void Dispose()
        {
            if ( File.Exists(_decryptedTemporaryFilePath) ) 
            {
                try
                {
                    File.Delete(_decryptedTemporaryFilePath);
                }
                catch ( Exception ex )
                {
                    Console.WriteLine(ex);
                }
            }
        }
    }
}
