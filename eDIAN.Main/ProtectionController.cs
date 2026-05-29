using ZwSoft.ZwCAD.ApplicationServices;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using eDIAN.Main.Protect;
using log4net;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Exceptions;
using Microsoft.InformationProtection.File;
using Microsoft.InformationProtection.Policy;
using Microsoft.InformationProtection.Protection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Action = Microsoft.InformationProtection.Policy.Actions.Action;
using eDIAN.Hook;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using Label = Microsoft.InformationProtection.Label;
using eDIAN.Main.API;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.Linq;

namespace eDIAN.Main
{
    public class ProtectionController
    {
        private static readonly ILog logger = PluginLogger.getLogger("ProtectionController", "application.log");

        public event EventHandler OnSignout;                               // 로그아웃 처리 후 발생하는 문서 닫기 이벤트          (closeDocuments() 호출)
        public event EventHandler<EventArgs> OnChangeAuthStatus;           // 로그인/로그아웃 처리 후 발생하는 UI 변경 이벤트     (changeFormByAuthstatus() 호출)
        public event EventHandler<MessageEventArgs> OnRaiseError;          // 레이블 적용 후 발생하는 결과 메세지 팝업 이벤트     (showNotificationWindow() 호출) 
        // public event EventHandler<ProtectedDocumentEventArgs> OnOpenDocument;   // 파일 열기 처리 후 레이블 아이콘 변경 이벤트 (displayLabelIcons 호출)

        // MIP 인증 및 보호 처리를 위한 클라이언트
        private static IPublicClientApplication publicClientApplication;

        // API 접근을 위한 인증 처리 인터페이스(IAuthDelegate) 구현체
        private AuthDelegate authDelegate;

        private IFileProfile fileProfile;
        private IFileEngine fileEngine;
        private IPolicyProfile policyProfile;
        private IPolicyEngine policyEngine;

        // MIP Context 
        private MipContext mipContext;

        // 파일에 레이블을 적용하기 위한 파일 옵션 정보 구조체
        public struct FileOptions
        {
            public String FileName;
            public String OutputFileName;
            public String LabelId;
            public DataState DataState;
            public AssignmentMethod AssignmentMethod;
            public ActionSource ActionSource;
            public bool IsAuditDiscoveryEnabled;
            public bool GenerateChangeAuditEvent;
            public bool EnableDocTracking;
            public bool NotifyOwnerOnOpen;      // 보호된 문서에 대한 추적 (true : 보호 문서 복호화 할 때마다 이메일 알람)
        }

        // 기본 민감도 레이블
        private Label defaultLabel;

        // 문서 메뉴 관리자
        private ApplicationController applicationController;

        public ProtectionController()
        {
            this.defaultLabel = null;

            this.applicationController = ApplicationController.getInstance();
        }

        /// <summary>
        /// 어플리케이션 정보를 클라이언트 사용해 인증 후 인증토큰 캐시 저장, 보안 요소 초기화 
        /// </summary>
        public async Task createApplication()
        {
            logger.Debug("createApplication");

            try
            {
                BrokerOptions brokerOptions = new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = CommonConstants.APPLICATION_NAME };

                String tenantStr = "";

                if (String.IsNullOrWhiteSpace(ServiceConstants.TENANT_ID) == false)
                {
                    tenantStr = ServiceConstants.TENANT_ID;

                    // ** 클라이언트 ID와 테넌트 아이디를 이용해 publicClientApplication을 초기화하는 기존 방식. 테넌트 아이디가 고정되어 있어 단일 테넌트 지원에 적합. (다중 테넌트 지원 불가) 
                    ProtectionController.publicClientApplication = PublicClientApplicationBuilder.Create(ServiceConstants.CLIENT_ID)
                            .WithAuthority($"{ServiceConstants.MIP_LOGIN_FORM_URL}{tenantStr}")
                            .WithDefaultRedirectUri()
                            .WithBroker(brokerOptions)
                            .Build();
                }
                else
                {
                    tenantStr = "organizations";

                    // ** 클라이언트 ID와 리디렉션 URI를 이용해 publicClientApplication을 초기화하는 방식. 테넌트 아이디는 로그인 시 사용자가 속한 테넌트로 동적으로 결정됨. (다중 테넌트 지원)
                    ProtectionController.publicClientApplication =
                        PublicClientApplicationBuilder.Create(ServiceConstants.CLIENT_ID)
                            .WithAuthority($"{ServiceConstants.MIP_LOGIN_FORM_URL}{tenantStr}")
                            .WithRedirectUri("com.microsoft.rms-sharing-for-win://authorize/")
                            .WithBroker(brokerOptions)
                            .Build();
                }

                // 비동기식 캐시 처리 helper 객체를 생성
                MsalCacheHelper cacheHelper = await this.createCacheHelperAsync().ConfigureAwait(false); 

                // 사용자 토큰 캐시를 저장
                cacheHelper.RegisterCache(ProtectionController.publicClientApplication.UserTokenCache);

                logger.Debug("MSAL Application created successfully.");

                // 어플리케이션 및 클라이언트ID, 버전 정보를 통해 MIP 접속 후 파일 및 정책 프로필 MIP 보안 구성 요소를 구한다.

                await this.initalizeMipComponents();
            }
            catch (Exception ex)
            {
                logger.Error($" - createApplication :", ex);
            }
        }

        /// <summary>
        /// MSAL 토큰 캐시 파일을 어디에, 어떻게 저장할지 설정하고, 그 설정으로 캐시 헬퍼를 생성
        /// </summary>
        /// <returns></returns>
        private async Task<MsalCacheHelper> createCacheHelperAsync()
        {
            // 캐시 저장 위치 및 파일을 생성

            String cacheFileName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".msalcache.bin";
            String cacheFileDirectory = MsalCacheHelper.UserRootDirectory;

            logger.Debug($"createCacheHelperAsync : CacheFilePath = {Path.Combine(cacheFileDirectory, cacheFileName)}");

            StorageCreationProperties storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, cacheFileDirectory).Build();

