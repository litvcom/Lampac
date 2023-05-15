﻿using System.Collections.Concurrent;

namespace Lampac.Models
{
    public class ProxySettings
    {
        public string? name;

        public string? pattern;

        public bool useAuth;

        public bool BypassOnLocal;

        public string? username;

        public string? password;

        public string? refresh_uri;

        public ConcurrentBag<string>? list;
    }
}
