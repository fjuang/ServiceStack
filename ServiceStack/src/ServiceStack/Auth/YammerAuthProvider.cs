﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ServiceStack.Configuration;
using ServiceStack.Text;

namespace ServiceStack.Auth
{
    /// <summary>
    /// The ServiceStack Yammer OAuth provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This provider is loosely based on the existing ServiceStack's Facebook OAuth provider.
    /// </para>
    /// <para>
    /// For the full info on Yammer's OAuth2 authentication flow, refer to:
    /// https://developer.yammer.com/authentication/#a-oauth2
    /// </para>
    /// <para>
    /// Note: Add these to your application / web config settings under appSettings and replace
    /// values as appropriate.
    /// <![CDATA[
    ///     <!-- ServiceStack Yammer OAuth config -->
    ///     <add key="oauth.yammer.ClientId" value=""/>
    ///     <add key="oauth.yammer.ClientSecret" value=""/>
    ///     <add key="oauth.yammer.AccessTokenUrl" value="https://www.yammer.com/oauth2/access_token.json"/>
    ///     <add key="oauth.yammer.CallbackUrl" value="~/"/>
    ///     <add key="oauth.yammer.PreAuthUrl" value="https://www.yammer.com/dialog/oauth"/>
    ///     <add key="oauth.yammer.Realm" value="https://www.yammer.com"/>
    ///     <add key="oauth.yammer.RedirectUrl" value="~/auth/yammer"/>
    /// ]]>
    /// </para>
    /// </remarks>
    public class YammerAuthProvider : OAuthProvider
    {
        /// <summary>
        /// The OAuth provider name / identifier.
        /// </summary>
        public const string Name = "yammer";

        /// <summary>
        /// Initializes a new instance of the <see cref="YammerAuthProvider"/> class.
        /// </summary>
        /// <param name="appSettings">
        /// The application settings (in web.config).
        /// </param>
        public YammerAuthProvider(IAppSettings appSettings)
            : base(appSettings, appSettings.GetString("oauth.yammer.Realm"), Name, "ClientId", "AppSecret")
        {
            this.ClientId = appSettings.GetString("oauth.yammer.ClientId");
            this.ClientSecret = appSettings.GetString("oauth.yammer.ClientSecret");
            this.PreAuthUrl = appSettings.GetString("oauth.yammer.PreAuthUrl");

            NavItem = new NavItem {
                Href = "/auth/" + Name,
                Label = "Sign In with Yammer",
                Id = "btn-" + Name,
                ClassName = "btn-social btn-yammer",
                IconClass = "fab svg-yammer",
            };
        }

        /// <summary>
        /// Gets or sets the Yammer OAuth client id.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets the Yammer OAuth client secret.
        /// </summary>
        public string ClientSecret { get; set; }

        /// <summary>
        /// Gets or sets the Yammer OAuth pre-auth url.
        /// </summary>
        public string PreAuthUrl { get; set; }

        /// <summary>
        /// Authenticate against Yammer OAuth endpoint.
        /// </summary>
        /// <param name="authService">
        /// The auth service.
        /// </param>
        /// <param name="session">
        /// The session.
        /// </param>
        /// <param name="request">
        /// The request.
        /// </param>
        /// <param name="token"></param>
        /// <returns>
        /// The <see cref="object"/>.
        /// </returns>
        public override async Task<object> AuthenticateAsync(IServiceBase authService, IAuthSession session, Authenticate request, CancellationToken token = default)
        {
            var tokens = this.Init(authService, ref session, request);
            var ctx = CreateAuthContext(authService, session, tokens);

            // Check if this is a callback from Yammer OAuth,
            // if not, get the code.
            var code = authService.Request.QueryString["code"];
            var isPreAuthCallback = !code.IsNullOrEmpty();
            if (!isPreAuthCallback)
            {
                var preAuthUrl = $"{this.PreAuthUrl}?client_id={this.ClientId}&redirect_uri={this.RedirectUrl.UrlEncode()}";

                await this.SaveSessionAsync(authService, session, SessionExpiry, token).ConfigAwait();
                return authService.Redirect(PreAuthUrlFilter(ctx, preAuthUrl));
            }

            // If access code exists, get access token to be able to call APIs.
            var accessTokenUrl = $"{this.AccessTokenUrl}?client_id={this.ClientId}&client_secret={this.ClientSecret}&code={code}";

            try
            {
                // Get access response object
                var contents = await AccessTokenUrlFilter(ctx, accessTokenUrl).GetStringFromUrlAsync().ConfigAwait();

                var authInfo = PclExportClient.Instance.ParseQueryString(contents);
                var authObj = JsonObject.Parse(contents);
                var accessToken = authObj.Object("access_token");
                var userInfo = authObj.Object("user");

                // Save info into user session
                tokens.AccessToken = accessToken.Get("token");
                tokens.UserId = accessToken.Get("user_id");
                tokens.UserName = userInfo.Get("name");
                tokens.DisplayName = userInfo.Get("full_name");
                tokens.FullName = userInfo.Get("full_name");
                tokens.FirstName = userInfo.Get("first_name");
                tokens.LastName = userInfo.Get("last_name");

                var emails = userInfo.Object("contact").ArrayObjects("email_addresses").ConvertAll(x =>
                    new EmailAddresses
                    {
                        Type = x.Get("type"),
                        Address = x.Get("address")
                    });

                var email = emails.FirstOrDefault(q => q.Type == "primary");
                if (email != null)
                {
                    tokens.Email = email.Address;
                }

                // Save session info incl. login state
                session.UserName = tokens.UserName;
                session.Email = tokens.Email;
                session.FirstName = tokens.FirstName;
                session.LastName = tokens.LastName;

                session.IsAuthenticated = true;

                // Pass along
                var response = await OnAuthenticatedAsync(authService, session, tokens, authInfo.ToDictionary(), token).ConfigAwait();
                if (response != null)
                    return response;

                // Has access!
                return await authService.Redirect(SuccessRedirectUrlFilter(ctx, this.CallbackUrl.SetParam("s", "1"))).SuccessAuthResultAsync(authService,session).ConfigAwait();
            }
            catch (WebException webEx)
            {
                var statusCode = ((HttpWebResponse)webEx.Response).StatusCode;
                if (statusCode == HttpStatusCode.BadRequest)
                {
                    return authService.Redirect(FailedRedirectUrlFilter(ctx, this.CallbackUrl.SetParam("f", "AccessTokenFailed")));
                }
            }

            // Unknown error, shouldn't get here.
            return authService.Redirect(FailedRedirectUrlFilter(ctx, this.CallbackUrl.SetParam("f", "Unknown")));
        }

