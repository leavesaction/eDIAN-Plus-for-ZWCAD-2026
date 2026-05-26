using ZwSoft.ZwCAD.ApplicationServices;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.Data;
using log4net;
using System.Collections.ObjectModel;
using System.IO;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadCoreApplication = ZwSoft.ZwCAD.ApplicationServices.Core.Application;
using System;
using System.Linq;

namespace eDIAN.Main.Core
{
    public class DocumentHandler
    {
        private static readonly ILog logger = PluginLogger.getLogger("DocumentHandler", "application.log");

        public event EventHandler<ProtectedDocumentEventArgs> OnActivedDocument;

        // 열린 문서 정보 Collection 객체
        public static ObservableCollection<DocumentListItem> itemList;

        // 마지막으로 선택한 파일의 목록에서의 위치 인덱트
        private int lastSelecedIndex = 0;

        static DocumentHandler()
        {
            itemList = new ObservableCollection<DocumentListItem>();
        }

        public DocumentHandler()
        {

        }

        /// <summary>
        /// 새로운 문서가 열릴때 열린 파일 목록 추가
        /// </summary>
        /// <param name="protectedDocument"></param>
        /// <param name="id"></param>
        public void addOpenDocument(ProtectedDocument protectedDocument, String id = null)
        {
            if (protectedDocument == null)
            {
                return;
            }

            // 열린 파일 목록에 존재하지 않는 경우 DocumentListItem 생성해 목록에 추가

            if (this.existOpenDocument(protectedDocument) == false)
            {
                itemList.Add(new DocumentListItem(Path.GetFileName(protectedDocument.filePath), protectedDocument));

                logger.Debug(DocumentHandler.toString());
            }

            // itemId가 전달된 경우 해당 itemId의 인덱스, 전달되지 않은 경우 열린 문서 목록의 마지막 인덱스 또는 첫번째 인덱스(오름차순 정렬인 경우) 선택

            // 문서가 열릴 때 리스트에 포함될 위치

            int selectedIndex = itemList.Count - 1;

            if (CommonConstants.IS_ASC == true)
            {
                selectedIndex = 0;
            }

            // 문서 활성화 이벤트 (activeDocument()) 호출 
            OnActivedDocument?.Invoke(this, new ProtectedDocumentEventArgs(selectedIndex));  
        }

        /// <summary>
        /// 열린 문서가 닫힐때 열린 파일 목록에서 제거
        /// </summary>
        /// <param name="protectedDocument"></param>
        public void removeOpenDocument(ProtectedDocument protectedDocument)
        {
            if (protectedDocument == null)
            {
                return;
            }

            if (itemList.Count > 0)
            {
                DocumentHandler.itemList.RemoveAt(this.getOpenDocumentIndex(protectedDocument));
            }
        }

        /// <summary>
        /// 활성 AutoCAD 문서에 대해 DBMOD를 읽어 변경 여부(*)를 ListBox 파일명에 반영
        /// </summary>
        public void replaceOpenDocument(ProtectedDocument protectedDocument)
        {
            if (protectedDocument == null)
            {
                return;
            }

            bool isUpdated = Convert.ToInt16(CadCoreApplication.GetSystemVariable("DBMOD")) != 0 ? true : false;

            if (protectedDocument.isUpdated != isUpdated)
            {
                // 활성 문서의 DBMOD 값을 읽어 이전 isUpdated 값과 다르면 ProtectedDocument.isUpdated 속성에 반영

                protectedDocument.isUpdated = isUpdated;

                logger.Debug($" - changeUpdatedStatus : {isUpdated}");
            }

            int selectedIndex = this.getOpenDocumentIndex(protectedDocument);

            if (selectedIndex < 0 || selectedIndex >= DocumentHandler.itemList.Count)
            {
                return;
            }

            // Item에 속한 ProtectedDocument 파일명이 동일할 경우 전달 받은 ProtectedDocument 로 교체

            DocumentHandler.itemList[selectedIndex].source.Dispose();

            DocumentHandler.itemList[selectedIndex] = new DocumentListItem(DocumentHandler.itemList[selectedIndex].id, Path.GetFileName(protectedDocument.filePath), protectedDocument);

            // logger.Debug(DocumentHandler.toString());

            // 문서 활성화 이벤트 (activeDocument()) 호출 
            OnActivedDocument?.Invoke(this, new ProtectedDocumentEventArgs(selectedIndex));
        }