            MsalCacheHelper cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties, new TraceSource("MSAL.CacheTrace")).ConfigureAwait(false);

            return cacheHelper;
        }

        /// <summary>
        /// 어플리케이션 및 클라이언트ID, 버전 정보를 통해 MIP 접속 후 
        /// 파일 및 정책 프로필 MIP 보안 구성 요소를 구한다. 
        /// </summary>
        private async Task initalizeMipComponents()
        {
            try
            {
                logger.Debug("initalizeMipComponents");

                // 파일 SDK 작업을 위한 래퍼를 초기화합니다.
                MIP.Initialize(MipComponent.File);
                logger.Debug("Initialize Wrapper for File SDK operations.");

                // Microsoft Entra 앱 등록의 clientID를 ApplicationId로 설정하여 ApplicationInfo를 생성.
                ApplicationInfo appInfo = new ApplicationInfo()
                {
                    ApplicationId = ServiceConstants.CLIENT_ID,
                    ApplicationName = CommonConstants.APPLICATION_NAME,
                    ApplicationVersion = CommonConstants.INFORMATION_APPLICATION_VERSION
                };

                // AppInfo를 전달하여 AuthDelegateImpl 객체를 인스턴스화.
                this.authDelegate = new AuthDelegate(appInfo, ProtectionController.publicClientApplication);

                logger.Debug("[Instantiate the AuthDelegateImpl object, passing in AppInfo]");
                logger.Debug($" - mipRootPath is exists.");

                // MIP root 경로를 기준으로 MipConfiguration 객체 생성 (로그 레벨: Trace, 캐시 저장소 유형: OnDiskEncrypted)
                MipConfiguration mipConfiguration = new MipConfiguration(appInfo, CommonConstants.PLUGIN_MIP_ROOT_PATH, Microsoft.InformationProtection.LogLevel.Trace, false, CacheStorageType.OnDiskEncrypted);
                logger.Debug(" - Create MipConfiguration Object");

                // 생성된 MIPConfiguration 을 이용해 MIPContext 생성
                this.mipContext = MIP.CreateMipContext(mipConfiguration);
                logger.Debug(" - Create MipContext using Configuration");

                // 생성된 Context를 이용해 FileProfileSettings 객체 생성 (로컬 상태를 생성/사용하기 위해 파일 프로필 설정을 초기화)
                FileProfileSettings profileSettings = new FileProfileSettings(this.mipContext, CacheStorageType.OnDiskEncrypted, new ConsentDelegate());
                logger.Debug(" - Initialize and instantiate the File Profile.");

                // 파일 프로필을 비동기로 로드하고 결과 응답 대기 
                this.fileProfile = await MIP.LoadFileProfileAsync(profileSettings);

                // Context를 이용해 PolicyProfileSettings 객체를 생성하고 초기화 
                PolicyProfileSettings policySettings = new PolicyProfileSettings(this.mipContext, CacheStorageType.OnDiskEncrypted);

                // 정책 프로필을 비동기 식으로 로드하고 결과 응답 대기
                this.policyProfile = await MIP.LoadPolicyProfileAsync(policySettings);

                logger.Debug(" - Initialize Wrapper for File SDK operations.");
            }
            catch (NetworkException ex)
            {
                logger.Error($" - initalizeMipComponents NetworError", ex);
                MessageHandler.Show("error.mip.connect");           // MIP 초기화 중 네트워크 연결에 문제가 발생 했습니다.
            }
            catch (Exception ex)
            {
                logger.Error($" - initalizeMipComponents Error", ex);
                MessageHandler.Show("error.mip.initialize");       // MIP 초기화 중 오류가 발생했습니다.
            }
        }

        /// <summary>
        /// MIP 로그인 또는 로그아웃 처리. 인증 결과는 <see cref="PluginApplication.authenticationResult"/>에 저장한다.
        /// </summary>
        /// <returns></returns>
        public async Task executeAuthentificationAsync(IntPtr handle)
        {
            if (ProtectionController.publicClientApplication == null)
            {
                logger.Error(" - executeAuthentificationAsync : publicClientApplication is null");

                return;
            }

            if (PluginApplication.authStatus == CommonConstants.AuthStatus.AUTH)
            {
                // 로그인 상태일 경우 로그아웃 처리

                // MIP 클라이언트에서 인증 토큰을 구한다.
                IEnumerable<IAccount> accounts = await ProtectionController.publicClientApplication.GetAccountsAsync();

                if (accounts.Any())
                {
                    // 인증 토큰이 존재하는 경우에는 로그아웃처리
                    try
                    {
                        OnSignout?.Invoke(this, EventArgs.Empty);               //--> 문서 닫기 이벤트 호출 closeDocuments()

                        // 인증 토큰 제거
                        await ProtectionController.publicClientApplication.RemoveAsync(accounts.FirstOrDefault());

                        // 파일엔진, 인증결과 릴리즈
                        fileEngine?.Dispose();
                        fileEngine = null;

                        PluginApplication.authenticationResult = null;

                        // 로그인 사용자 정보 클리어

                        PluginApplication.userLicenseData.userName = "";
                        PluginApplication.userLicenseData.userId = "";
                        PluginApplication.userLicenseData.licenseType = "";
                        PluginApplication.userLicenseData.licenseTypeName = "";
                        PluginApplication.userLicenseData.hasLicense = false;
                        PluginApplication.userLicenseData.isOnlyView = true;

                        PluginApplication.authStatus = CommonConstants.AuthStatus.NONE;

                        // 이벤트 호출. 인증 상태에 따른 폼 구성 변경 : changeFormByAuthstatus()
                        this.OnChangeAuthStatus?.Invoke(this, new EventArgs());

                        // 임시 파일 삭제
                        FileManager.deleteMipTempFiles();

                        // 퀵 메뉴, 리본 메뉴, 클래식 메뉴 초기화 
                        this.applicationController.setApplicationMenuByLabels(null);
                    }
                    catch (NetworkException ex)
                    {
                        logger.Error($"- executeAuthentificationAsync(Logout)", ex);
                    }
                    catch (MsalException ex)
                    {
                        logger.Error($"- executeAuthentificationAsync(Logout)", ex);
                    }
                    finally
                    {
                        // 임시 경로에 대한 everyone 사용자 권한 부여
                        FileManager.setUserMipTempWritePermissions(false);
                    }
                }
            }
            else
            {
                if (this.policyProfile == null)
                {
                    logger.Error(" - executeAuthentificationAsync : policyEngine is null");

                    return;
                }

                if (this.fileProfile == null)
                {
                    logger.Error(" - executeAuthentificationAsync : fileEngine is null");

                    return;
                }

                // 로그아웃 상태일 경우 로그인 처리

                // 전달 받은 로그인 핸들러를 통해 인증 토큰를 구한다.
                PluginApplication.authenticationResult = await this.getAuthenticationResultAsync(handle);

                if (PluginApplication.authenticationResult != null)
                {
                    // 인증 토큰의 TanentId를 저장
                    ServiceConstants.TENANT_ID = PluginApplication.authenticationResult.TenantId;

                    try
                    {
                        String authInfo;

                        try
                        {
                            // 인증 토큰 값으로 로그인 웹화면을 통해 로그인한 인증 정보 조회(Web API 호출)
                            authInfo = await MicrosoftService.Instance.callAuthentificationWithToken(PluginApplication.authenticationResult.AccessToken);
                        }
                        catch (HttpRequestException ex)
                        {
                            logger.Error("MIP Graph API 호출 중 HttpRequestException", ex);

                            MessageHandler.Show("error.mip.auth.connect");           // MIP 사용자 정보 조회 중 네트워크 오류가 발생했습니다.\n네트워크 연결 상태를 확인한 후 다시 시도해 주세요.

                            return;
                        }
                        catch (Exception ex)
                        {
                            logger.Error("MIP Graph API 호출 중 예외 발생", ex);

                            MessageHandler.Show("error.mip.user.search");       // MIP 사용자 정보 조회 중 오류가 발생했습니다.\n관리자에게 문의해 주세요.

                            return;
                        }

                        // JSON 형식 체크

                        if (String.IsNullOrWhiteSpace(authInfo) || authInfo.TrimStart().StartsWith("<") || authInfo.TrimStart().StartsWith("System."))      // 예외 ToString() 패턴 방어
                        {
                            logger.Error("MIP Graph API 응답이 JSON 형식이 아닙니다.");

                            MessageHandler.Show("error.mip.user.response.format");       // MIP 사용자 정보 응답 형식이 올바르지 않습니다.\n관리자에게 문의해 주세요.

                            return;
                        }

                        JObject authJson;

                        try
                        {
                            authJson = JObject.Parse(authInfo);
                        }
                        catch (JsonReaderException jex)
                        {
                            logger.Error("MIP Graph 응답 JSON 파싱 실패", jex);

                            MessageHandler.Show("error.mip.user.response.parse");       // MIP 사용자 정보 응답(JSON) 파싱 중 오류가 발생했습니다.\n관리자에게 문의해 주세요.

                            return;
                        }

                        // 인증정보 내에 사용자 정보

                        String displayName = authJson["displayName"]?.ToString() ?? "";
                        String userPrincipalName = authJson["userPrincipalName"]?.ToString() ?? "";

                        if (String.IsNullOrEmpty(userPrincipalName))
                        {
                            logger.Error("Graph 응답에 userPrincipalName 이 없습니다.");

                            MessageHandler.Show("error.mip.user.response.missing", "Error");       // MIP 사용자 정보에 계정 정보가 없습니다.\n관리자에게 문의해 주세요.

                            return;
                        }

                        if (IApplicationService.instance != null)
                        {
                            // tanent, ClientId, MIP 계정 (userInfo.UserPrincipalName) 정보를 이용해
                            // 관리자 사이트의 사용자 라이센스 조회 API 엔드포인트 호출. 라이센스 형태 (licType)를 구한 후 userInfo.isOnlyView 값을 세팅

                            PluginApplication.userLicenseData = await IApplicationService.instance
                                .CallGetUserLicenseDataAsync(displayName, userPrincipalName)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            // 관리자 사이트 API 서비스가 초기화 되지 않은 경우 (예: 로그인 후 라이센스 조회 API 호출 실패 등) 기본값으로 Viewer 라이센스 세팅. (최소 권한으로 로그인 유지)

                            PluginApplication.userLicenseData = new UserLicenseData()
                            {
                                userName = displayName,
                                userId = userPrincipalName,
                                hasLicense = false,
                                licenseType = "V",
                                licenseTypeName = "Viewer",
                                isOnlyView = true
                            };
                        }

                        // UserPrincipalName > PolicyEngineSettings > PolicyEngine 생성

                        PolicyEngineSettings profileEngineSetting = new PolicyEngineSettings(userPrincipalName, this.authDelegate, "", "en-US");

                        profileEngineSetting.Identity = new Identity(userPrincipalName);

                        this.policyEngine = await this.policyProfile.AddEngineAsync(profileEngineSetting);

                        // 기본 민감도 레이블 조회
                        this.defaultLabel = this.policyEngine.GetDefaultSensitivityLabel();

                        if (this.defaultLabel != null)
                        {
                            logger.Debug(String.Format(" - Default label {0} : {1} - {2}", defaultLabel.Name, defaultLabel.Id, defaultLabel.Description));
                        }
                        else
                        {
                            logger.Debug(" - Default Sensitivity Label is null");
                        }

                        //////////////////////////////////////////////////////////
                        // MIP 민감도 레이블 콤보박스 구성을 위한 레이블 목록 구성
                        //////////////////////////////////////////////////////////

                        // UserPrincipalName > FileEngineSettings add identity > FileEngine 생성

                        FileEngineSettings fileEngineSettings = new FileEngineSettings(userPrincipalName, this.authDelegate, "", "en-US");

                        /************************************************************************
                         파일엔진 커스텀 설정 추가
                        ************************************************************************/

                        List<KeyValuePair<String, String>> customSettings = fileEngineSettings.CustomSettings;

                        if (customSettings == null)
                        {
                            customSettings = new List<KeyValuePair<String, String>>();
                        }

                        // 레이블링 전 암호화 된 문서 파일의 최대 크기 설정 추가 (60GB)
                        customSettings.Add(new KeyValuePair<String, String>("MaxFileSizeForProtection", CommonConstants.MAX_PROTECTED_FILE_SIZE.ToString()));

                        fileEngineSettings.CustomSettings = customSettings;

                        /************************************************************************/

                        // 파일엔진 설정에 사용자 아이덴티티 추가
                        fileEngineSettings.Identity = new Identity(userPrincipalName);

                        this.fileEngine = await this.fileProfile.AddEngineAsync(fileEngineSettings);

                        // 기존 파일 목록 클리어
                        PluginApplication.documentHandler.clearOpenDocument(true);

                        PluginApplication.authStatus = CommonConstants.AuthStatus.AUTH;

                        // 이벤트 호출. 인증 상태에 따라 인증 정보보 폼에 반영 (changeFormByAuthstatus)

                        this.OnChangeAuthStatus?.Invoke(this, new EventArgs());

                        // 서비스 서버에 로그인 성공 로그 전송
                        if (IApplicationService.instance != null)
                        {
                            await IApplicationService.instance.CallSaveUserActionLogAsync("", "", "Login", "0").ConfigureAwait(false);
                        }
                    }
                    catch (NetworkException ex)
                    {
                        logger.Error($"- executeAuthentificationAsync(Login)", ex);
                    }
                    catch (Exception e)
                    {
                        logger.Error($" - executeAuthentificationAsync(Login)", e);
                    }
                    finally
                    {
                        // 임시 경로에 대한 everyone 사용자 권한 부여
                        FileManager.setUserMipTempWritePermissions(false);
                    }
                }
                else
                {
                    logger.Debug(" - executeAuthentificationAsync : authResult is null.");
                }
            }
        }

        /// <summary>
        /// 민감도 레이블 선택 콤보박스 옵션 목록 생성.
        /// <para><paramref name="managedLabelDataList"/>가 null 이거나 비어 있으면(내부 연동) MIP 레이블을 필터 없이 모두 추가한다.</para>
        /// <para>1건 이상이면(외부·관리자 연동) 목록의 <c>labelId</c>와 일치하는 MIP 레이블만 추가한다. 빈 배열 <c>[]</c>도 null 과 동일하게 전체 표시한다.</para>
        /// </summary>
        /// <param name="managedLabelDataList">관리자 API 레이블 목록. null 또는 빈 목록 시 MIP 전체 레이블 사용.</param>
        /// <returns></returns>
        public List<SensitivityLabelsOption> createSensitivityLabelOptions(List<LabelData> managedLabelDataList)
        {
            List<SensitivityLabelsOption> sensitivityLabels = new List<SensitivityLabelsOption>();

            if (this.fileEngine == null)
            {
                logger.Error(" - createSensitivityLabelOption : fileEngine is null");

                return sensitivityLabels;
            }

            // 파일엔진에서 민감도 레이블 목록 조회
            ReadOnlyCollection<Label> labels = this.fileEngine.SensitivityLabels;

            // 기본 선택 옵션 추가 (authStatus 를 이용해 sensitivityLabels도 변경하도록 통합. 동일 한 이벤트 구현체에서 처리 되게 수정)
            sensitivityLabels.Add(new SensitivityLabelsOption(PluginApplication.global.getMessage("select.sensitivity.label"), ""));

            foreach (Label label in labels)
            {
                //logger.Debug(String.Format("{0} : {1} - {2}", label.Name, label.Id, label.Description));

                // 기본 레이블과 동일한 레이블의 레이블명 접미어 추가
                String strDefaultLabelName = "";

                if (this.defaultLabel != null && this.defaultLabel.Id == label.Id)
                {
                    strDefaultLabelName = "(기본 레이블)";
                }

                // 외부(관리자) 연동: managedLabelDataList에 labelId가 있는 MIP 레이블만 포함.
                // null 또는 Count==0(빈 배열)이면 아래 else — 내부 연동, MIP 레이블 전체.

                if (managedLabelDataList != null && managedLabelDataList.Count > 0)
                {
                    foreach (LabelData labelData in managedLabelDataList)
                    {
                        if (label.Id.Equals(labelData.labelId))
                        {
                            sensitivityLabels.Add(new SensitivityLabelsOption(label.Name + strDefaultLabelName, label.Id, labelData.sequence, labelData.isDefaultLabel));
                        }
                    }
                }
                else
                {
                    // 내부 시스템 연동. 레이블 목록에 민감도 레이블 추가 (레이블명, 레이블값)

                    sensitivityLabels.Add(new SensitivityLabelsOption(label.Name + strDefaultLabelName, label.Id));
                }
            }

            return sensitivityLabels;
        }

        /// <summary>
        /// 전달 받은 어플리케이션의 핸들러를 기준으로 인증 처리
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        public async Task<AuthenticationResult> getAuthenticationResultAsync(IntPtr handle)
        {
            AuthenticationResult result = null;

            if (ProtectionController.publicClientApplication == null)
            {
                logger.Error(" - getAuthenticationResultAsync : publicClientApplication is null");
                return result;
            }

            String[] scopes = new String[] { "user.read" };

            IEnumerable<IAccount> accounts;

            try
            {
                // 캐시에 있는 계정 목록 조회 (가능하면 한 번만)
                accounts = await publicClientApplication.GetAccountsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error(" - getAuthenticationResultAsync - GetAccountsAsync Exception", ex);
                return null;
            }

            IAccount account = accounts.FirstOrDefault();

            Boolean needInteractive = false;

            if (account != null)
            {
                try
                {
                    // 1단계: Silent 인증 시도 (캐시 / 기존 계정 재사용)

                    result = await publicClientApplication.AcquireTokenSilent(scopes, account).ExecuteAsync().ConfigureAwait(false);

                    return result; // Silent 성공 시 바로 반환
                }
                catch (MsalUiRequiredException ex)
                {
                    // 토큰 만료, 조건부 액세스 등으로 UI가 필요한 경우
                    logger.Debug($"getAuthenticationResultAsync - MsalUiRequiredException(Silent): {ex.Message}");
                    needInteractive = true;
                }
                catch (Exception ex)
                {
                    // 기타 예외는 로그만 남기고 Interactive 로 넘김
                    logger.Error(" - getAuthenticationResultAsync - AcquireTokenSilent Exception", ex);
                    needInteractive = true;
                }
            }
            else
            {
                // 캐시에 계정이 없으면 바로 Interactive 필요
                needInteractive = true;
            }

            if (!needInteractive)
            {
                return result;
            }

            try
            {
                // 2단계: Interactive 인증 시도 (핸들러 기반, 계정 힌트 포함)

                AcquireTokenInteractiveParameterBuilder builder = publicClientApplication.AcquireTokenInteractive(scopes).WithParentActivityOrWindow(handle).WithPrompt(Prompt.SelectAccount);

                if (account != null)
                {
                    // 캐시에 계정이 있으면 힌트로 넘김
                    builder = builder.WithAccount(account);
                }

                result = await builder.ExecuteAsync().ConfigureAwait(false);
            }
            catch (MsalServiceException ex) when (ex.ErrorCode == MsalError.UnknownBrokerError)
            {
                // 브로커(WAM) 쪽 unknown_broker_error 방어
                logger.Error(" - getAuthenticationResultAsync - AcquireTokenInteractive unknown_broker_error", ex);

                if (ex.AdditionalExceptionData != null)
                {
                    foreach (var kv in ex.AdditionalExceptionData)
                    {
                        logger.Error($"   BrokerAdditionalData: {kv.Key} = {kv.Value}");
                    }
                }

                result = null;
            }
            catch (MsalUiRequiredException ex)
            {
                // 이론상 Interactive 에서는 잘 안 나오지만, 방어용
                logger.Error(" - getAuthenticationResultAsync - AcquireTokenInteractive MsalUiRequiredException", ex);
                result = null;
            }
            catch (Exception ex)
            {
                logger.Error(" - getAuthenticationResultAsync - AcquireTokenInteractive Exception", ex);
                result = null;
            }

            return result;
        }

        /// <summary>
        /// 해당 파일경로에 대한 MIP에서 파일 보호 정보를 조회 후 CAD 파일 열고 권한에 따라 CAD 및 메인 폼에 UI 세팅 처리
        /// </summary>
        /// <param name="filePath"></param>        
        /// <param name="id"></param>        
        public async Task openDocumentFile(String filePath, String id = null)
        {
            logger.Debug("----------------------------------------------------");
            logger.Debug("* protectController : openDocumentFile ");
            logger.Debug("----------------------------------------------------");

            if (PluginApplication.authStatus == CommonConstants.AuthStatus.NONE)
            {
                MessageHandler.Show("user.auth.need");             // 로그인 후 사용 가능합니다.

                return;
            }

            if (String.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageHandler.Show("file.not.found");             // 파일경로에 해당하는 파일이 존재하지 않습니다.

                return;
            }

            ProtectedDocument existDocument = PluginApplication.documentHandler.getOpenDocument(filePath);

            // 존재하는 문서에 대한 처리 로직 추가

            bool isOpenReadOnly = false;

            if (existDocument != null)
            {
                if (existDocument.isProtected == false)
                {
                    if (existDocument.isReadOnly == false)
                    {
                        // 이미 열려 문서가 일반 문서이고 읽기 전용 문서가 아닌 경우 : "선택한 파일은 이미 열려 있습니다. 읽기 전용으로 열겠습니까?", "도면 열기"

                        DialogResult result = MessageHandler.Show("confirm.file.already.open");

                        if (result == DialogResult.Yes)
                        {
                            isOpenReadOnly = true;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                else
                {
                    // 이미 열려 있는 보호 문서인 경우

                    logger.Debug("선택한 파일은 이미 열려 있습니다.");

                    MessageHandler.Show("file.already.open");             // 선택한 파일은 이미 열려 있습니다.

                    return;
                }
            }

            // [중요]MIP 파일 핸드러에서 권한, 권한 부여자, 레이블정보, 파일을 복호화 후 복호화된 임시파일 정보가 포함된 문서 보호 정보 생성
            ProtectedDocument protectedDocument = await this.patchProtectionData(filePath);

            if (protectedDocument.message != String.Empty)
            {
                // 권한 처리 메세지가 없는 경우 메세지창을 표시하지 않음.
                logger.Debug($"protectionDocument.message : {protectedDocument.message}");

                OnRaiseError?.Invoke(this, new MessageEventArgs(protectedDocument.message, "Error"));   // 권한 처리 메세지 이벤트 호출

                return;
            }

            // 대상 파일명
            String openFilePath = filePath;

            if (protectedDocument.isProtected)
            {
                // 파일명을 임시 파일명으로 대체 
                openFilePath = protectedDocument.decryptedTemporaryFilePath;
            }

            if (!File.Exists(openFilePath))
            {
                // 파일이 존재 하지 않는 경우 파일을 열지 않음.
                logger.Debug($"File {openFilePath} does not exist.");

                return;
            }


            String errorCode = "-401";

            try
            {
                // 권한 부여된 파일이 존재하는 경우 CAD 파일을 읽기/쓰기 모드로 열고
                // 열린 파일 CAD 파일 객체를 반환 (VFS·MIP 복호화 직후 1회 재시도)

                Document document = null;
                const int maxOpenAttempts = 2;
                for (int attempt = 1; attempt <= maxOpenAttempts; attempt++)
                {
                    try
                    {
                        document = CadApplication.DocumentManager.Open(openFilePath, isOpenReadOnly);
                        if (document != null)
                            break;
                        logger.Warn($"Open attempt {attempt}/{maxOpenAttempts} returned null for ...\\{Path.GetFileName(openFilePath)}");
                    }
                    catch (Exception openEx)
                    {
                        logger.Warn($"Open attempt {attempt}/{maxOpenAttempts} failed: {openEx.Message}");
                        if (attempt >= maxOpenAttempts)
                            throw;
                    }
                    if (attempt < maxOpenAttempts)
                        System.Threading.Thread.Sleep(250);
                }

                if (document != null)
                {
                    if (protectedDocument.isProtected &&
                        !String.IsNullOrWhiteSpace(protectedDocument.decryptedTemporaryFilePath))
                    {
                        VfsInterceptor.FinalizeOpenVaporize(protectedDocument.decryptedTemporaryFilePath);
                    }

                    // 문서 handle, 해시코드, 읽기전용 여부 추가

                    protectedDocument.handle = document.Window.Handle;
                    protectedDocument.hashCode = document.GetHashCode();
                    protectedDocument.isReadOnly = document.IsReadOnly;

                    // 보호된 도면인 레이블에 해당하는 메뉴로 세팅
                    this.applicationController.setApplicationMenuByLabels(protectedDocument);

                    // 열린 문서 목록에 추가
                    PluginApplication.documentHandler.addOpenDocument(protectedDocument, id);

                    errorCode = "0";

                    logger.Debug($" - Open Document : '...\\{Path.GetFileName(protectedDocument.filePath)}'");
                    logger.Debug(protectedDocument.ToString());
                }
                else
                {
                    logger.Error($" - Failed to open document : '...\\{Path.GetFileName(openFilePath)}'");

                    MessageHandler.Show("document.cannot.open");             // 도면 파일을 열 수 없습니다.
                }
            }
            catch (NetworkException ex)
            {
                logger.Error($" - getProtectionInfo Excception", ex);

                PluginApplication.documentHandler.removeOpenDocument(protectedDocument);

                MessageHandler.Show("error.network.connection");       // 네트워크 연결에 문제가 발생했습니다. 다시 시도하세요.
            }
            catch (Exception ex)
            {
                logger.Error($" - getProtectionInfo Excception", ex);

                PluginApplication.documentHandler.removeOpenDocument(protectedDocument);

                MessageHandler.Show("error.document.open");    // 도면 파일을 여는 중 오류가 발생했습니다. 다시 시도 하세요.
            }
            finally
            {
                FileManager.setUserMipTempWritePermissions(false);

                if (protectedDocument.isProtected)
                {
                    // # 보안이 적용된 문서
                    logger.Debug(" - Protected document.");

                    if (!protectedDocument.isPrint && !protectedDocument.isOwner)
                    {
                        // 화면 캡처 방지 적용 (출력 권한이 없고 소유자도 아닌 경우)
                        DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, true);
                    }
                    else
                    {
                        // 화면 캡처 방지 해제 (출력 권한이 있거나 소유자인 경우)
                        DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, false);
                    }

                    // 보호된 도면이 활성화 될 때 권한에 따라 활성화 비활성화 처리
                    this.applicationController.setApplicationMenuByLabels(protectedDocument);

                    if (IApplicationService.instance != null)
                    {
                        // 보호 적용된 파일을 열때 로그 저장
                        await IApplicationService.instance
                            .CallSaveUserActionLogAsync(protectedDocument.contentId, protectedDocument.filePath, "Open File", errorCode)
                            .ConfigureAwait(false);
                    }
                }
                else
                {
                    // # 보안 적용 되지 않은 문서

                    logger.Debug(" - Not protected document.");

                    // 화면 캡처 방지 해제 (보호 적용된 도면이 아닌 경우)
                    DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, false);

                    // 보호된 도면이 아닌 경우 메뉴 활성화
                    this.applicationController.setApplicationMenuByLabels(null);
                }
            }
        }

        /// <summary>
        /// 선택한 레이블의 Adhoc Protection(사용자 정의 권한 보호) 여부 확인
        /// </summary>
        /// <param name="protectedDocument"></param>
        /// <returns></returns>
        public bool hasAdhocProtectionInLabel(String labelId)
        {
            bool hasAdhocProtection = false;

            if (this.policyEngine == null)
            {
                logger.Error(" - hasAdhocProtectionInLabel : policyEngine is null");

                return hasAdhocProtection;
            }

            if (String.IsNullOrEmpty(labelId))
            {
                logger.Error(" - hasAdhocProtectionInLabel : labelId is null");

                return hasAdhocProtection;
            }

            // 레이블에 대한 실행 상태 객체를 생성한다. (CommonExecutionState 는 ExecutionState의 구현체)

            CommonExecutionState executionState = new CommonExecutionState(this.policyEngine.GetLabelById(labelId));

            // this.policyEngine > policyHandler > ComputeActions => Action 목록

            using (IPolicyHandler policyHandler = this.policyEngine.CreatePolicyHandler(true))
            {
                ReadOnlyCollection<Action> actionList = policyHandler.ComputeActions(executionState);

                foreach (Action action in actionList!)
                {
                    // Action 목록 내 ActionType이 'ProtectAdhoc' 인 action 이 존재하면 AdhocProtection 을 포함하는 레이블임. 

                    // logger.Debug($"action.ActionType : '{action.ActionType}'");

                    if (action != null && action.ActionType.ToString().Equals("ProtectAdhoc"))
                    {
                        // logger.Debug($"action.ActionType is '{action.ActionType}'");

                        hasAdhocProtection = true;

                        break;
                    }
                }
            }

            return hasAdhocProtection;
        }

        /// <summary>
        /// 파일 보호 및 레이블(권한, 대상 사용자)적용
        /// </summary>
        /// <param name="fileOptions"></param>
        /// <param name="users"></param>
        /// <param name="rights"></param>
        /// <returns></returns>
        public async Task<JObject> applyProtectionToFile(DocumentListItem item, List<String> users = null, List<String> rights = null, DateTime? expireDateTime = null)
        {
            JObject result = new JObject();

            if (item == null)
            {
                return result;
            }

            ProtectedDocument protectedDocument = item.source;

            Document cadDocument = PluginApplication.documentHandler.searchDocument(item.source);

            try
            {
                if (cadDocument != null && protectedDocument != null)
                {
                    if (protectedDocument.isProtected == true)
                    {
                        // 1. 사용자 도면 저장 (AutoCAD QSAVE 명령 호출)
                        cadDocument.SendStringToExecute("._QSAVE\n", true, false, false);

                        // 2. 기존 보호 정보에 저장 권한이 있을 경우 원본 파일의 보호 정보를 임시 파일에 적용 (기존 보호 정보 백업)
                        await this.applyProtectionToTempFile(protectedDocument);

                        // 3. 원본 파일에 레이블링(레이블, 대상 사용자, 권한 적용)을 적용하고 적용된 임시 파일을 원본 파일로 대체 (핵심)
                        result = await this.applyUserRightAndLabelToFile(protectedDocument, true, users, rights, expireDateTime);

                        //  권한, 권한 부여자, 레이블정보, 파일을 복호화 후 복호화된 임시파일 경로가 포함된 문서 보호 정보 생성
                        ProtectedDocument newProtectedDocument = await this.patchProtectionData(protectedDocument, false);

                        if (newProtectedDocument?.message != String.Empty)
                        {
                            // 권한 처리 메세지가 없는 경우 메세지창을 표시하지 않음.
                            logger.Debug($"newProtectionDocument.message : {newProtectedDocument?.message}");

                            OnRaiseError?.Invoke(this, new MessageEventArgs(newProtectedDocument?.message, "Error"));   // 권한 처리 메세지 이벤트 호출
                        }

                        logger.Debug($" - newProtectedDocument : {newProtectedDocument?.ToString()}");

                        // 열린 문서 목록에 추가하고 Selected 처리

                        PluginApplication.documentHandler.replaceOpenDocument(newProtectedDocument);
                    }
                    else
                    {
                        // 보호 되지 않은 문서에 대한 레이블 적용

                        // 1. 실제 AutoCAD 문서를 찾아 닫기
                        cadDocument.CloseAndSave(protectedDocument.filePath);

                        // 2. 파일에 레이블을 적용하기 위한 옵션 정보 생성 후 FileName 에 해당하는 CAD 파일에 레이블링(레이블, 대상 사용자, 권한 적용)을 적용하고 적용된 임시 파일을 원본 파일로 대체 (핵심)
                        result = await this.applyUserRightAndLabelToFile(protectedDocument, true, users, rights, expireDateTime);

                        // 3. 파일에 보호 적용한 후 파일 열기
                        await this.openDocumentFile(protectedDocument.filePath, item.id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(" - applyFileProtection Exception : ", ex);

                result = new JObject();

                result.Add("errorCode", "-9");
                result.Add("errorMessage", PluginApplication.global.getMessage("file.in.use"));            // 해당 도면이 사용되고 있습니다.
                result.Add("resultMessage", PluginApplication.global.getMessage("file.in.use.delayed"));   // 해당 도면에 대한 리소스 해제가 지연되고 있습니다. 다시 시도해 주세요.
            }

            return result;
        }

        /// <summary>
        /// [MIP] 파일 옵션에 있는 파일에 레이블, 대상 사용자, 권한 적용. 선택적으로 사용자 권한 적용 (isReplace : true (덮어쓰기))
        /// </summary>
        /// <param name="fileOptions">파일 옵션(대상 파일, 적용할 레이블 포함)</param>
        /// <param name="isReplace">레이블링 된 파일을 원본파일 rename 할지 여부</param>
        /// <param name="users">적용 대상 사용자 아이디 목록(선택)</param>
        /// <param name="rights">적용할 권한 목록(선택)</param>
        private async Task<JObject> applyUserRightAndLabelToFile(ProtectedDocument protectedDocument, bool isReplace = true, List<String> users = null, List<String> rights = null, DateTime? expireDateTime = null)
        {
            JObject resultLabeling = new JObject();

            int errorCode = 0;

            String errorMessage = "";

            if (this.fileEngine == null)
            {
                logger.Error(" - applyUserRightAndLabelToFile : fileEngine is null");
                return null;
            }

            if (protectedDocument == null)
            {
                errorCode = -1001;
                errorMessage = PluginApplication.global.getMessage("document.info.not.set");       // 도면 보호 정보가 없습니다.

                logger.Error($" - applyUserRightAndLabelToFile : {errorMessage} [{errorCode}]");

                resultLabeling.Add("errorCode", errorCode);
                resultLabeling.Add("errorMessage", errorMessage);
                resultLabeling.Add("resultMessage", errorMessage);

                return resultLabeling;
            }

            if (String.IsNullOrEmpty(protectedDocument.filePath) || String.IsNullOrEmpty(protectedDocument.labelId))
            {
                errorCode = -1000;
                errorMessage = PluginApplication.global.getMessage("file.label.not.set");      // 파일 정보가 없거나 레이블 ID가 비어 있습니다.

                logger.Error($" - applyUserRightAndLabelToFile : {errorMessage} [{errorCode}]");

                resultLabeling.Add("errorCode", errorCode);
                resultLabeling.Add("errorMessage", errorMessage);
                resultLabeling.Add("resultMessage", errorMessage);

                return resultLabeling;
            }

            // 파일 옵션 세팅
            FileOptions fileOptions = new FileOptions
            {
                FileName = protectedDocument.filePath,
                OutputFileName = FileManager.getOutputFilePath(protectedDocument.filePath),
                LabelId = protectedDocument.labelId,
                IsAuditDiscoveryEnabled = true,
                EnableDocTracking = true,
                NotifyOwnerOnOpen = false
            };

            // 파일 옵션의 원본 파일경로를 이용해 파일 핸들러를 생성
            // (두 번째 inputFilePath는 관리자 감사를 위해 사람이 읽을 수 있는 콘텐츠 식별자를 제공하는 데 사용).

            IFileHandler fileHandler = await this.createFileHandler(fileOptions);

            String inputFilePath = fileOptions.FileName;
            String outputFilePath = fileOptions.OutputFileName;

            LabelingOptions labelingOptions = new LabelingOptions()
            {
                AssignmentMethod = AssignmentMethod.Standard
            };

            try
            {
                // 원본 파일 경로와 임시파일 경로 (안쓰임)
                String actualFilePath = inputFilePath;
                String actualOutputFilePath = outputFilePath;

                if (users != null && rights != null)
                {
                    // 적용 대상 사용자 목록과 권한 목록이 존재 할 경우에만 암호화 처리

                    // 사용자 정의 레이블 정보 목록을 생성
                    List<UserRights> userRightsList = this.getUserRights(users, rights);

                    // 사용자 정의 레이블 정보 목록으로 ProtectionDescriptor 생성
                    ProtectionDescriptor protectionDescriptor = new ProtectionDescriptor(userRightsList);

                    // 만료 일시가 존재 하는 경우 만료 일시 추가
                    if (expireDateTime != null)
                    {
                        try
                        {
                            protectionDescriptor.ContentValidUntil = expireDateTime;

                            logger.Debug($"Protection expiration date set to: {expireDateTime}");
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Invalid expireDateTime format: {expireDateTime}", ex);
                        }
                    }

                    // 생성된 ProtectionDescriptor로 보호 정보를 세팅 
                    fileHandler.SetProtection(protectionDescriptor, new ProtectionSettings());
                }

                IFileStatus fileStatus = FileHandler.GetFileStatus(fileOptions.FileName, this.mipContext, null);

                // 파일 옵션에 있는 선택된 레이블 아이디로 레이블 세팅
                fileHandler.SetLabel(this.fileEngine.GetLabelById(fileOptions.LabelId), labelingOptions, new ProtectionSettings());

                // 변경된 내용을 임시 파일에 적용
                bool result = await fileHandler.CommitAsync(outputFilePath);

                // 임시 파일에 해당하는 파일 보호 핸들러 생성
                IFileHandler fileHandlerModified = await this.fileEngine.CreateFileHandlerAsync(outputFilePath, actualOutputFilePath, true);

                // 임시 파일의 레이블 정보 조회 
                ContentLabel contentLabel = fileHandlerModified.Label;

                logger.Debug(String.Format("Getting the label committed to file: {0}", outputFilePath));
                logger.Debug(String.Format("File Label: {0} \r\nisProtected: {1}", contentLabel.Label.Name, contentLabel.IsProtectionAppliedFromLabel.ToString()));

                logger.Debug($"파일에 레이블 '{contentLabel.Label.Name}'이(가) 성공적으로 설정되었습니다.");

                // 원본 파일명을 임시 파일명으로 세팅
                fileOptions.FileName = fileOptions.OutputFileName;

                // 보호화 적용된 파일에 대한 추적 실시(또는 추적 안함) 
                await this.enableDocTracking(fileOptions);
            }
            catch (JustificationRequiredException jre)
            {
                logger.Debug($" - JustificationRequiredException : ${jre.Message}");

                /// !!!!! 파일이 보호된 상태에서만 레이블 적용 처리

                // 정당성 사유 입력 창을 팝업하고 입력 받은 메세지를 세팅
                String justificationMessage = PluginApplication.pluginFormManager.setJustificationMessageForm();

                if (String.IsNullOrEmpty(justificationMessage))
                {
                    // 정당성 사유가 입력되지 않은 경우 레이블링 처리 중단
                    errorCode = -1009;
                    errorMessage = PluginApplication.global.getMessage("cancel.label.not.justification");      // 정당성 사유가 입력되지 않아 레이블 적용이 취소되었습니다.

                    logger.Error($" - setFileUserRightAndLabels : Justification message is empty [{errorCode}]");

                    return resultLabeling;
                }

                logger.Debug("   * justificationMessage : " + justificationMessage);

                // 라벨 옵션 세팅
                labelingOptions.IsDowngradeJustified = true;
                labelingOptions.JustificationMessage = justificationMessage;

                // 현재 파일에 파일 옵션의 라벨아이디 세팅 (세팅된 LabelOptions 포함)
                fileHandler.SetLabel(this.fileEngine?.GetLabelById(fileOptions.LabelId), labelingOptions, new ProtectionSettings());

                // 임시 파일에 반영
                bool downgradedResult = await fileHandler.CommitAsync(outputFilePath);

                // 임시 파일에 해당하는 파일 보호 핸들러 생성 (생략)
                // IFileHandler commitHandler = await fileEngine.CreateFileHandlerAsync(outputFilePath, outputFilePath, true);

                // 원본 파일명을 임시 파일명으로 대체
                fileOptions.FileName = fileOptions.OutputFileName;

                // 보호화 적용된 파일에 대한 추적 실시(또는 추적 안함)
                await this.enableDocTracking(fileOptions);
            }
            catch (AdhocProtectionRequiredException apre)
            {
                errorCode = -1002;
                errorMessage = "Adhoc Protection이 필요합니다. 레이블을 적용할 수 없습니다.";
                logger.Error($" - applyUserRightAndLabelToFile : AdhocProtectionRequiredException [{errorCode}]", apre);
            }
            catch (NetworkException ne)
            {
                errorCode = -1003;
                errorMessage = "네트워크 연결에 문제가 발생했습니다.";
                logger.Error($" - applyUserRightAndLabelToFile : NetworkException [{errorCode}]", ne);
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    if (ex.InnerException is UnauthorizedAccessException)
                    {
                        errorCode = -1004;
                        errorMessage = PluginApplication.global.getMessage("error.unauthorized.access");      // 파일에 대한 인증된 접근이 아닙니다.
                        logger.Error($" - applyUserRightAndLabelToFile : UnauthorizedAccessException [{errorCode}]", ex);
                    }
                    else if (ex.InnerException is NoPermissionsException)
                    {
                        errorCode = -1005;
                        errorMessage = PluginApplication.global.getMessage("error.no.permissions");      // 파일에 대한 권한이 없습니다.
                        logger.Error($" - applyUserRightAndLabelToFile : NoPermissionsException [{errorCode}]", ex);
                    }
                    else if (ex.InnerException is AccessDeniedException)
                    {
                        errorCode = -1006;
                        errorMessage = PluginApplication.global.getMessage("error.file.access.denied");      // 파일에 대한 액세스가 거부되었습니다.
                        logger.Error($" - applyUserRightAndLabelToFile : AccessDeniedException [{errorCode}]", ex);
                    }
                    else
                    {
                        errorCode = -1007;
                        errorMessage = PluginApplication.global.getMessage("error.unexpected");      // 예상치 못한 오류가 발생했습니다.
                        logger.Error($" - applyUserRightAndLabelToFile : Unexpected error [{errorCode}]", ex);
                    }
                }
                else
                {
                    errorCode = -1008;
                    errorMessage = PluginApplication.global.getMessage("error.unexpected");      // 예상치 못한 오류가 발생했습니다.
                    logger.Error($" - applyUserRightAndLabelToFile : An unexpected error occurred [{errorCode}]", ex);
                }

            }
            finally
            {
                // 라벨, 대상자, 권한 반영 결과 생성

                fileHandler = null;

                // 에러 코드 및 메세지 추가
                resultLabeling.Add("errorCode", errorCode);
                resultLabeling.Add("errorMessage", errorMessage);

                if (isReplace && errorCode == 0)
                {
                    // 성공(0) : 라벨, 대상자, 권한 반영이 정상 처리되고 대체일 경우  

                    try
                    {
                        if (File.Exists(inputFilePath) && File.Exists(outputFilePath))
                        {
                            // 원본,  임시파일 모두 존재 할 경우 원본 파일을 임시 파일로 대체 한다. 

                            File.Delete(inputFilePath);
                            File.Move(outputFilePath, inputFilePath);

                            logger.Debug($" - {outputFilePath} to {inputFilePath} move.");

                            Thread.Sleep(200);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($" - applyUserRightAndLabelToFile : Error replacing original file:", ex);
                    }

                    resultLabeling.Add("resultMessage", PluginApplication.global.getMessage("success.apply.label"));
                }
                else
                {
                    resultLabeling.Add("resultMessage", PluginApplication.global.getMessage("error.apply.label")); // 레이블 적용 중 오류가 발생했습니다.
                }
            }

            return resultLabeling;
        }

        /// <summary>
        /// MIP 파일 보호 핸들러로 부터 파일에 부여된 
        /// 권한, 권한 부여자, 레이블정보, 파일을 복호화 처리 후 복호화된 임시파일 정보를 구하여 
        /// protectedDocument(파일 보호 정보)에 세팅 후 리턴
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>resultProtectedDocument</returns>
        private async Task<ProtectedDocument> patchProtectionData(ProtectedDocument protectedDocument, bool isCreateTemporaryFile = true)
        {
            if (protectedDocument == null)
            {
                return null;
            }

            ProtectedDocument resultProtectedDocument = await this.patchProtectionData(protectedDocument.filePath, isCreateTemporaryFile);

            resultProtectedDocument.handle = protectedDocument.handle;
            resultProtectedDocument.hashCode = protectedDocument.hashCode;
            resultProtectedDocument.isReadOnly = protectedDocument.isReadOnly;
            resultProtectedDocument.decryptedTemporaryFilePath = protectedDocument.decryptedTemporaryFilePath;

            return resultProtectedDocument;
        }

        /// <summary>
        /// MIP 파일 보호 핸들러로 부터 파일에 부여된 
        /// 권한, 권한 부여자, 레이블정보, 파일을 복호화 처리 후 복호화된 임시파일 정보를 구하여 
        /// ProtectedDocument(문서 보호 정보)에 세팅 후 리턴
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>protectedDocument</returns>
        private async Task<ProtectedDocument> patchProtectionData(String fileName, bool isCreateTemporaryFile = true)
        {
            // 문서 Protection 정보 생성

            ProtectedDocument protectedDocument = new ProtectedDocument(fileName);

            try
            {
                if (protectedDocument.isNewFile)
                {
                    // 파일 경로에 파일이 존재 하지 않는 경우 

                    return protectedDocument;
                }

                // 파일이 존재하는 경우 파일 옵션 생성

                FileOptions options = new FileOptions
                {
                    FileName = fileName,
                    OutputFileName = fileName,
                    IsAuditDiscoveryEnabled = true
                };

                // 파일 옵션의 원본 파일경로에 해당하는 MIP에서 MIP 파일 보호 핸들러를 생성

                IFileHandler fileHandler = await this.createFileHandler(options);

                try
                {
                    if (fileHandler.Protection != null)
                    {
                        // 파일 보호 정보가 존재하는 경우 

                        protectedDocument.isProtected = true;                             // 보호 상태값을 true로 세팅

                        // protectedDocument.FileHandler = handler;                       // MIP 파일 핸들러 세팅   

                        if (fileHandler.Protection.AccessCheck(Rights.Owner) && !PluginApplication.userLicenseData.isOnlyView)
                        {
                            // 파일 권한 정보 부여자 인 경우 
                            protectedDocument.isOwner = true;
                        }
                        if (fileHandler.Protection.AccessCheck(Rights.View))
                        {
                            // 조회 권한인 경우 
                            protectedDocument.isView = true;
                        }
                        if (fileHandler.Protection.AccessCheck(Rights.Print) && !PluginApplication.userLicenseData.isOnlyView)
                        {
                            // 출력 권한인 경우 
                            protectedDocument.isPrint = true;
                        }
                        if (fileHandler.Protection.AccessCheck(Rights.Edit) && !PluginApplication.userLicenseData.isOnlyView)
                        {
                            // 저장 권한인 경우 
                            protectedDocument.isEdit = true;
                        }
                        if (fileHandler.Protection.AccessCheck(Rights.Export) && !PluginApplication.userLicenseData.isOnlyView)
                        {
                            // 복사 권한인 경우
                            protectedDocument.isExport = true;
                        }
                        if (fileHandler.Protection.AccessCheck(Rights.Extract) && !PluginApplication.userLicenseData.isOnlyView)
                        {
                            // 다른 이름으로 저장인 경우
                            protectedDocument.isExtract = true;
                        }

                        // MIP 파일 보호 핸들러의 컨텐츠 아이디, 권한 부여자, 레이블명, 레이블 아이디 세팅

                        protectedDocument.contentId = fileHandler.Protection.ContentId;
                        protectedDocument.protectionOwner = fileHandler.Protection.Owner;
                        protectedDocument.labelName = fileHandler.Protection.ProtectionDescriptor.Name;
                        protectedDocument.labelId = fileHandler.Protection.ProtectionDescriptor.LabelId;

                        //  만료 일자 추가
                        DateTime? expirationDate = fileHandler.Protection.ProtectionDescriptor.ContentValidUntil;

                        logger.Debug($"Protection expiration datetime :  {expirationDate}");

                        if (expirationDate.HasValue)
                        {
                            DateTime localExpirationDate;

                            if (expirationDate.Value.Kind == DateTimeKind.Utc)
                            {
                                // UTC를 로컬 시간으로 변환
                                localExpirationDate = expirationDate.Value.ToLocalTime();
                            }
                            else
                            {
                                // 이미 로컬 시간인 경우 그대로 사용
                                localExpirationDate = DateTime.SpecifyKind(expirationDate.Value, DateTimeKind.Utc).ToLocalTime();
                            }

                            protectedDocument.expireDateTime = localExpirationDate.ToString("yyyyMMddHHmmss");

                            logger.Debug($"Protection expiration datetime String : {protectedDocument.expireDateTime}");
                        }

                        if (isCreateTemporaryFile)
                        {
                            FileManager.setUserMipTempWritePermissions(true);

                            // 임시파일 생성 후 임시 파일 복사 (생성 된 임시파일은 IFileHandler가 소멸 되면 같이 삭제 되므로 임시파일을 백업) 

                            String tempPath = await fileHandler.GetDecryptedTemporaryFileAsync();

                            String targetPath = Path.Combine(CommonConstants.PLUGIN_MIP_TEMP_PATH, $"_{Path.GetFileNameWithoutExtension(tempPath)}.dwg");

                            File.Copy(tempPath, targetPath, true);

                            // 복사된 파일을 숨김(Hidden) 속성으로 설정
                            try
                            {
                                FileAttributes attrs = File.GetAttributes(targetPath);

                                // 이미 Hidden 이 아니면 Hidden 플래그 추가
                                if ((attrs & FileAttributes.Hidden) == 0 && CommonConstants.IS_FILE_ACL)
                                {
                                    File.SetAttributes(targetPath, attrs | FileAttributes.Hidden);
                                }
                            }
                            catch (Exception ex)
                            {
                                // 숨김 속성 설정 실패는 치명적이지 않으므로 로그만 남김
                                logger.Error($"Failed to set Hidden attribute on temp file: {targetPath}", ex);
                            }

                            FileManager.setUserMipTempWritePermissions(false);

                            // 임시 파일 경로 세팅 

                            protectedDocument.decryptedTemporaryFilePath = Path.GetFullPath(targetPath);
                        }

                        // 소유자인 경우 부여한 권한 및 대상 사용자 정보 조회

                        List<UserRights> userRightList = fileHandler.Protection.ProtectionDescriptor.UserRights;

                        foreach (UserRights userRight in userRightList)
                        {
                            protectedDocument.appliedUserList = userRight.Users.ToList();
                            protectedDocument.appliedRightList = userRight.Rights.ToList();
                        }
                    }
                    else
                    {
                        // 파일 보호 정보가 존재하지 않는 경우 초기화 

                        protectedDocument.isProtected = false;
                        protectedDocument.isOwner = false;
                        protectedDocument.isView = false;
                        protectedDocument.isPrint = false;
                        protectedDocument.isEdit = false;
                        protectedDocument.isExport = false;
                        protectedDocument.isExtract = false;
                        protectedDocument.labelName = String.Empty;
                        protectedDocument.labelId = String.Empty;
                        protectedDocument.protectionOwner = String.Empty;
                        protectedDocument.expireDateTime = String.Empty;
                    }
                }
                finally
                {
                    // MIP 파일 보호 핸들러 릴리즈
                    fileHandler.Dispose();
                }
            }
            catch (NetworkException ex)
            {
                protectedDocument.message = PluginApplication.global.getMessage("error.network.connection"); // 네트워크 연결에 문제가 발생했습니다.

                logger.Error($" - patchProtectionData", ex);
            }
            catch (NoPermissionsException ex)
            {
                protectedDocument.message = PluginApplication.global.getMessage("error.no.permissions"); // 해당 문서에 대한 접근 권한이 없습니다.

                logger.Error($" - patchProtectionData", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                protectedDocument.message = PluginApplication.global.getMessage("error.deny.security"); // 보안 오류로 인해 접속 인증을 할 수 없습니다.

                logger.Error($"  - patchProtectionData", ex);
            }
            catch (AccessDeniedException ex)
            {
                protectedDocument.message = "MIP 서비스에 접근이 거부되었습니다.";

                logger.Debug($" - patchProtectionData", ex);
            }
            catch (Exception ex)
            {
                logger.Error($" - patchProtectionData", ex);

                if (ex.InnerException != null)
                {
                    if (ex.InnerException is NetworkException)
                    {
                        protectedDocument.message = PluginApplication.global.getMessage("error.network.connection"); // 네트워크 연결에 문제가 발생했습니다.

                        logger.Error($" - patchProtectionData Inner Exception", ex.InnerException);
                    }
                    if (ex.InnerException is NoPermissionsException)
                    {
                        protectedDocument.message = PluginApplication.global.getMessage("error.no.permissions"); // 해당 문서에 대한 접근 권한이 없습니다.

                        logger.Error($" - patchProtectionData Inner Exception", ex.InnerException);
                    }
                    else if (ex.InnerException is UnauthorizedAccessException)
                    {
                        protectedDocument.message = PluginApplication.global.getMessage("error.deny.security"); // 보안 오류로 인해 접속 인증을 할 수 없습니다.

                        logger.Error($" - patchProtectionData Inner Exception", ex.InnerException);
                    }
                    else if (ex.InnerException is AccessDeniedException)
                    {
                        protectedDocument.message = PluginApplication.global.getMessage("error.deny.mip"); // MIP 서비스에 접근이 거부되었습니다.

                        logger.Error($" - patchProtectionData Inner Exception", ex.InnerException);
                    }
                    else
                    {
                        protectedDocument.message = PluginApplication.global.getMessage("error.mip.auth.unexpected"); // MIP 권한 확인 중 예상치 못한 오류가 발생했습니다.
                        logger.Error($" - patchProtectionData Inner Exception", ex.InnerException);
                    }
                }
                else
                {
                    protectedDocument.message = PluginApplication.global.getMessage("error.mip.auth.unexpected"); // MIP 권한 확인 중 예상치 못한 오류가 발생했습니다.
                    logger.Error($" - patchProtectionData Inner Exception", ex.InnerException);
                }
            }

            return protectedDocument;
        }

        private async Task<String> GetDecryptedTemporaryFile(String fileName)
        {
            if (fileEngine == null)
            {
                return "";
            }

            //Create a fileHandler for consumption for the Protected File.
            IFileHandler protectedFileHandler = await fileEngine.CreateFileHandlerAsync(fileName,// inputFilePath
                                                            fileName,// actualFilePath
                                                            false, //isAuditDiscoveryEnabled
                                                            null); // fileExecutionState

            // Decrypt the file and get the temporary decrypted file path
            String tempPath = await protectedFileHandler.GetDecryptedTemporaryFileAsync();
            String DecryptedTemporaryFile = Path.GetFullPath(tempPath);

            return DecryptedTemporaryFile;
        }

        /// <summary>
        /// [MIP] 저장 권한이 있을 경우 원본 파일의 보호 정보를 임시파일에 적용
        /// </summary>
        /// <param name="protectedDocument">문서 보호 정보</param>
        /// <returns></returns>
        public async Task<bool> applyProtectionToTempFile(ProtectedDocument protectedDocument)
        {
            bool result = false;

            if (protectedDocument == null)
            {
                throw new ArgumentNullException(nameof(protectedDocument));
            }

            // 문서 보안 정보에 저장된 파일명
            String protectedFilePath = protectedDocument.filePath;

            CloseFlowDiagnostics.LogPhaseByPath(protectedDocument.decryptedTemporaryFilePath,
                CloseFlowDiagnostics.ClosePhase.ApplyProtectionBegin,
                $"filePath='{Path.GetFileName(protectedFilePath)}'");

            if (!String.IsNullOrWhiteSpace(protectedDocument.decryptedTemporaryFilePath))
            {
                VfsInterceptor.PrepareCloseCommit(protectedDocument.decryptedTemporaryFilePath);
            }

            try
            {
                if (!File.Exists(protectedFilePath))
                {
                    throw new Exception($" - applyProtectionToTempFile : File [{protectedFilePath}] is Not Found");
                }
                else if (this.fileEngine == null)
                {
                    throw new Exception($" - applyProtectionToTempFile : FileEngine is Null");
                }

                /* 보호된 파일을 처리하기 파일보호 핸들러 생성 
                    - inputFile Path : protectedFilePath (보호할 파일 경로)
                    - actualFile Path : protectedFilePath (감사 추적을 위한 사람이 읽을 수 있는 콘텐츠 식별자)
                    - isAuditDiscoveryEnabled : false (감사 추적 비활성화)
                    - fileExecutionState : null
                */
                IFileHandler protectedFileHandler = await fileEngine.CreateFileHandlerAsync(protectedFilePath, protectedFilePath, false, null);

                // 저장 권한이 있는 경우 원본 파일에 적용된 보호 정보를 임시파일에 적용

                IProtectionHandler protectionHandler = protectedFileHandler.Protection;

                if (protectionHandler != null)
                {
                    if (protectionHandler.AccessCheck("Edit"))
                    {
                        // 보호 정보가 존재하고 저장 권한이 있는 경우 임시 파일에 원래 보호정보를 적용 

                        // 임시파일 경로 조회 
                        String tempFilePath = protectedDocument.decryptedTemporaryFilePath;

                        // 임시 파일의 파일 보호 핸들러 생성
                        IFileHandler tempFileHandler = await fileEngine.CreateFileHandlerAsync(tempFilePath, tempFilePath, false);

                        // 임시 파일 보호 핸들러에 원래 파일 보호 정보를 세팅.
                        tempFileHandler.SetProtection(protectionHandler);

                        // 임시 파일 보호 핸들러에 적용된 정보를 Commit
                        result = await tempFileHandler.CommitAsync(protectedFilePath);
                        CloseFlowDiagnostics.LogPhaseByPath(protectedDocument.decryptedTemporaryFilePath,
                            CloseFlowDiagnostics.ClosePhase.ApplyProtectionCommitDone, $"result={result}");
                    }

                    if (!String.IsNullOrWhiteSpace(protectedDocument.decryptedTemporaryFilePath))
                    {
                        CloseFlowDiagnostics.LogPhaseByPath(protectedDocument.decryptedTemporaryFilePath,
                            CloseFlowDiagnostics.ClosePhase.ApplyProtectionDeleteBegin, null);
                        // 기존 임시 파일 삭제
                        FileManager.deleteFilesByName(protectedDocument.decryptedTemporaryFilePath);
                        CloseFlowDiagnostics.LogPhaseByPath(protectedDocument.decryptedTemporaryFilePath,
                            CloseFlowDiagnostics.ClosePhase.ApplyProtectionDeleteEnd, null);
                    }
                }
                else
                {
                    logger.Debug($" - applyProtectionAtTempFile : 원본 파일이 레이블링이 적용된 파일이 아닙니다.");
                }

                CloseFlowDiagnostics.LogPhaseByPath(protectedDocument.decryptedTemporaryFilePath,
                    CloseFlowDiagnostics.ClosePhase.ApplyProtectionEnd, $"success={result}");
            }
            catch (Exception ex)
            {
                result = false;
                CloseFlowDiagnostics.LogPhaseByPath(protectedDocument.decryptedTemporaryFilePath,
                    CloseFlowDiagnostics.ClosePhase.ApplyProtectionFailed, ex.Message);
                logger.Error($"  * applyProtectionToTempFile", ex);
            }

            return result;
        }

        /// <summary>
        /// MIP 리소스 릴리즈
        /// </summary>
        public void release()
        {
            if (this.fileEngine != null)
            {
                this.fileEngine.Dispose();
                this.fileEngine = null;
            }

            if (this.fileProfile != null)
            {
                this.fileProfile.Dispose();
                this.fileProfile = null;
            }

            if (this.mipContext != null)
            {
                this.mipContext.Dispose();
            }
        }

        /// <summary>
        /// [MIP] FileOptions의 원본 파일 경로로 보호 파일 핸들러를 생성하고 보호된 파일을 추적.
        /// 파일이 보호되지 않으면 예외가 발생합니다.
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        private async Task<bool> enableDocTracking(FileOptions options)
        {
            IFileHandler handler = await this.createFileHandler(options);

            if (options.EnableDocTracking && handler.Protection != null)
            {
                // 보호 적용된 문서 일 경우에 추적

                try
                {
                    await handler.RegisterContentForTrackingAndRevocationAsync(options.NotifyOwnerOnOpen);

                    return true;
                }
                catch (NetworkException e)
                {
                    logger.Error(" - enableDocTracking NetworkException", e);

                    return false;
                }
                catch (Exception e)
                {
                    logger.Error(" - enableDocTracking Exception", e);

                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// [MIP] MIP에서 파일 옵션의 원본 파일경로를 이용해 MIP 파일 핸들러를 생성
        /// (IFileHandler는 파일 API의 모든 레이블 지정 및 보호 작업을 구현)
        /// </summary>
        /// <param name="options">Struct provided to set various options for the handler.</param>
        /// <returns></returns>
        private async Task<IFileHandler> createFileHandler(FileOptions options)
        {
            if (this.fileEngine == null)
            {
                throw new Exception("FileEngine is not initialized.");
            }

            // Create the handler using options from FileOptions. Assumes that the engine was previously created and stored in private engine object.
            // There's probably a better way to pass/store the engine, but this is a sample ;)

            IFileHandler handler = await this.fileEngine.CreateFileHandlerAsync(options.FileName, options.FileName, options.IsAuditDiscoveryEnabled);

            return handler;
        }

        /// <summary>
        /// 사용자 정의 레이블 정보 목록을 생성. 
        /// 적용 대상 사용자 아이디 목록과 권한 목록으로 생성한 사용자 정의 레이블 정보를 추가
        /// </summary>
        /// <param name="users">적용 대상 사용자 아이디 목록</param>
        /// <param name="rights">적용할 권한 목록</param>
        /// <returns>사용자 정의 레이블 정보 목록</returns>
        private List<UserRights> getUserRights(List<String> users, List<String> rights)
        {
            // 부여된 권한 및 대상사용자 목록 생성
            UserRights userRights = new UserRights(users, rights);

            // 사용자 권한 목록에 추가
            List<UserRights> userRightsList = new List<UserRights>()
            {
                userRights
            };

            return userRightsList;
        }
    }
}