        /// <summary>
        /// Load the UserAuth info into the session.
        /// </summary>
        /// <param name="userSession">
        /// The User session.
        /// </param>
        /// <param name="tokens">
        /// The OAuth tokens.
        /// </param>
        /// <param name="authInfo">
        /// The auth info.
        /// </param>
        /// <param name="token"></param>
        protected override async Task LoadUserAuthInfoAsync(AuthUserSession userSession, IAuthTokens tokens, Dictionary<string, string> authInfo, CancellationToken token = default)
        {
            try
            {
                var contents = await AuthHttpGateway.DownloadYammerUserInfoAsync(tokens.UserId).ConfigAwait();

                var obj = JsonObject.Parse(contents);

                tokens.UserId = obj.Get("id");
                tokens.UserName = obj.Get("name");
                tokens.DisplayName = obj.Get("full_name");
                tokens.FullName = obj.Get("full_name");
                tokens.FirstName = obj.Get("first_name");
                tokens.LastName = obj.Get("last_name");

                var emails = obj.Object("contact").ArrayObjects("email_addresses").ConvertAll(x =>
                    new EmailAddresses
                    {
                        Type = x.Get("type"),
                        Address = x.Get("address")
                    });

                var email = emails.FirstOrDefault(q => q.Type == "primary");
                if (email != null)
                {
                    tokens.Email = email.Address;
                }

                if (SaveExtendedUserInfo)
                {
                    obj.Each(x => authInfo[x.Key] = x.Value);
                }
                userSession.UserAuthName = tokens.Email;
            }
            catch (Exception ex)
            {
                Log.Error($"Could not retrieve Yammer user info for '{tokens.DisplayName}'", ex);
            }

            await LoadUserOAuthProviderAsync(userSession, tokens).ConfigAwait();
        }

        /// <summary>
        /// Load the UserOAuth info into the session.
        /// </summary>
        /// <param name="authSession">
        /// The auth session.
        /// </param>
        /// <param name="tokens">
        /// The OAuth tokens.
        /// </param>
        public override Task LoadUserOAuthProviderAsync(IAuthSession authSession, IAuthTokens tokens)
        {
            if (authSession is AuthUserSession userSession)
            {
                userSession.UserAuthId = tokens.UserId ?? userSession.UserAuthId;
                userSession.UserAuthName = tokens.UserName ?? userSession.UserAuthName;
                userSession.DisplayName = tokens.DisplayName ?? userSession.DisplayName;
                userSession.FirstName = tokens.FirstName ?? userSession.FirstName;
                userSession.LastName = tokens.LastName ?? userSession.LastName;
                userSession.PrimaryEmail = tokens.Email ?? userSession.PrimaryEmail ?? userSession.Email;
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The Yammer User's email addresses.
    /// </summary>
    public class EmailAddresses
    {
        /// <summary>
        /// Gets or sets the email address type (e.g. primary).
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the email address.
        /// </summary>
        public string Address { get; set; }
    }
}
