﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Lampac.Engine;
using Lampac.Engine.CORE;
using System.Web;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Shared.Engine.CORE;

namespace Lampac.Controllers.LITE
{
    public class AnimeGo : BaseController
    {
        ProxyManager proxyManager = new ProxyManager("animego", AppInit.conf.AnimeGo);

        [HttpGet]
        [Route("lite/animego")]
        async public Task<ActionResult> Index(string title, int year, int pid, int s, string t, string account_email)
        {
            if (!AppInit.conf.AnimeGo.enable || string.IsNullOrWhiteSpace(title))
                return Content(string.Empty);

            bool firstjson = true;
            string html = "<div class=\"videos__line\">";

            if (pid == 0)
            {
                #region Поиск
                string memkey = $"animego:search:{title}";
                if (!memoryCache.TryGetValue(memkey, out List<(string title, string pid, string s)> catalog))
                {
                    string search = await HttpClient.Get($"{AppInit.conf.AnimeGo.host}/search/anime?q={HttpUtility.UrlEncode(title)}", timeoutSeconds: 10, proxy: proxyManager.Get());
                    if (search == null)
                    {
                        proxyManager.Refresh();
                        return Content(string.Empty);
                    }

                    catalog = new List<(string title, string pid, string s)>();

                    foreach (string row in search.Split("class=\"p-poster__stack\"").Skip(1))
                    {
                        string player_id = Regex.Match(row, "data-ajax-url=\"/[^\"]+-([0-9]+)\"").Groups[1].Value;
                        string name = Regex.Match(row, "card-title text-truncate\"><a [^>]+>([^<]+)<").Groups[1].Value;
                        string animeyear = Regex.Match(row, "class=\"anime-year\"><a [^>]+>([0-9]{4})<").Groups[1].Value;

                        if (!string.IsNullOrWhiteSpace(player_id) && !string.IsNullOrWhiteSpace(name) && name.ToLower().Contains(title.ToLower()))
                        {
                            string season = "0";
                            if (animeyear == year.ToString() && name.ToLower() == title.ToLower())
                                season = "1";

                            catalog.Add((name, player_id, season));
                        }
                    }

                    if (catalog.Count == 0)
                    {
                        proxyManager.Refresh();
                        return Content(string.Empty);
                    }

                    memoryCache.Set(memkey, catalog, DateTime.Now.AddMinutes(AppInit.conf.multiaccess ? 40 : 10));
                }

                if (catalog.Count == 1)
                    return LocalRedirect($"/lite/animego?title={HttpUtility.UrlEncode(title)}&pid={catalog[0].pid}&s={catalog[0].s}&account_email={HttpUtility.UrlEncode(account_email)}");

                foreach (var res in catalog)
                {
                    string link = $"{host}/lite/animego?title={HttpUtility.UrlEncode(title)}&pid={res.pid}&s={res.s}";

                    html += "<div class=\"videos__item videos__season selector " + (firstjson ? "focused" : "") + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\",\"similar\":true}'><div class=\"videos__season-layers\"></div><div class=\"videos__item-imgbox videos__season-imgbox\"><div class=\"videos__item-title videos__season-title\">" + res.title + "</div></div></div>";
                    firstjson = false;
                }
                #endregion
            }
            else 
            {
                #region Серии
                string memKey = $"animego:playlist:{pid}";
                if (!memoryCache.TryGetValue(memKey, out (string translation, List<(string episode, string uri)> links, List<(string name, string id)> translations) cache))
                {
                    #region content
                    var player = await HttpClient.Get<JObject>($"{AppInit.conf.AnimeGo.host}/anime/{pid}/player?_allow=true", timeoutSeconds: 10, proxy: proxyManager.Get(), addHeaders: new List<(string name, string val)>() 
                    {
                        ("cache-control", "no-cache"),
                        ("dnt", "1"),
                        ("pragma", "no-cache"),
                        ("referer", $"{AppInit.conf.AnimeGo.host}/"),
                        ("sec-fetch-dest", "empty"),
                        ("sec-fetch-mode", "cors"),
                        ("sec-fetch-site", "same-origin"),
                        ("x-requested-with", "XMLHttpRequest")
                    });

                    string content = player?.Value<string>("content");
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        proxyManager.Refresh();
                        return Content(string.Empty);
                    }
                    #endregion

                    var g = Regex.Match(content, "data-player=\"(https?:)?//(aniboom\\.[^/]+)/embed/([^\"\\?&]+)\\?episode=1\\&amp;translation=([0-9]+)\"").Groups;
                    if (string.IsNullOrWhiteSpace(g[2].Value) || string.IsNullOrWhiteSpace(g[3].Value) || string.IsNullOrWhiteSpace(g[4].Value))
                    {
                        proxyManager.Refresh();
                        return Content(string.Empty);
                    }

                    #region links
                    cache.links = new List<(string episode, string uri)>();
                    var match = Regex.Match(content, "data-episode=\"([0-9]+)\"");
                    while (match.Success)
                    {
                        if (!string.IsNullOrWhiteSpace(match.Groups[1].Value))
                            cache.links.Add((match.Groups[1].Value, $"video.m3u8?host={g[2].Value}&token={g[3].Value}&e={match.Groups[1].Value}"));

                        match = match.NextMatch();
                    }

                    if (cache.links.Count == 0)
                    {
                        proxyManager.Refresh();
                        return Content(string.Empty);
                    }
                    #endregion

                    #region translation / translations
                    cache.translation = g[4].Value;
                    cache.translations = new List<(string name, string id)>();

                    match = Regex.Match(content, "data-player=\"(https?:)?//aniboom\\.[^/]+/embed/[^\"\\?&]+\\?episode=[0-9]+\\&amp;translation=([0-9]+)\"[\n\r\t ]+data-provider=\"[0-9]+\"[\n\r\t ]+data-provide-dubbing=\"([0-9]+)\"");
                    while (match.Success)
                    {
                        if (!string.IsNullOrWhiteSpace(match.Groups[2].Value) && !string.IsNullOrWhiteSpace(match.Groups[3].Value))
                        {
                            string name = Regex.Match(content, $"data-dubbing=\"{match.Groups[3].Value}\"><span [^>]+>[\n\r\t ]+([^\n\r<]+)").Groups[1].Value.Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                                cache.translations.Add((name, match.Groups[2].Value));
                        }

                        match = match.NextMatch();
                    }
                    #endregion

                    memoryCache.Set(memKey, cache, DateTime.Now.AddMinutes(AppInit.conf.multiaccess ? 30 : 10));
                }

                #region Перевод
                if (string.IsNullOrWhiteSpace(t))
                    t = cache.translation;

                foreach (var translation in cache.translations)
                {
                    string link = $"{host}/lite/animego?pid={pid}&title={HttpUtility.UrlEncode(title)}&s={s}&t={translation.id}";
                    string active = t == translation.id ? "active" : "";

                    html += "<div class=\"videos__button selector " + active + "\" data-json='{\"method\":\"link\",\"url\":\"" + link + "\"}'>" + translation.name + "</div>";
                }

                html += "</div><div class=\"videos__line\">";
                #endregion

                foreach (var l in cache.links)
                {
                    string hls = $"{host}/lite/animego/{l.uri}&t={t ?? cache.translation}&account_email={HttpUtility.UrlEncode(account_email)}";

                    html += "<div class=\"videos__item videos__movie selector " + (firstjson ? "focused" : "") + "\" media=\"\" s=\"" + s + "\" e=\"" + l.episode + "\" data-json='{\"method\":\"play\",\"url\":\"" + hls + "\",\"title\":\"" + $"{title} ({l.episode} серия)" + "\"}'><div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">" + $"{l.episode} серия" + "</div></div>";
                    firstjson = true;
                }
                #endregion
            }