        /// <summary>
        /// 열려 있는 모든 보호 파일 또는 전체 파일 목록 제거
        /// </summary>
        /// <param name="protectedDocument"></param>
        public void clearOpenDocument(bool onlyProtectedFile)
        {
            for (int i = 0; i < DocumentHandler.itemList.Count(); i++)
            {
                DocumentListItem item = itemList[i];

                if ((onlyProtectedFile && item.source.isProtected) || !onlyProtectedFile)
                {
                    item.source.Dispose();

                    itemList.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 활성화 문서의 탭 반전 처리
        /// </summary>
        public void activeOpenDocument(ProtectedDocument protectedDocument)
        {
            int selectedIndex = this.getOpenDocumentIndex(protectedDocument);

            // 문서 활성화 이벤트 (activeDocument()) 호출 
            OnActivedDocument?.Invoke(this, new ProtectedDocumentEventArgs(selectedIndex));
        }

        public static String toString() 
        {
            String result = $"[Document List]\n";

            if (itemList == null)
            {
                result += $"   - itemList is empty.";
            }

            foreach (DocumentListItem item in itemList!)
            {
                result += $"  {item.id}. {item.displayName}\n";
            }

            return result;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// 전달 받은 ProtectedDocument 와 동일한 파일명을 가진 DocumentListItem 존재 여부 반환
        /// </summary>
        /// <param name="protectedDocument"></param>
        /// <returns></returns>        
        public bool existOpenDocument(ProtectedDocument protectedDocument)
        {
            bool result = false;

            if (protectedDocument == null)
            {
                return result;
            }

            // logger.Debug($" ### protectedDocument : {protectedDocument.ToString()}");

            for (int i = 0; i < itemList.Count; i++)
            {
                DocumentListItem item = itemList[i];

                if (this.equals(item.source, protectedDocument))
                {
                    result = true;

                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 파일 경로에 해당하는 보호된 문서 정보 존재 유무 확인
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool existOpenDocument(String filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return false;
            }

            for (int i = 0; i < itemList.Count; i++)
            {
                if (this.equals(itemList[i].source, filePath))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 문서에 해당하는 보호된 문서 정보 존재 유무 확인
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool existOpenDocument(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            for (int i = 0; i < itemList.Count; i++)
            {
                if (this.equals(itemList[i].source, document))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 열린 문석 목록에서 파일 명에 해당하는 ProtectedDocument 반환
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public ProtectedDocument getOpenDocument(Document document)
        {
            if (document == null)
            {
                return null;
            }

            foreach (DocumentListItem item in itemList)
            {
                if (item == null)
                {
                    continue;
                }

                if (this.equals(item.source, document))
                {
                    // 파일명 또는 복호화된 임시 파일명이 일치할 경우 해당 ProtectdDocument 반환
                    return item.source;
                }
            }

            return null;
        }

        /// <summary>
        /// 파일 경로에 해당하는 보호된 문서 정보 조회
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ProtectedDocument getOpenDocument(String filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return null;
            }

            foreach (DocumentListItem item in itemList)
            {
                if (item == null)
                {
                    continue;
                }

                if (this.equals(item.source, filePath))
                {
                    return item.source;
                }
            }

            return null;
        }

        /// <summary>
        /// 파일 핸들러에 해당하는 보호된 문서 정보 조회
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ProtectedDocument getOpenDocument(int hashCode)
        {

            for (int i = 0; i < itemList.Count; i++)
            {
                if (this.equals(itemList[i].source, hashCode))
                {
                    return itemList[i].source;
                }
            }

            return null;
        }


        /// <summary>
        /// 파일 핸들러에 해당하는 보호된 문서 정보 조회
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ProtectedDocument getOpenDocument(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            for (int i = 0; i < itemList.Count; i++)
            {
                if (this.equals(itemList[i].source, handle))
                {
                    return itemList[i].source;
                }
            }

            return null;
        }

        /// <summary>
        /// 열린 문서 목록에서 ProtectedDocument 에 해당하는 리스트 인덱스 반환
        /// </summary>
        /// <param name="protectedDocument"></param>
        /// <returns></returns>
        public int getOpenDocumentIndex(ProtectedDocument protectedDocument)
        {
            if (protectedDocument == null)
            {
                return this.lastSelecedIndex;
            }

            for (int i = 0; i < itemList.Count; i++)
            {
                if (this.equals(itemList[i].source, protectedDocument))
                {
                    this.lastSelecedIndex = i;

                    break;
                }
            }

            return this.lastSelecedIndex;
        }

        /// <summary>
        /// 열려진 CAD 도면에서 protectedDocument에 해당하는 Document 객체 조회
        /// </summary>
        /// <param name="protectedDocument"></param>
        /// <returns></returns>
        public Document searchDocument(ProtectedDocument protectedDocument)
        {
            Document result = null;

            foreach (Document document in CadApplication.DocumentManager)
            {
                if (document == null)
                {
                    continue;
                }

                if (this.equals(protectedDocument, document))
                {
                    result = document;
                    break;
                }
            }

            return result;
        }

//////////////////////////////////////////////////////////////////////////////////////////////////////

        // 해당 보호 문서 정보가 파일 경로에 해댱하는지 확인
        private bool equals(ProtectedDocument sourceDocument, String filePath)
        {
            return sourceDocument.filePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) || sourceDocument.decryptedTemporaryFilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase);
        }

        // 해당 보호 문서 정보가 파일 핸들러에 해당하는지 확인
        private bool equals(ProtectedDocument sourceDocument, int hashCode)
        {
            return sourceDocument.hashCode == hashCode;
        }

        // 해당 보호 문서 정보가 파일 핸들러에 해당하는지 확인
        private bool equals(ProtectedDocument sourceDocument, IntPtr handle)
        {
            return sourceDocument.handle == handle;
        }

        // 해당 보호 문서 정보가 문서에 해당하는지 확인
        private bool equals(ProtectedDocument sourceDocument, Document targetDocument)
        {
            String filePath = targetDocument.Name;
            int hashCode = targetDocument.GetHashCode();
            bool isReadOnly = targetDocument.IsReadOnly;
/*
            logger.Debug($" -------------------------------------------------------------------------------");
            logger.Debug($" >>> equals1: Comparing {targetDocument.Name} with {sourceDocument.filePath} or {sourceDocument.decryptedTemporaryFilePath}");
            logger.Debug($" >>> equals1: Comparing {targetDocument.IsReadOnly} with {sourceDocument.isReadOnly}");
            logger.Debug($" >>> equals1: Comparing {targetDocument.GetHashCode()} with {sourceDocument.hashCode}");
            logger.Debug($" -------------------------------------------------------------------------------");
*/
            return (sourceDocument.filePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) || sourceDocument.decryptedTemporaryFilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                && sourceDocument.hashCode == hashCode
                && sourceDocument.isReadOnly == isReadOnly;
        }

        // 두개의 보호 문서 정보 동일 여부 확인
        private bool equals(ProtectedDocument sourceDocument, ProtectedDocument targetDocument)
        {
            String filePath = targetDocument.filePath;
            int hashCode = targetDocument.hashCode;
            bool isReadOnly = targetDocument.isReadOnly;

/*
            logger.Debug($" -------------------------------------------------------------------------------");
            logger.Debug($" >>> equals2: Comparing {targetDocument.filePath} with {sourceDocument.filePath} or {sourceDocument.decryptedTemporaryFilePath}");
            logger.Debug($" >>> equals2: Comparing {targetDocument.isReadOnly} with {sourceDocument.isReadOnly}");
            logger.Debug($" >>> equals2: Comparing {targetDocument.hashCode} with {sourceDocument.hashCode}");
            logger.Debug($" -------------------------------------------------------------------------------");
*/
            return (sourceDocument.filePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) || sourceDocument.decryptedTemporaryFilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                && sourceDocument.hashCode == hashCode
                && sourceDocument.isReadOnly == isReadOnly;
        }
    }
}