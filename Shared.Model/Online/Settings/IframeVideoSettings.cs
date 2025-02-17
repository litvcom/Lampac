﻿using Shared.Model.Base;

namespace Lampac.Models.LITE
{
    public class IframeVideoSettings : BaseSettings
    {
        public IframeVideoSettings(string plugin, string host, string cdnhost, bool enable = true)
        {
            this.cdnhost = cdnhost;
            this.enable = enable;
            this.plugin = plugin;

            if (host != null)
                apihost = host.StartsWith("http") ? host : Decrypt(host);
        }

        public string cdnhost { get; set; }

        public string? token { get; set; }
    }
}
