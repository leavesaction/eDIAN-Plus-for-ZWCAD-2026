using eDIAN.Core;
using eDIAN.Main.Data;
using log4net;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using ZwSoft.ZwCAD.ApplicationServices;
using CadCoreApplication = ZwSoft.ZwCAD.ApplicationServices.Core.Application;

namespace eDIAN.Main.Core
{
    /// <summary>
    /// 저장·닫기(closeDocument) 및 CAD documentDestroyed 구간의 단계별 진단 로그.
    /// 로그 파일: logs\close_flow.log (grep: [CLOSE-FLOW])
    ///
    /// === CAD 내부 크래시 vs 플러그인 구분 (사후 분석) ===
    /// 1) 마지막 phase가 BeforeCloseAndDiscard 이고 AfterCloseAndDiscard 없음
    ///    → Document.CloseAndDiscard() 또는 CAD 네이티브 teardown 구간에서 프로세스 종료 가능성 큼.
    /// 2) AfterCloseAndDiscard 있고 DocumentToBeDestroyed 없음
    ///    → Close API는 반환했으나 CAD가 destroy 이벤트까지 못 감 — CAD 내부 비동기 종료.
    /// 3) DocumentToBeDestroyed 있고 DocumentDestroyed 없음
    ///    → CAD destroy 핸들러/네이티브 I/O 구간 (플러그인 documentDestroyed 미진입).
    /// 4) DocumentDestroyed 이후 ApplyProtection/DeleteFiles 중 로그 중단
    ///    → Managed 비동기 정리 또는 MIP I/O (application.log / close_flow.log 대조).
    /// 5) SessionComplete까지 찍혔는데도 ZWCAD 종료
    ///    → 플러그인 경로는 완료; Windows 이벤트 뷰어 ZWCAD.exe Fault / WER 덤프 확인.
    /// 6) VFS ON 시 vfs_console_{PID}.log 의 CloseHandle CRITICAL 과 close_flow 타임스탬프 대조.
    /// </summary>
    public static class CloseFlowDiagnostics
    {
        public enum ClosePhase
        {
            SessionBegin,
            ActivateForClose,
            ConfirmSaveYes,
            ConfirmSaveNo,
            ConfirmCancel,
            SaveAsBegin,
            SaveAsEnd,
            SaveAsFailed,
            BeforeCloseAndDiscard,
            AfterCloseAndDiscard,
            CloseException,
            CloseFallbackClose,
            DocumentToBeDestroyed,
            DocumentDestroyed,
            DestroyBranchApplyProtection,
            DestroyBranchDeleteFiles,
            ApplyProtectionAsyncStart,
            ApplyProtectionBegin,
            ApplyProtectionCommitDone,
            ApplyProtectionDeleteBegin,
            ApplyProtectionDeleteEnd,
            ApplyProtectionEnd,
            ApplyProtectionFailed,
            RemoveOpenDocument,
            SessionComplete
        }

        private static readonly ILog Logger = PluginLogger.getLogger("CloseFlow", "close_flow.log");
        private static readonly ConcurrentDictionary<string, string> CorrelationByKey =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static int _sessionCounter;

        public static string BeginSession(string trigger, ProtectedDocument protectedDocument)
        {
            string correlationId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{_sessionCounter++ % 1000:D3}";
            RegisterKeys(correlationId, protectedDocument);

            Log(correlationId, ClosePhase.SessionBegin,
                $"trigger={trigger} pid={Process.GetCurrentProcess().Id} " +
                DescribeDocument(protectedDocument));

            return correlationId;
        }

        public static void RegisterKeys(string correlationId, ProtectedDocument protectedDocument)
        {
            if (String.IsNullOrWhiteSpace(correlationId) || protectedDocument == null)
            {
                return;
            }

            RegisterKey(correlationId, protectedDocument.filePath);
            RegisterKey(correlationId, protectedDocument.decryptedTemporaryFilePath);
        }

        public static void RegisterKey(string correlationId, string pathOrKey)
        {
            if (String.IsNullOrWhiteSpace(correlationId) || String.IsNullOrWhiteSpace(pathOrKey))
            {
                return;
            }

            CorrelationByKey[pathOrKey] = correlationId;
        }

        public static void LogPhase(string correlationId, ClosePhase phase, string detail = null)
        {
            if (String.IsNullOrWhiteSpace(correlationId))
            {
                return;
            }

            Log(correlationId, phase, detail);
        }

        public static void LogPhaseByPath(string pathOrDocName, ClosePhase phase, string detail = null)
        {
            if (String.IsNullOrWhiteSpace(pathOrDocName))
            {
                return;
            }

            string key = NormalizeLookupKey(pathOrDocName);
            if (!CorrelationByKey.TryGetValue(key, out string correlationId))
            {
                Logger.Warn($"[CLOSE-FLOW] id=unknown phase={phase} path='{ShortPath(pathOrDocName)}' (no active session) {detail}");
                return;
            }

            Log(correlationId, phase, $"path='{ShortPath(pathOrDocName)}' {detail}");
        }

