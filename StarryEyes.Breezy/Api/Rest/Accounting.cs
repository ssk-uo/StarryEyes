﻿using System;
using System.Collections.Generic;
using StarryEyes.Breezy.Api.Parsing;
using StarryEyes.Breezy.Authorize;
using StarryEyes.Breezy.DataModel;

namespace StarryEyes.Breezy.Api.Rest
{
    public static class Accounting
    {
        public static IObservable<TwitterUser> VerifyCredential(this AuthenticateInfo authinfo)
        {
            return authinfo.GetOAuthClient()
                .SetEndpoint(ApiEndpoint.EndpointApiV1a.JoinUrl("account/verify_credentials.json"))
                .GetResponse()
                .UpdateRateLimitInfo(authinfo)
                .ReadUser();
        }

        public static IObservable<TwitterUser> UpdateProfile(this AuthenticateInfo info,
            string name = null, string url = null, string location = null, string description = null,
            bool? include_entities = true, bool? skip_status = null)
        {
            var param = new Dictionary<string, object>()
            {
                {"url", url},
                {"location", location},
                {"description", description},
                {"include_entities", include_entities},
                {"skip_status", skip_status},
            }.Parametalize();
            return info.GetOAuthClient()
                .SetEndpoint(ApiEndpoint.EndpointApiV1a.JoinUrl("/account/update_profile.json"))
                .SetParameters(param)
                .SetMethodType(Codeplex.OAuth.MethodType.Post)
                .GetResponse()
                .ReadUser();
        }

        // TODO: UpdateProfileImage
    }
}
