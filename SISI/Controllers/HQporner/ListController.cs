﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Lampac.Models.SISI;
using Lampac.Engine.CORE;
using Shared.Engine.SISI;
using Shared.Engine.CORE;
using SISI;

namespace Lampac.Controllers.HQporner
{
    public class ListController : BaseSisiController
    {
        [HttpGet]
        [Route("hqr")]
        async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1)
        {
            var init = AppInit.conf.HQporner.Clone();

            if (!init.enable)
                return OnError("disable");

            if (NoAccessGroup(init, out string error_msg))
                return OnError(error_msg, false);

            if (IsOverridehost(init, out string overridehost))
                return Redirect(overridehost);

            string memKey = $"hqr:{search}:{sort}:{c}:{pg}";
            if (!hybridCache.TryGetValue(memKey, out List<PlaylistItem> playlists))
            {
                var proxyManager = new ProxyManager("hqr", init);
                var proxy = proxyManager.Get();

                var rch = new RchClient(HttpContext, host, init, requestInfo);
                if (rch.IsNotSupport("web", out string rch_error))
                    return OnError(rch_error, false);

                if (rch.IsNotConnected())
                    return ContentTo(rch.connectionMsg);

                string html = await HQpornerTo.InvokeHtml(init.corsHost(), search, sort, c, pg, url =>
                    rch.enable ? rch.Get(init.cors(url), httpHeaders(init)) : HttpClient.Get(init.cors(url), timeoutSeconds: 10, proxy: proxy, headers: httpHeaders(init))
                );

                if (html == null)
                    return OnError("html", rch.enable ? null : proxyManager, string.IsNullOrEmpty(search));

                playlists = HQpornerTo.Playlist($"{host}/hqr/vidosik", html);

                if (playlists.Count == 0)
                    return OnError("playlists", proxyManager, pg > 1 && string.IsNullOrEmpty(search));

                if (!rch.enable)
                    proxyManager.Success();

                hybridCache.Set(memKey, playlists, cacheTime(10, init: init));
            }

            return OnResult(playlists, string.IsNullOrEmpty(search) ? HQpornerTo.Menu(host, sort, c) : null, plugin: "hqr");
        }
    }
}
