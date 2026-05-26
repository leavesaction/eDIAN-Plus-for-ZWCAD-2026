using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.UI;
using log4net;
using Microsoft.Identity.Client;
using Microsoft.InformationProtection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace eDIAN.Main.Protect
{
    /// <summary>
    /// MIP <see cref="IAuthDelegate"/> — contract is synchronous <see cref="AcquireToken"/> only (SDK 1.18.x).
    /// MSAL is async; sync entry points use <see cref="RunMipSync{T}"/> to avoid blocking the UI thread on .Result.
    /// UPN/identity for engines is set in <see cref="ProtectionController"/> (not here).
    /// </summary>
    internal class AuthDelegate : IAuthDelegate
    {
        private static readonly ILog logger = PluginLogger.getLogger("AuthDelegate", "application.log");

        private readonly ApplicationInfo applicationInfo;
        private IPublicClientApplication publicClientApplication;

        private static readonly bool isMultitenantApp = false;

        public AuthDelegate(ApplicationInfo applicationInfo, IPublicClientApplication clientApplication)
        {
            this.applicationInfo = applicationInfo;
            this.publicClientApplication = clientApplication;
        }

        /// <inheritdoc />
        public string AcquireToken(Identity identity, string authority, string resource, string claims)
        {
            _ = identity;
            try
            {
                return RunMipSync(async () =>
                {
                    AuthenticationResult result = await AcquireTokenAsync(authority, resource, claims, isMultitenantApp)
                        .ConfigureAwait(false);
                    return result.AccessToken;
                });
            }
            catch (Exception ex)
            {
                logger.Error($"AcquireToken failed (authority={authority}, resource={resource})", ex);
                throw;
            }
        }

        /// <summary>
        /// MSAL token acquisition (shared with MIP callback path). Prefer awaiting from app code; MIP uses <see cref="AcquireToken"/>.
        /// </summary>
        public async Task<AuthenticationResult> AcquireTokenAsync(string authority, string resource, string claims, bool isMultiTenantApp = true)
        {
            if (this.publicClientApplication == null)
            {
                if (isMultiTenantApp)
                {
                    this.publicClientApplication = PublicClientApplicationBuilder.Create(this.applicationInfo.ApplicationId)
                        .WithAuthority(authority)
                        .WithDefaultRedirectUri()
                        .Build();
                }
                else
                {
                    if (authority.Contains("common", StringComparison.OrdinalIgnoreCase))
                    {
                        Uri authorityUri = new Uri(authority);
                        authority = string.Format("https://{0}/{1}", authorityUri.Host, ServiceConstants.TENANT_ID);
                    }

                    this.publicClientApplication = PublicClientApplicationBuilder.Create(this.applicationInfo.ApplicationId)
                        .WithAuthority(authority)
                        .WithDefaultRedirectUri()
                        .Build();
                }
            }

            IEnumerable<IAccount> accounts = await this.publicClientApplication.GetAccountsAsync().ConfigureAwait(false);

            string[] scopes = new[] { resource[resource.Length - 1].Equals('/') ? $"{resource}.default" : $"{resource}/.default" };

            IAccount account = accounts.FirstOrDefault();

            try
            {
                return await this.publicClientApplication.AcquireTokenSilent(scopes, account).ExecuteAsync().ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                AcquireTokenInteractiveParameterBuilder builder = this.publicClientApplication.AcquireTokenInteractive(scopes)
                    .WithParentActivityOrWindow(CommonConstants.CAD_MAIN_WINDOW_HANDLE)
                    .WithPrompt(Prompt.SelectAccount);

                if (account != null)
                {
                    builder = builder.WithAccount(account);
                }

                return await builder.ExecuteAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs async MSAL work off the caller thread, then blocks until complete.
        /// Unwraps <see cref="AggregateException"/> so MIP sees the real MSAL error (per IAuthDelegate remarks).
        /// </summary>
        private static T RunMipSync<T>(Func<Task<T>> asyncFunc)
        {
            try
            {
                return Task.Run(asyncFunc).GetAwaiter().GetResult();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                throw ex.InnerExceptions[0];
            }
        }
    }
}