        public static void LogPhaseForDocument(Document document, ProtectedDocument protectedDocument, ClosePhase phase, string detail = null)
        {
            string correlationId = ResolveCorrelationId(document, protectedDocument);
            if (String.IsNullOrWhiteSpace(correlationId))
            {
                Logger.Warn($"[CLOSE-FLOW] id=unknown phase={phase} {detail}");
                return;
            }

            Log(correlationId, phase, DescribeCadDocument(document) + " " + (detail ?? ""));
        }

        public static void Complete(string correlationId)
        {
            LogPhase(correlationId, ClosePhase.SessionComplete, "plugin close-flow finished");
            RemoveSession(correlationId);
        }

        public static void CompleteByPath(string pathOrDocName)
        {
            string key = NormalizeLookupKey(pathOrDocName);
            if (CorrelationByKey.TryGetValue(key, out string correlationId))
            {
                Complete(correlationId);
            }
        }

        public static int ReadDbMod()
        {
            try
            {
                return Convert.ToInt16(CadCoreApplication.GetSystemVariable("DBMOD"));
            }
            catch
            {
                return -1;
            }
        }

        public static string DescribeCadDocument(Document document)
        {
            if (document == null)
            {
                return "cad=null";
            }

            try
            {
                return $"cadName='{ShortPath(document.Name)}' disposed={document.IsDisposed} " +
                       $"unmanaged={(document.UnmanagedObject != IntPtr.Zero)} dbmod={ReadDbMod()}";
            }
            catch (Exception ex)
            {
                return $"cad=error({ex.Message})";
            }
        }

        /// <summary>
        /// 닫기 직전 MdiActive 문서와 대상 문서가 같은지 (비활성 닫기 NRE 진단용).
        /// </summary>
        public static string DescribeMdiActiveContext(Document targetDocument)
        {
            try
            {
                Document active = Application.DocumentManager.MdiActiveDocument;
                bool isTargetActive = ReferenceEquals(active, targetDocument);

                return $"mdiActive='{ShortPath(active?.Name)}' target='{ShortPath(targetDocument?.Name)}' " +
                       $"isTargetActive={isTargetActive}";
            }
            catch (Exception ex)
            {
                return $"mdi=error({ex.Message})";
            }
        }

        private static string DescribeDocument(ProtectedDocument protectedDocument)
        {
            if (protectedDocument == null)
            {
                return "pd=null";
            }

            return $"filePath='{ShortPath(protectedDocument.filePath)}' " +
                   $"temp='{ShortPath(protectedDocument.decryptedTemporaryFilePath)}' " +
                   $"protected={protectedDocument.isProtected} edit={protectedDocument.isEdit} " +
                   $"needLabeling={protectedDocument.needLabeling} isUpdated={protectedDocument.isUpdated} " +
                   $"dbmod={ReadDbMod()}";
        }

        private static string ResolveCorrelationId(Document document, ProtectedDocument protectedDocument)
        {
            if (protectedDocument != null)
            {
                if (!String.IsNullOrWhiteSpace(protectedDocument.filePath) &&
                    CorrelationByKey.TryGetValue(protectedDocument.filePath, out string byFile))
                {
                    return byFile;
                }

                if (!String.IsNullOrWhiteSpace(protectedDocument.decryptedTemporaryFilePath) &&
                    CorrelationByKey.TryGetValue(protectedDocument.decryptedTemporaryFilePath, out string byTemp))
                {
                    return byTemp;
                }
            }

            if (document != null && !String.IsNullOrWhiteSpace(document.Name))
            {
                string key = NormalizeLookupKey(document.Name);
                if (CorrelationByKey.TryGetValue(key, out string byName))
                {
                    return byName;
                }
            }

            return null;
        }

        private static void Log(string correlationId, ClosePhase phase, string detail)
        {
            Logger.Info($"[CLOSE-FLOW] id={correlationId} phase={phase} {detail}".Trim());
        }

        private static void RemoveSession(string correlationId)
        {
            foreach (var pair in CorrelationByKey)
            {
                if (pair.Value == correlationId)
                {
                    CorrelationByKey.TryRemove(pair.Key, out _);
                }
            }
        }

        private static string NormalizeLookupKey(string pathOrDocName)
        {
            try
            {
                return Path.GetFullPath(pathOrDocName);
            }
            catch
            {
                return pathOrDocName ?? String.Empty;
            }
        }

        private static string ShortPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            return Path.GetFileName(path);
        }
    }
}