            return Content(html + "</div>", "text/html; charset=utf-8");
        }


        #region Video
        [HttpGet]
        [Route("lite/animego/video.m3u8")]
        async public Task<ActionResult> Video(string host, string token, string t, int e)
        {
            if (!AppInit.conf.AnimeGo.enable)
                return Content(string.Empty);

            string memKey = $"animego:video:{token}:{t}:{e}";
            if (!memoryCache.TryGetValue(memKey, out string hls))
            {
                string embed = await HttpClient.Get($"https://{host}/embed/{token}?episode={e}&translation={t}", timeoutSeconds: 10, proxy: proxyManager.Get(), addHeaders: new List<(string name, string val)>()
                {
                    ("cache-control", "no-cache"),
                    ("dnt", "1"),
                    ("pragma", "no-cache"),
                    ("referer", $"{AppInit.conf.AnimeGo.host}/"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-origin"),
                    ("x-requested-with", "XMLHttpRequest")
                });

                if (string.IsNullOrWhiteSpace(embed))
                {
                    proxyManager.Refresh();
                    return Content(string.Empty);
                }

                embed = embed.Replace("&quot;", "\"").Replace("\\", "");

                hls = Regex.Match(embed, "\"hls\":\"\\{\"src\":\"(https?:)?(//[^\"]+\\.m3u8)\"").Groups[2].Value;
                if (string.IsNullOrWhiteSpace(hls))
                {
                    proxyManager.Refresh();
                    return Content(string.Empty);
                }

                hls = "https:" + hls;
                memoryCache.Set(memKey, hls, DateTime.Now.AddMinutes(AppInit.conf.multiaccess ? 30 : 10));
            }

            return Redirect(HostStreamProxy(true, hls, new List<(string, string)>() 
            {
                ("origin", "https://aniboom.one"),
                ("referer", "https://aniboom.one/")
            }));
        }
        #endregion
    }
}
