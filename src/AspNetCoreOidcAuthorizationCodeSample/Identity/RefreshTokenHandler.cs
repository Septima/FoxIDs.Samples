﻿using ITfoxtec.Identity.Discovery;
using ITfoxtec.Identity.Messages;
using ITfoxtec.Identity.Tokens;
using ITfoxtec.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using AspNetCoreOidcAuthorizationCodeSample.Models;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreOidcAuthorizationCodeSample.Identity
{
    public static class RefreshTokenHandler
    {
        public static async Task<TokenResponse> ResolveRefreshToken(CookieValidatePrincipalContext context, IdentitySettings identitySettings)
        {
            var tokenRequest = new TokenRequest
            {
                GrantType = IdentityConstants.GrantTypes.RefreshToken,
                RefreshToken = context.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken),
                ClientId = identitySettings.ClientId,
            };
            var clientCredentials = new ClientCredentials
            {
                ClientSecret = identitySettings.ClientSecret,
            };

            var oidcDiscoveryHandler = context.HttpContext.RequestServices.GetService<OidcDiscoveryHandler>();
            var oidcDiscovery = await oidcDiscoveryHandler.GetOidcDiscoveryAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, oidcDiscovery.TokenEndpoint);
            request.Content = new FormUrlEncodedContent(tokenRequest.ToDictionary().AddToDictionary(clientCredentials));

            var httpClientFactory = context.HttpContext.RequestServices.GetService<IHttpClientFactory>();

            var client = httpClientFactory.CreateClient();
            var response = await client.SendAsync(request);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var result = await response.Content.ReadAsStringAsync();
                    var tokenResponse = result.ToObject<TokenResponse>();
                    tokenResponse.Validate(true);
                    if (tokenResponse.AccessToken.IsNullOrEmpty()) throw new ArgumentNullException(nameof(tokenResponse.AccessToken), tokenResponse.GetTypeName());
                    if (tokenResponse.ExpiresIn <= 0) throw new ArgumentNullException(nameof(tokenResponse.ExpiresIn), tokenResponse.GetTypeName());

                    var oidcDiscoveryKeySet = await oidcDiscoveryHandler.GetOidcDiscoveryKeysAsync();
                    (var newPrincipal, var newSecurityToken) = JwtHandler.ValidateToken(tokenResponse.IdToken, oidcDiscovery.Issuer, oidcDiscoveryKeySet.Keys, identitySettings.ClientId);
                    if (context.Principal.Claims.Where(c => c.Type == JwtClaimTypes.Subject).Single().Value != newPrincipal.Claims.Where(c => c.Type == JwtClaimTypes.Subject).Single().Value)
                    {
                        throw new Exception("New principal has invalid sub claim.");
                    }

                    return tokenResponse;

                case HttpStatusCode.BadRequest:
                    var resultBadRequest = await response.Content.ReadAsStringAsync();
                    var tokenResponseBadRequest = resultBadRequest.ToObject<TokenResponse>();
                    tokenResponseBadRequest.Validate(true);
                    throw new Exception($"Error, Bad request. StatusCode={response.StatusCode}");

                default:
                    throw new Exception($"Error, Status Code not expected. StatusCode={response.StatusCode}");
            }
        }
    }
}
