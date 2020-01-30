﻿using System;
using System.Net;

namespace AARC.Mesh.TCP
{
    public class ServiceUrlFactory
    {
        protected readonly Uri _url;

        public ServiceUrlFactory()
        {
            _url = new Uri(Dns.GetHostName());
        }

        public string TransportId => _url.AbsoluteUri;
    }
}
